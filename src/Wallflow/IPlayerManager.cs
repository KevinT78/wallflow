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
}
