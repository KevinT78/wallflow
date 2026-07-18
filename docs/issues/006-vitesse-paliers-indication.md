# Issue 006 — Vitesse en paliers + indication (volume compris)

> **Statut : ✅ Implémentée** (TDD, 8 tests `SpeedPaliers`). Helper pur `SpeedPaliers.Nearest`,
> 4 boutons radio de vitesse dans la fenêtre, readout `%` du volume. Vérif visuelle du câblage
> UI restant à faire côté utilisateur.

> **Triage** : `ready-for-agent`
> Issu de la session grilling du 2026-07-18.

## Problem Statement

En tant qu'`Utilisateur`, le réglage de **vitesse** n'affiche **aucune valeur** (je ne sais pas à
quelle vitesse je suis) et le curseur donne une impression continue alors que je voudrais des
**crans francs**. Le **volume** de la fenêtre, lui aussi, n'affiche pas son pourcentage.

## Solution

La vitesse se règle par **paliers nommés visibles** (`0.5× · 1× · 1.5× · 2×`), la sélection servant
elle-même d'indication. Le slider de volume de la fenêtre affiche son **pourcentage**.

## User Stories

1. En tant qu'`Utilisateur`, je veux régler la vitesse par des paliers nommés (`0.5× · 1× · 1.5× ·
   2×`), afin de choisir franchement sans viser entre deux crans invisibles.
2. En tant qu'`Utilisateur`, je veux voir en permanence le palier de vitesse actif, afin de savoir
   à quelle vitesse joue mon `Wallpaper`.
3. En tant qu'`Utilisateur`, je veux que `1×` reste la vitesse par défaut, afin de retrouver un
   comportement normal sans réglage.
4. En tant qu'`Utilisateur` ayant déjà une vitesse enregistrée hors palier (ex. `3.0×`), je veux
   que l'UI mette en évidence le palier le plus proche sans écraser ma valeur tant que je ne clique
   pas, afin de ne pas subir un changement silencieux.
5. En tant qu'`Utilisateur`, je veux voir le pourcentage du volume dans la fenêtre, afin de savoir
   où j'en suis comme dans le menu du tray.
6. En tant qu'`Utilisateur`, je veux que les `Réglages de lecture` (volume, cadrage, boucle) et
   leur persistance continuent de fonctionner comme avant, afin qu'aucune régression n'accompagne
   ce changement.

## Implementation Decisions

- Nouveau helper **pur** `SpeedPaliers` : la liste des paliers `[0.5, 1.0, 1.5, 2.0]` et
  `Nearest(double) : double` (palier le plus proche). Seule logique **nouvelle** avec une vraie
  question de correction (arrondi, valeurs hors plage).
- `MainWindow` : remplacer le `Slider` de vitesse par **4 boutons radio** (`0.5× · 1× · 1.5× · 2×`,
  `GroupName` dédié), cohérents avec les radios de `Cadrage` voisines. La sélection **est**
  l'indication. Au refresh, mettre en évidence `SpeedPaliers.Nearest(Settings.Speed)` **sans**
  réécrire `Settings.Speed` tant que l'`Utilisateur` ne clique pas ; un clic pose la valeur du
  palier et déclenche `ApplyPlaybackSettings()`.
- `Settings.Speed` garde son clamp `0.25–4.0` : seul l'affichage se restreint aux paliers, le
  backend reste inchangé.
- Ajouter un **readout `%`** à côté du `Slider` de volume de la fenêtre (même manque d'indication,
  correction identique).

## Testing Decisions

**Principe** : ne tester que le comportement externe. Le câblage UI (boutons radio, readout) est
vérifié visuellement, pas unitairement.

- **`SpeedPaliers.Nearest` — tests purs** (nouveau, valeur réelle) : `1.0 → 1.0`, `1.75 → 2.0`,
  `1.74 → 1.5`, `0.1 → 0.5` (sous la plage), `9.0 → 2.0` (au-dessus), chaque palier exact →
  lui-même. Aucun effet de bord. Prior art : `SettingsTests` (logique pure, clamps).
- **Vérif live/visuelle** : le palier actif est mis en évidence au chargement depuis une valeur
  enregistrée ; cliquer un palier applique la vitesse ; le readout volume affiche le bon `%`.

## Out of Scope

- Paliers supplémentaires (`0.25×`, `3×`, `4×`) : le backend accepte toujours `0.25–4.0`, mais
  l'UI se limite à `0.5 / 1 / 1.5 / 2`.
- Réglages de lecture par `Wallpaper` (ils restent globaux).

## Further Notes

- **Pas d'ADR** : simple raffinement d'UI.
