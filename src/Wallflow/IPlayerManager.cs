namespace Wallflow;

public interface IPlayerManager
{
    void Load(string path);
    void PauseAll();
    void ResumeAll();
    void ApplySettings(Settings settings);
    void Rebuild();
    void Resync();
    void ResyncLight();
    void Clear();
    void Dispose();

    /// <summary>Émis quand la lecture d'un fichier échoue (fichier corrompu/illisible) — mpv ne
    /// le signale que via playback-error, jamais par le code retour de loadfile.</summary>
    event Action<string>? PlaybackError;

    /// <summary>Coupe le diaporama Windows s'il est actif et retourne sa config capturée, sinon null.</summary>
    SlideshowSnapshot? PauseSlideshowIfActive();

    /// <summary>Relance le diaporama Windows à partir d'une config capturée.</summary>
    void ResumeSlideshow(SlideshowSnapshot snapshot);
}
