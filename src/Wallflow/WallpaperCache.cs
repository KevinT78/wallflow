using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Wallflow;

/// <summary>
/// Conversion unique GIF/webp → mp4 H.264 en cache (%LOCALAPPDATA%\Wallflow\cache) : le décodage
/// GIF/webp de mpv est 100 % CPU (~27 % mesuré sur la machine de référence), le mp4 converti
/// profite du décodage matériel (~7 %). Les récents et settings gardent le chemin d'origine ;
/// seul le player reçoit le chemin converti. Sans ffmpeg.exe (à côté de l'exe ou dans le PATH),
/// ou si la conversion échoue : l'original joue tel quel — le cache est une optimisation, jamais
/// un point de défaillance. Réconciliation (<see cref="PruneOrphans"/>) après chaque mutation des
/// récents : le dossier de cache ne grossit pas indéfiniment.
/// </summary>
public static class WallpaperCache
{
    /// <summary>Coupe toute conversion. Isolation des tests uniquement (même pattern que
    /// Settings.DirOverride) : sans ça, chaque Apply(.gif) des tests spawnerait un ffmpeg.</summary>
    public static bool Disabled { get; set; }

    /// <summary>Remplace CacheDir. Isolation des tests uniquement (même pattern que Settings.DirOverride).</summary>
    public static string? DirOverride { get; set; }

    private static readonly string[] Convertible = [".gif", ".webp"];

    private static string CacheDir => DirOverride ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Wallflow", "cache");

    public static bool IsConvertible(string path) =>
        Convertible.Contains(Path.GetExtension(path).ToLowerInvariant());

    /// <summary>Chemin du mp4 déjà converti pour ce fichier, ou null (pas convertible, pas encore
    /// converti, ou source modifiée depuis — la clé couvre chemin + taille + date).</summary>
    public static string? TryGet(string path)
    {
        if (Disabled || !IsConvertible(path) || !File.Exists(path)) return null;
        var cached = CachePathFor(path);
        return File.Exists(cached) ? cached : null;
    }

    /// <summary>Convertit en arrière-plan puis appelle onConverted (thread pool) avec le chemin
    /// du mp4. Silencieux sur échec : l'original continue de jouer.</summary>
    public static void ConvertAsync(string path, Action<string> onConverted)
    {
        if (Disabled || !IsConvertible(path) || !File.Exists(path)) return;
        Task.Run(() =>
        {
            try
            {
                var final = CachePathFor(path);
                Directory.CreateDirectory(CacheDir);
                var tmp = Path.Combine(CacheDir, Guid.NewGuid() + ".tmp.mp4");

                // yuv420p + dimensions paires : exigences de compatibilité des décodeurs matériels.
                var psi = new ProcessStartInfo
                {
                    FileName = FindFfmpeg() ?? throw new FileNotFoundException("ffmpeg introuvable"),
                    Arguments = $"-y -loglevel error -i \"{path}\" -pix_fmt yuv420p " +
                                "-vf \"scale=trunc(iw/2)*2:trunc(ih/2)*2\" " +
                                $"-c:v libx264 -preset veryfast -crf 20 -movflags +faststart -an \"{tmp}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var proc = Process.Start(psi)!;
                proc.WaitForExit();
                if (proc.ExitCode != 0 || !File.Exists(tmp) || new FileInfo(tmp).Length == 0)
                {
                    if (proc.ExitCode != 0)
                        Log.Warn($"Conversion échouée ({proc.ExitCode}) : {path}");
                    if (File.Exists(tmp)) File.Delete(tmp);
                    return;
                }

                // Deux conversions simultanées du même fichier : la première gagne, l'autre se jette.
                if (!File.Exists(final)) File.Move(tmp, final);
                else File.Delete(tmp);
                onConverted(final);
            }
            catch (Exception ex)
            {
                // ponytail: échec voulu du cache — l'original joue, pas de cache, on réessaiera au prochain Apply
                Log.Warn($"Conversion en cache échouée pour {path} ({ex.Message})");
            }
        });
    }

    /// <summary>Supprime tout .mp4 du cache qui ne correspond à la clé (chemin+taille+date) d'aucun
    /// wallpaper actif fourni. À appeler après toute mutation qui peut faire sortir un wallpaper des
    /// récents : éviction au-delà de la limite, retrait manuel, purge des sources supprimées au
    /// démarrage. Best-effort par fichier : un verrou (lecture en cours) ne doit pas interrompre la
    /// purge des autres orphelins.</summary>
    public static void PruneOrphans(IEnumerable<string> activePaths)
    {
        if (Disabled || !Directory.Exists(CacheDir)) return;

        var active = activePaths.Where(File.Exists).Select(CachePathFor)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var file in Directory.EnumerateFiles(CacheDir, "*.mp4"))
        {
            // Exclut les conversions en cours (Guid.tmp.mp4) : pas des orphelins, juste pas encore promues.
            if (file.EndsWith(".tmp.mp4", StringComparison.OrdinalIgnoreCase)) continue;
            if (active.Contains(file)) continue;
            try { File.Delete(file); }
            catch (Exception ex) { Log.Warn($"Purge cache orpheline échouée pour {file} ({ex.Message})"); }
        }
    }

    /// <summary>Chemin de cache attendu pour ce fichier source (clé = chemin+taille+date). Public
    /// pour que <see cref="PruneOrphans"/> et les tests puissent le calculer.</summary>
    public static string CachePathFor(string path)
    {
        var info = new FileInfo(path);
        var key = $"{info.FullName.ToLowerInvariant()}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(key)));
        return Path.Combine(CacheDir, hash + ".mp4");
    }

    private static string? FindFfmpeg()
    {
        var local = Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe");
        if (File.Exists(local)) return local;
        // Sinon le PATH : Process.Start résout "ffmpeg" tout seul, mais on veut échouer proprement ici.
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
            if (!string.IsNullOrWhiteSpace(dir) && File.Exists(Path.Combine(dir.Trim(), "ffmpeg.exe")))
                return Path.Combine(dir.Trim(), "ffmpeg.exe");
        return null;
    }
}
