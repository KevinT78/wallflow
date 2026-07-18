# Issue 005 — Retirer le fond d'écran + bureau blanc au Quitter

> **Statut : ✅ Implémentée** (TDD, 8 tests verts). `IPlayerManager.Clear()`,
> `WallpaperHost.RestoreDesktop()`, `AppService.RemoveWallpaper()`, entrées tray + fenêtre,
> `Settings.DirOverride` pour l'isolation des tests. Vérif live du bureau natif restant à
> faire côté utilisateur.

> **Triage** : `ready-for-agent`
> Issu de la session grilling + domain-modeling du 2026-07-18. Glossaire déjà à jour
> (`Restauration du bureau`, `Retirer le fond d'écran` ajoutés dans [CONTEXT.md](../../CONTEXT.md)).

## Problem Statement

En tant qu'`Utilisateur`, je n'ai **aucun moyen propre d'arrêter** le fond. Le seul « Quitter » est
dans le menu du tray, et quand je clique dessus je me retrouve avec un **bureau blanc** (mon fond
d'écran Windows a disparu) et l'icône du tray s'en va — je ne peux plus rien relancer facilement.
Il n'existe pas non plus de moyen de simplement **retirer** le `Wallpaper` sans fermer l'app.

## Solution

Deux actions distinctes et sûres :

- **Retirer le fond d'écran** (menu tray + fenêtre) : mon bureau Windows natif revient
  (`Restauration du bureau`), l'app **reste vivante** dans le tray, et je ré-applique en un clic via
  les `Récents`.
- **Quitter** : ferme l'app **et** me rend mon fond d'écran Windows (plus de bureau blanc), et
  devient accessible aussi depuis la fenêtre, pas seulement le tray.

## User Stories

1. En tant qu'`Utilisateur`, je veux **Retirer le fond d'écran** depuis le menu du tray, afin de
   revenir à mon bureau Windows sans fermer l'application.
2. En tant qu'`Utilisateur`, je veux **Retirer le fond d'écran** depuis la fenêtre, afin d'avoir
   l'action sous la main quand la fenêtre est ouverte.
3. En tant qu'`Utilisateur`, quand je retire le fond, je veux que l'app reste dans le tray, afin de
   pouvoir ré-appliquer un `Wallpaper` sans la relancer.
4. En tant qu'`Utilisateur`, après avoir retiré le fond, je veux ré-appliquer un `Wallpaper` en un
   clic depuis les `Récents`, afin de revenir en arrière instantanément.
5. En tant qu'`Utilisateur`, quand j'ai retiré le fond puis redémarré Windows, je veux garder mon
   bureau normal (pas de `Wallpaper` réappliqué tout seul), afin que mon choix soit respecté.
6. En tant qu'`Utilisateur`, je veux que **Quitter** me rende mon fond d'écran Windows d'origine,
   afin de ne jamais me retrouver avec un bureau blanc.
7. En tant qu'`Utilisateur`, je veux un **Quitter** accessible depuis la fenêtre, afin de ne pas
   devoir chercher l'icône du tray dans l'overflow.
8. En tant qu'`Utilisateur`, je veux que « Retirer le fond » soit clairement distinct de « Pause »,
   afin de comprendre que l'un rend le bureau et l'autre fige seulement l'image.
9. En tant qu'`Utilisateur`, je veux que la `Pause manuelle` et la `Pause auto` continuent de
   fonctionner comme avant, afin qu'aucune régression n'accompagne ce changement.

## Implementation Decisions

- Nouvelle primitive `Restauration du bureau` dans `WallpaperHost` : P/Invoke
  `SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, null, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE)`, qui
  demande à Windows de repeindre le fond natif enregistré. Sans elle, détruire les fenêtres hôtes
  laisse le bureau blanc (cause racine du bug : le fond natif ne réapparaît **pas** tout seul).
- `IPlayerManager` gagne **une** méthode : `Clear()` — détruit les players **et** déclenche la
  `Restauration du bureau`. Seul changement d'interface.
- `PlayerManager` : `Clear()` = teardown des players + `Restauration du bureau` + oubli du
  `Wallpaper` courant. `Dispose()` (chemin Quitter) déclenche **aussi** la `Restauration du bureau`
  → corrige le bureau blanc. `Rebuild()` (écran branché/débranché) ne restaure **pas** (il
  re-couvre immédiatement — éviter le flicker).
- `AppService.RemoveWallpaper()` orchestre : `LastWallpaper = null` → `_players.Clear()` →
  `Settings.Save()` → `StateChanged`. **Modèle d'état (décision B)** : « retiré » = *absence de
  `Wallpaper` courant*, exprimée avec le champ `LastWallpaper` existant, **aucun nouveau drapeau**.
  Le garde de démarrage existant (`if LastWallpaper existe & fichier présent → Apply`) rend la
  persistance gratuite.
- `App.xaml.cs` (tray) : ajouter une entrée **« Retirer le fond d'écran »** appelant
  `RemoveWallpaper()`. Le « Quitter » existant hérite du fix via `Shutdown → OnExit →
  _service.Shutdown() → _players.Dispose()`.
- `MainWindow` : ajouter un bouton **« Retirer le fond d'écran »** et un bouton **« Quitter »**
  (`Application.Current.Shutdown()`) pour la découvrabilité.
- **Isolation des tests** : `Settings` gagne un `static string? DirOverride` (défaut `null`) qui,
  s'il est défini, remplace le dossier `%LocalAppData%\Wallflow` pour `Load`/`Save`. Sans lui, un
  test de `RemoveWallpaper` (`LastWallpaper = null` + `Save`) **effacerait le vrai `Wallpaper` de
  l'utilisateur** à chaque `dotnet test`.

## Testing Decisions

**Principe** : ne tester que le comportement externe. Le code WinAPI (`SystemParametersInfo`,
WorkerW) n'est **pas** testable unitairement — vérifié en live, comme tout le P/Invoke existant.

- **`AppService.RemoveWallpaper` — seam `IPlayerManager`** : `FakePlayerManager` gagne un flag
  `Cleared`. Asserts : après l'appel, `Settings.LastWallpaper == null` **et** `Cleared == true`.
  Prior art : `AppServiceTests.ApplyPlaybackSettings_PropagatesToPlayerManager`.
- **Persistance (décision B)** : construire un `AppService` avec un `Settings` dont
  `LastWallpaper == null` → `FakePlayerManager.Load` **jamais** appelé. Nécessite
  `Settings.DirOverride` sur un dossier temporaire.
- **Régression d'hygiène** : les 2 tests existants d'`AppServiceTests` deviennent hermétiques via
  le même `DirOverride`.
- **Vérif live** : le bureau natif revient après **Retirer** et après **Quitter** (plus de bureau
  blanc) ; ré-appliquer un `Récents` recouvre bien le bureau.

## Out of Scope

- Distinguer « n'a jamais eu de `Wallpaper` » de « `Wallpaper` retiré volontairement » (`null`
  couvre les deux, le produit n'a pas ce besoin).
- Rendre `WallpaperHost` testable unitairement.

## Further Notes

- **DESIGN.md à mettre à jour** : « Retirer le fond d'écran » et le « Quitter » dans la fenêtre
  élargissent l'UI « minimale » figée — à argumenter dans DESIGN.md puis répercuter.
- **CONTEXT.md déjà à jour** : contradiction du fond natif corrigée, termes `Restauration du
  bureau` et `Retirer le fond d'écran` ajoutés.
- **Pas d'ADR** : ni difficilement réversible ni arbitrage architectural.
