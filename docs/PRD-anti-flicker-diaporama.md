# PRD — Anti-flicker diaporama Windows

> **Triage** : `ready-for-agent`
> Issu de la session grilling + domain-modeling du 2026-07-19 (investigation en direct, voir
> Further Notes pour le détail des pistes testées).

## Problem Statement

Quand le fond d'écran natif de Windows est réglé sur **diaporama**, l'image du diaporama s'affiche
brièvement, en plein écran, sur tous les écrans, à chaque changement d'image — avant que Wallflow
ne recouvre automatiquement avec son propre `Wallpaper`. L'Utilisateur voit donc un flicker
périodique (au rythme de l'intervalle qu'il a configuré dans les réglages Windows) alors que son
`Wallpaper` Wallflow devrait rester ininterrompu.

Investigation en direct (voir Further Notes) : le tick du diaporama Windows ne déclenche **aucune**
notification système exploitable (`SPI_SETDESKWALLPAPER`/`WM_SETTINGCHANGE`, `SystemEvents`,
`WinEvent`) — Wallflow ne peut pas détecter ni anticiper le repaint pour réagir dessus.

## Solution

Plutôt que détecter et réagir au repaint (impossible, aucun signal disponible), **supprimer la
cause à la source** : tant que Wallflow a un `Wallpaper` actif, le diaporama Windows natif est
coupé (API `IDesktopWallpaper`, effet de bord documenté de `SetWallpaper`). Sa configuration
(images, intervalle, mélange) est capturée avant la coupure et restaurée à l'identique dès que
Wallflow n'a plus de `Wallpaper` actif (`Retirer le fond d'écran` ou `Quitter`) — symétrique au
mécanisme de `Restauration du bureau` déjà existant (issue 005), mais sur la couche « réglage natif
du diaporama » plutôt que sur la couche « repaint du fond ».

La capture est persistée dans `settings.json` : même si Wallflow crashe ou est tué brutalement
pendant qu'un `Wallpaper` est actif, le prochain `Retirer le fond d'écran`/`Quitter` propre —
même dans une session ultérieure — restaure quand même le diaporama d'origine.

## User Stories

1. En tant qu'Utilisateur ayant configuré un diaporama Windows, je veux que mon `Wallpaper`
   Wallflow reste visuellement ininterrompu, pour ne plus voir l'image du diaporama apparaître.
2. En tant qu'Utilisateur, je veux que Wallflow coupe automatiquement le diaporama Windows dès
   qu'il applique un `Wallpaper`, pour que le tick périodique cesse de se produire pendant que mon
   `Wallpaper` joue.
3. En tant qu'Utilisateur, je veux que mon diaporama Windows soit restauré automatiquement (mêmes
   images, même intervalle, même mélange) quand je fais `Retirer le fond d'écran`, pour retrouver
   exactement le réglage que j'avais avant.
4. En tant qu'Utilisateur, je veux la même restauration automatique quand je Quitte l'application,
   pour ne pas perdre mon réglage de diaporama juste parce que je ferme Wallflow.
5. En tant qu'Utilisateur dont le fond Windows n'était **pas** en mode diaporama (image fixe ou
   couleur unie) quand j'ouvre Wallflow, je ne veux **aucun** changement de mon réglage natif —
   comportement no-op.
6. En tant qu'Utilisateur, si Wallflow crashe ou est fermé brutalement pendant qu'un `Wallpaper`
   est actif, je veux que mon diaporama soit quand même restauré au prochain `Retirer le fond
   d'écran`/`Quitter` propre — même après un redémarrage de l'application — pour ne jamais perdre
   mon réglage silencieusement.
7. En tant qu'Utilisateur, je ne veux avoir aucun réglage à configurer moi-même pour ce
   comportement — automatique, sans option exposée dans l'UI.
8. En tant qu'Utilisateur, je veux que ce mécanisme n'affecte pas les `Réglages de lecture`
   (volume, cadrage, boucle, vitesse) — comportement orthogonal.
9. En tant qu'Utilisateur, je veux que changer d'image parmi mes `Récents` (donc rester avec un
   `Wallpaper` actif en permanence) ne redéclenche pas une nouvelle capture/coupure du diaporama —
   la coupure n'a lieu qu'à la transition « aucun `Wallpaper` actif → actif ».
10. En tant qu'Utilisateur, je veux que `Retirer des récents` (qui ne touche ni les players ni le
    `Wallpaper` courant) ne touche pas non plus au diaporama Windows — cohérent avec son
    comportement actuel de non-interférence.
11. En tant qu'Utilisateur multi-écran, je veux que la coupure/restauration du diaporama s'applique
    globalement (un seul réglage de fond Windows pour tous les écrans), cohérent avec le fait qu'un
    seul `Wallpaper` Wallflow s'affiche à l'identique sur tous les écrans.
12. En tant qu'Utilisateur, je veux qu'un branchement/débranchement d'écran (`Rebuild`) ne
    redéclenche pas une capture/coupure du diaporama, puisque le `Wallpaper` reste actif tout du
    long dans ce cas — aucune transition de state.

## Implementation Decisions

- **Nouvelle primitive dans `WallpaperHost`** (même frontière figée que le reste du WinAPI/COM
  bureau — cf. ARCHITECTURE-TECHNIQUE.md) construite sur l'interface COM `IDesktopWallpaper`
  (`CLSID_DesktopWallpaper`, disponible depuis Windows 8, donc compatible avec les cibles
  Wallflow) :
  - `PauseSlideshowIfActive()` : détecte le mode diaporama via le registre `BackgroundType == 2`,
    capture `GetSlideshow()` (résolu en chemin de dossier via `IShellItem::GetDisplayName`) +
    `GetSlideshowOptions()` (intervalle, mélange) dans un DTO sérialisable (`SlideshowSnapshot`),
    puis coupe le défilement via `Enable(false)`. Retourne le snapshot, ou `null` si rien à
    capturer (fond non-diaporama).
  - `ResumeSlideshow(snapshot)` : reconstruit un `IShellItemArray` depuis le chemin capturé
    (`SHCreateItemFromParsingName` + `SHCreateShellItemArrayFromShellItem`), puis `SetSlideshow()`
    + `SetSlideshowOptions()` + `Enable(true)` pour relancer à l'identique.
  - **Correction empirique (vérifié live 2026-07-19, Win10 19045)** : l'approche initiale
    « `SetWallpaper(cheminActuel)` pour couper » ne coupe PAS le diaporama, et `GetStatus()` n'est
    pas fiable en programmatique (reste `DSS_ENABLED` même diaporama posé, ne passe à `DSS_SLIDESHOW`
    que via l'app Réglages). Signaux fiables retenus : `BackgroundType == 2` pour détecter,
    `Enable(false)`/`Enable(true)` pour couper/relancer — round-trip vérifié (`0x3 → 0x0 → 0x3`).
- **Persistance de la capture** : nouvelle propriété `Settings.SlideshowSnapshot` (nullable),
  sérialisée dans `settings.json` comme les autres `Réglages` — c'est ce qui rend la restauration
  résiliente à un crash de l'app : tant qu'un `Retirer le fond d'écran`/`Quitter` propre a lieu, même
  dans une session ultérieure après un crash, la capture est toujours disponible.
- **Orchestration dans `PlayerManager`** (même emplacement que `RestoreDesktop()` aujourd'hui) :
  - `Load()`, uniquement à la transition **aucun `Wallpaper` actif → actif** (pas sur un simple
    changement d'image d'un `Wallpaper` déjà actif) : appelle `PauseSlideshowIfActive()`, stocke le
    résultat dans `Settings.SlideshowSnapshot`, sauvegarde.
  - `Clear()` / `Dispose()` : si `Settings.SlideshowSnapshot != null`, appelle
    `ResumeSlideshow(snapshot)`, remet `Settings.SlideshowSnapshot = null`, sauvegarde — ordre
    indépendant vis-à-vis de `RestoreDesktop()` (deux couches distinctes du bureau).
  - `Rebuild()` (écran branché/débranché) ne touche pas au diaporama : le `Wallpaper` reste actif
    tout du long, aucune transition de state.
- **Pas de nouveau réglage exposé** : comportement entièrement automatique, aucune option ajoutée
  dans la fenêtre ou le tray.

## Testing Decisions

- **Ce qui n'est pas testable unitairement** : comme `RestoreDesktop()`/`WorkerW`, tout le code COM
  (`IDesktopWallpaper`) est vérifié en live uniquement — régler un diaporama réel à intervalle
  court (1 min), `Apply` un `Wallpaper`, observer l'absence de flicker sur plusieurs ticks, puis
  `Retirer le fond d'écran`/`Quitter` et vérifier que le diaporama Windows reprend avec les mêmes
  images et le même intervalle.
- **Ce qui est testable**, via le seam existant `Settings.DirOverride` (aucun nouveau seam
  nécessaire) :
  - `Settings.SlideshowSnapshot` — sérialisation JSON round-trip, comme les autres propriétés de
    `Settings`.
  - Résilience de la persistance : construire un `AppService`/`Settings` avec un
    `SlideshowSnapshot` déjà présent (simulant un crash antérieur) → `RemoveWallpaper()` doit le
    consommer et le remettre à `null` (assert sur l'état de `Settings` après, sans vérifier l'appel
    COM réel — hors de portée du test).
- Prior art direct : `AppServiceTests.RemoveWallpaper` (issue 005), même pattern d'isolation.

## Out of Scope

- Distinguer « aucun diaporama à couper » de « diaporama coupé mais capture ratée » — les deux
  remontent `null` depuis `PauseSlideshowIfActive()`. Depuis le 2026-08-13, un snapshot persisté
  n'est plus écrasé par un `null` (sans quoi une capture ratée coupe le diaporama sans laisser de
  quoi le relancer, US6 défaite en silence). Contrepartie : si l'Utilisateur est passé à un fond
  fixe entre une session crashée et la suivante, son ancien diaporama sera quand même restauré au
  prochain `Retirer le fond d'écran` — tension assumée avec US3/US4. Lever l'ambiguïté demanderait
  d'élargir le seam `IPlayerManager` ; non fait tant que le cas ne s'observe pas en réel.
- Restaurer le mode Windows Spotlight (« Découvertes Windows ») — non documenté par Microsoft,
  distinct du diaporama classique couvert ici.
- ~~Réagir si l'Utilisateur réactive manuellement le diaporama pendant que Wallflow a déjà un
  `Wallpaper` actif — la capture n'a lieu qu'une fois, à la transition initiale ; pas de re-coupure
  continue.~~ **Remis dans le scope le 2026-08-13** : c'est ce trou qui a fait revenir le flicker en
  session. La capture, elle, reste unique (à la transition initiale) — mais la **coupure** est
  désormais maintenue tant qu'un `Wallpaper` est actif, via un garde-fou branché sur
  `SystemEvents.UserPreferenceChanged`. Voir « Garde-fou de re-coupure » dans
  ARCHITECTURE-TECHNIQUE.md.
- Garantir la restauration si l'app est désinstallée, ou tuée sans qu'un `Retirer le fond
  d'écran`/`Quitter` propre n'ait jamais lieu par la suite — limite acceptée, cf. Further Notes.
- Réglage du diaporama par écran — un seul diaporama global coupé/restauré, cohérent avec un seul
  `Wallpaper` Wallflow affiché à l'identique sur tous les écrans.

## Further Notes

- **Investigation empirique (session de grilling du 2026-07-19)** : 4 pistes de détection du
  repaint natif testées en direct, toutes négatives — rediffusion simulée de
  `SPI_SETDESKWALLPAPER`, `SystemEvents.UserPreferenceChanged` (toutes catégories), `WM_SETTINGCHANGE`
  brut (fenêtre message-only), `SetWinEventHook` (plage complète). Confirmé : aucun canal de
  notification Windows standard ne signale un tick de diaporama — d'où le changement de stratégie,
  de « réagir au symptôme » à « supprimer la cause ».
- **Terme de glossaire à ajouter à `CONTEXT.md`** : **Diaporama Windows** — le réglage natif
  Windows qui fait défiler des images en fond d'écran, distinct et faux-ami du `Wallpaper` de
  Wallflow (qui n'a lui-même aucune notion de diaporama : un seul `Wallpaper` actif à la fois).
- La restauration reste une best-effort côté produit : aucune garantie si le processus ne repasse
  plus jamais par un chemin de sortie propre.
