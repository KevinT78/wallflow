using System.Runtime.InteropServices;

namespace Wallflow;

/// <summary>
/// Enveloppe minimale de libmpv en mode embedding (option wid) : mpv rend directement
/// dans le HWND hôte fourni par WallpaperHost. Défauts figés par DESIGN.md :
/// boucle infinie, muet, cadrage cover, hwdec auto. Un player par écran.
/// </summary>
public sealed class MpvPlayer : IDisposable
{
    private const string Lib = "libmpv";

    static MpvPlayer()
    {
        // Le nom de la DLL varie selon les builds : libmpv-2.dll (shinchiro récents) ou mpv-2.dll.
        NativeLibrary.SetDllImportResolver(typeof(MpvPlayer).Assembly, (name, assembly, path) =>
        {
            if (name != Lib) return IntPtr.Zero;
            foreach (var candidate in new[] { "libmpv-2.dll", "mpv-2.dll" })
                if (NativeLibrary.TryLoad(candidate, assembly, path, out var handle))
                    return handle;
            return IntPtr.Zero;
        });
    }

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr mpv_create();

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern int mpv_initialize(IntPtr ctx);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern int mpv_set_option_string(IntPtr ctx,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern int mpv_set_property_string(IntPtr ctx,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern int mpv_command(IntPtr ctx, IntPtr[] args);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern int mpv_observe_property(IntPtr ctx, ulong replyUserdata,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int format);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr mpv_wait_event(IntPtr ctx, double timeout);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern void mpv_terminate_destroy(IntPtr ctx);

    // Constantes stables de client.h (cf. mpv/mpv.def) — seuls les slots utilisés sont déclarés.
    private const int MpvEventShutdown = 1;
    private const int MpvEventFileLoaded = 8;
    private const int MpvEventPropertyChange = 22;
    private const int MpvFormatString = 1;

    private const ulong PlaybackErrorUdata = 1;

    [StructLayout(LayoutKind.Sequential)]
    private struct MpvEvent
    {
        public int EventId;
        public int Error;
        public ulong ReplyUserdata;
        public IntPtr Data;
        public ulong Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MpvEventProperty
    {
        public IntPtr Name;
        public int Format;
        public IntPtr Data;
    }

    private IntPtr _ctx;
    private Thread? _eventThread;

    /// <summary>Émis (thread des événements mpv) quand la lecture d'un fichier échoue — mpv ne signale
    /// jamais ça via le code retour de loadfile (le chargement est asynchrone). Consommé par
    /// PlayerManager → AppService → Snackbar.</summary>
    public event Action<string>? PlaybackError;

    public MpvPlayer(IntPtr parentHwnd)
    {
        _ctx = mpv_create();
        if (_ctx == IntPtr.Zero)
            throw new InvalidOperationException("mpv_create a échoué — libmpv-2.dll est-elle à côté de wallflow.exe ?");

        SetOption("wid", parentHwnd.ToInt64().ToString());
        SetOption("loop-file", "inf");
        SetOption("mute", "yes");
        SetOption("panscan", "1.0");                 // cover : remplit l'écran, rogne les bords
        SetOption("hwdec", "auto");
        SetOption("image-display-duration", "inf");  // les images fixes restent affichées
        SetOption("osc", "no");
        SetOption("input-default-bindings", "no");
        SetOption("input-vo-keyboard", "no");

        if (mpv_initialize(_ctx) < 0)
            throw new InvalidOperationException("mpv_initialize a échoué");

        // Best-effort : si le build mpv ignore la propriété playback-error (mpv < 0.35),
        // aucun événement n'arrivera et on retombe sur le comportement silencieux d'avant.
        mpv_observe_property(_ctx, PlaybackErrorUdata, "playback-error", MpvFormatString);
        _eventThread = new Thread(RunEventLoop) { IsBackground = true, Name = "mpv-events" };
        _eventThread.Start();
    }

    /// <summary>Boucle d'événements : seule thread qui appelle mpv_wait_event (règle client.h).
    /// Repère l'échec de chargement via la propriété playback-error — le seul signal fiable
    /// (loadfile retourne 0 même quand le décodage échoue ensuite).</summary>
    private void RunEventLoop()
    {
        var ctx = _ctx;
        while (true)
        {
            var evPtr = mpv_wait_event(ctx, -1.0);
            if (evPtr == IntPtr.Zero) continue;

            var ev = Marshal.PtrToStructure<MpvEvent>(evPtr);
            if (ev.EventId == MpvEventShutdown) return;
            if (ev.EventId == MpvEventPropertyChange && ev.ReplyUserdata == PlaybackErrorUdata)
                ReadPlaybackError(ev.Data);
            // MpvEventFileLoaded : la propriété playback-error est remise à vide par mpv lui-même,
            // aucun nettoyage à faire de notre côté.
        }
    }

    private void ReadPlaybackError(IntPtr propertyPtr)
    {
        var prop = Marshal.PtrToStructure<MpvEventProperty>(propertyPtr);
        if (prop.Format != MpvFormatString || prop.Data == IntPtr.Zero) return;
        var message = Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(prop.Data));
        if (!string.IsNullOrEmpty(message))
            PlaybackError?.Invoke(message);
    }

    private void SetOption(string name, string value) => mpv_set_option_string(_ctx, name, value);

    public void Load(string path)
    {
        var code = Command("loadfile", path);
        if (code < 0)
            PlaybackError?.Invoke($"mpv refuse de charger {path} (erreur {code})");
    }

    public void Pause() => mpv_set_property_string(_ctx, "pause", "yes");

    public void Resume() => mpv_set_property_string(_ctx, "pause", "no");

    public void ApplyVolume(int vol, bool muted)
    {
        mpv_set_property_string(_ctx, "volume", Math.Clamp(vol, 0, 100).ToString());
        mpv_set_property_string(_ctx, "mute", muted ? "yes" : "no");
    }

    // mpv n'a pas de propriété « video-fit » : le cadrage se pilote via panscan + keepaspect.
    public void ApplyVideoFit(string fit)
    {
        switch (fit)
        {
            case "cover": // remplit l'écran, rogne les bords
                mpv_set_property_string(_ctx, "panscan", "1.0");
                mpv_set_property_string(_ctx, "keepaspect", "yes");
                break;
            case "fit": // letterbox, tout visible
                mpv_set_property_string(_ctx, "panscan", "0");
                mpv_set_property_string(_ctx, "keepaspect", "yes");
                break;
            case "fill": // étire sans respecter le ratio
                mpv_set_property_string(_ctx, "panscan", "0");
                mpv_set_property_string(_ctx, "keepaspect", "no");
                break;
        }
    }

    public void ApplyLoop(bool loop) =>
        mpv_set_property_string(_ctx, "loop-file", loop ? "inf" : "no");

    // InvariantCulture obligatoire : en fr-FR, "F2" seul donne "1,50" et mpv rejette la valeur.
    public void ApplySpeed(double speed) =>
        mpv_set_property_string(_ctx, "speed",
            Math.Clamp(speed, 0.25, 4.0).ToString("F2", System.Globalization.CultureInfo.InvariantCulture));

    private int Command(params string[] args)
    {
        // mpv_command attend un char*[] UTF-8 terminé par NULL.
        var ptrs = new IntPtr[args.Length + 1];
        try
        {
            for (var i = 0; i < args.Length; i++)
                ptrs[i] = Marshal.StringToCoTaskMemUTF8(args[i]);
            return mpv_command(_ctx, ptrs);
        }
        finally
        {
            foreach (var p in ptrs)
                if (p != IntPtr.Zero) Marshal.FreeCoTaskMem(p);
        }
    }

    public void Dispose()
    {
        if (_ctx != IntPtr.Zero)
        {
            var ctx = _ctx;
            _ctx = IntPtr.Zero;
            // mpv_terminate_destroy libère ctx en interne (bloquant) ; l'appeler pendant que
            // RunEventLoop lit encore ce même ctx via mpv_wait_event est une race UB documentée
            // côté mpv (SHUTDOWN jamais vu proprement, thread orphelin, Join timeout à chaque
            // fois). Pattern correct (doc mpv, client.h) : "quit" d'abord, laisser RunEventLoop
            // recevoir SHUTDOWN et sortir naturellement, PUIS seulement terminate_destroy.
            var quitPtr = Marshal.StringToCoTaskMemUTF8("quit");
            mpv_command(ctx, [quitPtr, IntPtr.Zero]);
            Marshal.FreeCoTaskMem(quitPtr);
            _eventThread?.Join(TimeSpan.FromSeconds(2));
            mpv_terminate_destroy(ctx);
        }
    }
}
