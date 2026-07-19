# Issue 007 — Couper/restaurer le diaporama Windows pendant la session

> **Triage** : `ready-for-agent`
> Issu de `/to-issues` sur [docs/PRD-anti-flicker-diaporama.md](../PRD-anti-flicker-diaporama.md)
> (session grilling + domain-modeling du 2026-07-19).

## Parent

[docs/PRD-anti-flicker-diaporama.md](../PRD-anti-flicker-diaporama.md)

## What to build

Tant que Wallflow a un `Wallpaper` actif, le diaporama Windows natif doit rester coupé, pour que son
tick périodique ne repeigne plus jamais l'écran par-dessus le `Wallpaper` de Wallflow (cause du
flicker rapporté — voir investigation dans le PRD : aucune notification système ne permet de
détecter ce repaint, d'où la stratégie « supprimer la cause » plutôt que « réagir au symptôme »).

`IPlayerManager` gagne deux méthodes, implémentées dans `PlayerManager` en délégant à une nouvelle
primitive `WallpaperHost` construite sur l'interface COM `IDesktopWallpaper`
(`CLSID_DesktopWallpaper`, dispo depuis Windows 8) :

- `PauseSlideshowIfActive()` : si le diaporama Windows est actif (registre `BackgroundType == 2`),
  capture sa config (dossier d'images, intervalle, mélange) puis coupe le défilement via
  `Enable(false)`. Retourne la config capturée, ou `null` si le diaporama n'était pas actif.
- `ResumeSlideshow(snapshot)` : relance le diaporama à l'identique à partir d'une config capturée
  (`SetSlideshow` + `SetSlideshowOptions` + `Enable(true)`).

> **Note d'implémentation (vérifié live, Win10 19045)** : la coupure par effet de bord de
> `SetWallpaper` et la détection par `GetStatus()` prévues initialement ne fonctionnent pas en
> programmatique (voir PRD, section Correction empirique). Mécanisme retenu : détection
> `BackgroundType == 2`, coupure/reprise par `Enable(false)`/`Enable(true)` — round-trip vérifié
> `0x3 → 0x0 → 0x3`.

L'orchestration (quand appeler quoi) vit dans `AppService`, pas dans `PlayerManager` — il connaît
déjà la transition aucun↔actif via `Settings.LastWallpaper`, ce qui rend cette partie testable via
le seam `IPlayerManager` existant :

- `Apply(path)` : si `Settings.LastWallpaper` était `null` avant cet appel (transition
  aucun-actif), appelle `PauseSlideshowIfActive()` et garde le résultat en mémoire (champ privé —
  la persistance dans `settings.json` est hors scope ici, voir issue 008). Un `Apply` qui ne fait
  que changer d'image alors qu'un `Wallpaper` était déjà actif ne redéclenche **pas** de capture.
- `RemoveWallpaper()` et `Shutdown()` (chemin Quitter) : si une capture est en mémoire, appellent
  `ResumeSlideshow(snapshot)` puis l'oublient.
- `Rebuild()` (écran branché/débranché) ne touche pas au diaporama — le `Wallpaper` reste actif
  tout du long, aucune transition de state.

Aucun réglage exposé dans l'UI — comportement entièrement automatique.

## Acceptance criteria

- [ ] `IPlayerManager` expose `PauseSlideshowIfActive()` et `ResumeSlideshow(snapshot)` ;
      `PlayerManager` les implémente en délégant à `WallpaperHost`.
- [ ] `WallpaperHost` implémente la capture/coupure/reprise via `IDesktopWallpaper` (COM).
- [ ] `AppService.Apply()` capture le diaporama uniquement à la transition aucun `Wallpaper` actif
      → actif (pas sur un changement d'image d'un `Wallpaper` déjà actif).
- [ ] `AppService.RemoveWallpaper()` restaure le diaporama capturé s'il y en a un, et l'oublie.
- [ ] `AppService.Shutdown()` (Quitter) fait de même.
- [ ] Si le fond Windows n'était pas en mode diaporama à l'`Apply`, aucun changement de réglage
      natif (no-op vérifié : `PauseSlideshowIfActive()` retourne `null`).
- [ ] `RemoveFromRecents` ne touche ni ne consulte le diaporama (comportement inchangé).
- [ ] Vérif live : diaporama Windows réglé à 1 min, `Apply` un `Wallpaper` → aucun flicker observé
      sur plusieurs ticks ; `Retirer le fond d'écran` puis `Quitter` → le diaporama Windows reprend
      avec les mêmes images et le même intervalle qu'avant.
- [ ] Tests `AppServiceTests` (via `FakePlayerManager` étendu) : capture appelée une seule fois à la
      transition initiale, restauration appelée sur `RemoveWallpaper`, pas de recapture sur un
      `Apply` d'image alors qu'un `Wallpaper` était déjà actif.

## Blocked by

None - can start immediately
