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
    private static extern void mpv_terminate_destroy(IntPtr ctx);

    private IntPtr _ctx;

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
    }

    private void SetOption(string name, string value) => mpv_set_option_string(_ctx, name, value);

    public void Load(string path) => Command("loadfile", path);

    public void Pause() => mpv_set_property_string(_ctx, "pause", "yes");

    public void Resume() => mpv_set_property_string(_ctx, "pause", "no");

    private void Command(params string[] args)
    {
        // mpv_command attend un char*[] UTF-8 terminé par NULL.
        var ptrs = new IntPtr[args.Length + 1];
        try
        {
            for (var i = 0; i < args.Length; i++)
                ptrs[i] = Marshal.StringToCoTaskMemUTF8(args[i]);
            mpv_command(_ctx, ptrs);
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
            mpv_terminate_destroy(_ctx);
            _ctx = IntPtr.Zero;
        }
    }
}
