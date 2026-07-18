namespace Wallflow;

/// <summary>Paliers de vitesse affichés dans l'UI. Le backend (Settings.Speed) reste continu 0.25–4.0 ;
/// seule la présentation se restreint à ces valeurs nommées.</summary>
public static class SpeedPaliers
{
    public static readonly double[] Values = [0.5, 1.0, 1.5, 2.0];

    /// <summary>Palier le plus proche de <paramref name="speed"/>. Hors plage → borne la plus proche.
    /// Égalité (ex. 1.75, pile entre 1.5 et 2.0) → palier supérieur.</summary>
    public static double Nearest(double speed) =>
        Values.OrderBy(v => Math.Abs(v - speed)).ThenByDescending(v => v).First();
}
