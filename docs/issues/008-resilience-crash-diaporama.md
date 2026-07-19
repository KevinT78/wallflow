# Issue 008 — Résilience crash : snapshot du diaporama persisté

> **Triage** : `ready-for-agent`
> Issu de `/to-issues` sur [docs/PRD-anti-flicker-diaporama.md](../PRD-anti-flicker-diaporama.md)
> (session grilling + domain-modeling du 2026-07-19).

## Parent

[docs/PRD-anti-flicker-diaporama.md](../PRD-anti-flicker-diaporama.md)

## What to build

La capture du diaporama Windows (issue 007) vit en mémoire dans `AppService` : si Wallflow crashe
ou est tué brutalement (`taskkill /F`, plantage) pendant qu'un `Wallpaper` est actif, cette capture
est perdue et le diaporama de l'Utilisateur reste coupé silencieusement, sans recours.

`Settings` gagne une propriété `SlideshowSnapshot` (nullable, sérialisable : dossier d'images,
intervalle, mélange), persistée dans `settings.json` comme les autres `Réglages`. `AppService`
utilise ce champ persisté à la place du champ en mémoire introduit par l'issue 007 :

- `Apply()` (transition aucun→actif) : écrit `Settings.SlideshowSnapshot` (au lieu du champ privé),
  sauvegardé via le `Settings.Save()` déjà existant dans `Apply`.
- `RemoveWallpaper()` / `Shutdown()` : si `Settings.SlideshowSnapshot != null`, restaure puis remet
  à `null` et sauvegarde.

Comme la capture est sur disque dès qu'elle existe, un crash entre-temps ne la perd plus : le
prochain `Retirer le fond d'écran`/`Quitter` propre — même dans une session ultérieure après
redémarrage de l'app — la consomme et restaure le diaporama d'origine.

## Acceptance criteria

- [ ] `Settings.SlideshowSnapshot` (nullable) sérialise/désérialise correctement en JSON
      (round-trip), comme les autres propriétés de `Settings`.
- [ ] `AppService.Apply()` écrit la capture dans `Settings.SlideshowSnapshot` (persisté), plus dans
      un champ en mémoire.
- [ ] `AppService.RemoveWallpaper()`/`Shutdown()` lisent `Settings.SlideshowSnapshot`, restaurent si
      présent, le remettent à `null`, et persistent ce retrait.
- [ ] Test : construire un `AppService`/`Settings` isolé (`Settings.DirOverride`) avec un
      `SlideshowSnapshot` déjà présent (simulant un crash antérieur) → `RemoveWallpaper()` doit
      appeler `ResumeSlideshow` avec cette valeur et remettre `Settings.SlideshowSnapshot` à `null`.
- [ ] Vérif live : lancer Wallflow avec le diaporama Windows actif, appliquer un `Wallpaper`,
      `taskkill /F` le process, relancer Wallflow, faire `Retirer le fond d'écran` → le diaporama
      Windows d'origine est restauré malgré le crash intermédiaire.

## Blocked by

- Issue 007 (réutilise `IPlayerManager.PauseSlideshowIfActive()` / `ResumeSlideshow()`)
