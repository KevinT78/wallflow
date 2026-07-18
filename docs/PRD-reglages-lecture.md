# PRD — Réglages de lecture configurables

## Problem Statement

Actuellement Wallflow a 4 réglages de lecture figés par DESIGN.md : muet, cadrage cover, boucle
infinie, vitesse 1.0x. L'utilisateur ne peut pas ajuster le volume, changer le mode de cadrage,
désactiver la boucle, ou modifier la vitesse d'un wallpaper animé. Le constat est que ces valeurs
par défaut ne conviennent pas à tous les cas d'usage (certains veulent un wallpaper muet mais pas
cover, d'autres veulent une lecture unique sans boucle).

## Solution

Ajouter 4 réglages de lecture configurables par l'utilisateur : **Volume** (0-100 + muet),
**Cadrage** (Cover / Fit / Fill), **Boucle** (On/Off), **Vitesse** (0.25x–4x). Ces réglages sont
globaux (pas par wallpaper), persistés dans `settings.json`, accessibles depuis une section dédiée
de la fenêtre principale, et le volume est aussi accessible depuis le menu du tray.

## User Stories

1. En tant qu'Utilisateur, je veux régler le volume du wallpaper, pour l'entendre si la vidéo a
   une bande-son intéressante.
2. En tant qu'Utilisateur, je veux pouvoir rendre le wallpaper muet d'un clic, pour couper le son
   rapidement sans repasser par le slider.
3. En tant qu'Utilisateur, je veux choisir entre les modes de cadrage Cover / Fit / Fill, pour que
   l'image s'affiche comme je le souhaite (rognée, avec bandes, ou déformée).
4. En tant qu'Utilisateur, je veux désactiver la boucle pour que la vidéo ne joue qu'une fois,
   pour les contenus qui n'ont pas vocation à tourner en continu.
5. En tant qu'Utilisateur, je veux régler la vitesse de lecture entre 0.25x et 4x, pour ralentir
   ou accélérer l'animation.
6. En tant qu'Utilisateur, je veux que mes réglages soient persistés et restaurés au lancement,
   pour ne pas avoir à les reconfigurer à chaque démarrage.
7. En tant qu'Utilisateur, je veux pouvoir régler rapidement le volume depuis le menu du tray,
   sans ouvrir la fenêtre.
8. En tant qu'Utilisateur, je veux voir l'état du pause manuelle et du volume dans le tray, pour
   savoir d'un coup d'œil si le son est coupé.
9. En tant qu'Utilisateur, je veux que les réglages s'appliquent immédiatement sans redémarrer
   l'application, pour un feedback instantané.

## Implementation Decisions

- **Settings** : 5 nouvelles propriétés dans le POCO `Settings` :
  - `Volume` (int, 0-100, défaut 100)
  - `Muted` (bool, défaut false)
  - `VideoFit` (string, "cover"/"fit"/"fill", défaut "cover")
  - `Loop` (bool, défaut true)
  - `Speed` (double, 0.25-4.0, défaut 1.0)
- **MpvPlayer** : les options figées passent du constructeur à des méthodes `Apply*` publiques :
  - `ApplyVolume(int vol, bool muted)` → mpv `volume` / `mute`
  - `ApplyVideoFit(string fit)` → mpv `panscan` + `video-fit`
  - `ApplyLoop(bool loop)` → mpv `loop-file`
  - `ApplySpeed(double speed)` → mpv `speed`
- **PlayerManager** : extrait une interface `IPlayerManager`. Nouvelle méthode
  `ApplySettings(Settings)` qui itère sur tous les players.
- **AppService** : dépend de `IPlayerManager` (injecté). Nouvelle méthode
  `ApplyPlaybackSettings()` qui écrit `Settings.Save()` + appelle
  `PlayerManager.ApplySettings()`.
- **UI fenêtre** : nouvelle section dans `MainWindow.xaml` entre la grille des récents et les
  toggles existants — slider volume + mute, radio buttons cadrage, toggle boucle, slider vitesse.
- **UI tray** : `App.xaml.cs` — volume slider + statut muet dans le ContextMenu du tray.
- **Persistance** : globale (un jeu de réglages pour tous les wallpapers).

## Testing Decisions

- **Ce qui fait un bon test** : tester le comportement observable (les settings sont sauvegardés,
  les bons paramètres mpv sont envoyés), pas les détails d'implémentation.
- **Modules testés** :
  1. `Settings` — sérialisation JSON round-trip, valeurs par défaut, limites (volume 0/100,
     speed 0.25/4.0).
  2. `AppService.ApplyPlaybackSettings` — vérifie que `Settings.Save()` est appelé et que
     `IPlayerManager.ApplySettings()` reçoit les bonnes valeurs (mock `IPlayerManager`).
- **Nouveau seam** : `IPlayerManager` extraite de `PlayerManager` pour permettre l'injection et
  le mock dans `AppService`. Seam unique et le plus haut possible.
- **Test project** : nouveau projet `tests/Wallflow.Tests/` xunit, même `TargetFramework`.

## Out of Scope

- Réglages par wallpaper (chaque fichier avec ses propres réglages) — persistance globale
  seulement.
- Réglages avancés (égaliseur, filtres mpv, effets visuels).
- Profile de réglages (sauvegarder/charger un profil).
- Mode Picture-in-Picture ou multi-wallpaper par écran.
- Support du HTML/Shadertoy comme wallpaper.
- Installation CI/CD ou GitHub Actions (pas de remote).

## Further Notes

- Une image fixe ignore le réglage de boucle (pas de sens).
- Les valeurs hors limites sont clampées à la lecture (volume < 0 → 0, > 100 → 100,
  speed < 0.25 → 0.25, > 4.0 → 4.0).
- Le menu du tray affiche un indicateur visuel quand le son est muet.
- Session de grilling 2026-07-18 : décisions capturées dans ADR-002.
