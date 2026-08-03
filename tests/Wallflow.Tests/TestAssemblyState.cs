using System.Runtime.CompilerServices;

namespace Wallflow.Tests;

/// <summary>
/// Neutralise dès le chargement de l'assemblage de test tout accès au réel : journal, clé registre
/// Run, spawn ffmpeg, hook WinEvent. Protection structurelle — elle ne dépend plus du fait qu'un
/// test pense à poser TestIsolation (avant cette garde, des Apply de tests ont pollué le vrai
/// %LOCALAPPDATA%\Wallflow\logs). Le dossier de settings.json reste isolé par TestIsolation
/// (dossier temp unique par test).
/// </summary>
internal static class TestAssemblyState
{
    [ModuleInitializer]
    internal static void Init()
    {
        Log.Enabled = false;
        AppService.SkipRunKey = true;
        WallpaperCache.Disabled = true;
        ActivityMonitor.Disabled = true;
    }
}
