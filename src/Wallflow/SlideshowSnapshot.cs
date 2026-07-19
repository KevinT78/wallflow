namespace Wallflow;

/// <summary>
/// Config capturée d'un diaporama Windows natif — juste ce qu'il faut pour le relancer à
/// l'identique (dossier d'images, intervalle, mélange). Record = égalité par valeur (tests).
/// </summary>
public sealed record SlideshowSnapshot(string FolderPath, uint IntervalMs, bool Shuffle);
