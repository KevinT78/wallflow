namespace Wallflow;

public interface IPlayerManager
{
    void Load(string path);
    void PauseAll();
    void ResumeAll();
    void ApplySettings(Settings settings);
    void Rebuild();
    void Clear();
    void Dispose();

    /// <summary>Coupe le diaporama Windows s'il est actif et retourne sa config capturée, sinon null.</summary>
    SlideshowSnapshot? PauseSlideshowIfActive();

    /// <summary>Relance le diaporama Windows à partir d'une config capturée.</summary>
    void ResumeSlideshow(SlideshowSnapshot snapshot);
}
