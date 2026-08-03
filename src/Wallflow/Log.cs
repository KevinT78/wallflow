using System.IO;

namespace Wallflow;

/// <summary>
/// Journalisation minimale, sans dépendance : un fichier texte rotatif par jour dans
/// %LOCALAPPDATA%\Wallflow\logs. Tous les chemins best-effort du produit (COM diaporama,
/// ffmpeg, mpv) y tracent leurs échecs — seuls endroits où l'app n'a pas d'autre voix.
/// Thread-safe (lock), ne jette jamais : un échec d'écriture ne doit pas casser l'appelant.
/// Isolation des tests : Enabled (coupe tout) et DirOverride (même pattern que Settings).
/// </summary>
public static class Log
{
    /// <summary>Coupe toute écriture. Isolation des tests uniquement.</summary>
    public static bool Enabled { get; set; } = true;

    /// <summary>Remplace le dossier %LocalAppData%\Wallflow\logs pour l'écriture. Isolation des tests uniquement.</summary>
    public static string? DirOverride { get; set; }

    private static readonly object Gate = new();

    private static string Dir => DirOverride ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Wallflow", "logs");

    public static void Info(string message) => Write("INFO", message, null);
    public static void Warn(string message) => Write("WARN", message, null);
    public static void Error(string message, Exception? ex = null) => Write("ERROR", message, ex);

    private static void Write(string level, string message, Exception? ex)
    {
        if (!Enabled) return;
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level} {message}";
        if (ex != null) line += Environment.NewLine + ex;

        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Dir);
                File.AppendAllText(Path.Combine(Dir, $"wallflow-{DateTime.Now:yyyyMMdd}.log"), line + Environment.NewLine);
            }
        }
        catch (Exception)
        {
            // Un log qui échoue ne doit jamais casser l'appelant (best-effort, comme le reste).
        }
    }
}
