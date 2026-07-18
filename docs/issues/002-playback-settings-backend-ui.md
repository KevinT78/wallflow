# Issue 002 — Tous les réglages de lecture : modèle + backend + UI fenêtre

> **Statut : ✅ Implémentée** (commits ce90784, f52f6ef, 46b6b16, dfd73bf)

## What to build

Ajouter les 4 réglages de lecture (Volume + Mute, Cadrage, Boucle, Vitesse) de bout en bout :
persistance dans `Settings`, propagation via `IPlayerManager` jusqu'à `MpvPlayer`, et contrôles
UI dans la fenêtre principale.

Couverture fonctionnelle :

- **Volume** : slider 0-100 + toggle muet, appliqué via mpv `volume` / `mute`
- **Cadrage** : radio boutons Cover / Fit / Fill, appliqué via mpv `panscan` + `video-fit`
- **Boucle** : toggle on/off, appliqué via mpv `loop-file`
- **Vitesse** : slider continu 0.25x–4x par pas de 0.25, appliqué via mpv `speed`

Les réglages sont globaux (pas par wallpaper) et persistés dans `settings.json`. Appliqués
immédiatement sans redémarrage.

## Acceptance criteria

- [x] `Settings` a les 5 nouvelles propriétés avec leurs valeurs par défaut :
      `Volume=100`, `Muted=false`, `VideoFit="cover"`, `Loop=true`, `Speed=1.0`
- [x] `MpvPlayer` a des méthodes `ApplyVolume`, `ApplyVideoFit`, `ApplyLoop`, `ApplySpeed`
      qui modifient les propriétés mpv à chaud
- [x] `MpvPlayer` applique les settings au moment du `Load` (constructeur ou appel explicite)
- [x] `IPlayerManager.ApplySettings(Settings)` propage à tous les players
- [x] `AppService.ApplyPlaybackSettings()` sauvegarde `Settings` + appelle
      `PlayerManager.ApplySettings`
- [x] UI fenêtre : une section de réglages entre la grille des récents et les toggles existants
      avec slider volume + mute, radios cadrage, toggle boucle, slider vitesse
- [x] Les contrôles UI sont initialisés depuis les settings chargés au démarrage
- [x] Tout changement dans l'UI déclenche `AppService.ApplyPlaybackSettings()`
- [x] Les valeurs hors limites sont clampées
- [x] `dotnet build` réussi
- [x] Tests : `Settings` round-trip JSON (valeurs par défaut, limites toutes les propriétés)
- [x] Tests : `AppService.ApplyPlaybackSettings()` propage bien à `IPlayerManager` (mock)

## Blocked by

- Issue 001
