## Problem Statement

La fenêtre de Wallflow est devenue un « tas de contrôles » sans hiérarchie : un gros bandeau de dépôt permanent (alors que le drag & drop fonctionne déjà sur toute la fenêtre), une grille de `Récents` écrasée au milieu, quatre groupes de `Réglages de lecture` étalés en permanence, et une rangée de quatre boutons en bas. L'`Utilisateur` — dont le geste principal après le premier jour est « ré-appliquer un `Wallpaper` récent » — doit chercher ses vignettes dans l'écran le plus chargé de l'app, sans même voir quel `Wallpaper` est actuellement actif, et sans pouvoir retirer un élément des `Récents`.

## Solution

Refonte structurée de la fenêtre en **deux zones seulement**, en esthétique Fluent Windows 11 native (lib Wpf.Ui déjà embarquée — Mica, icônes Segoe Fluent, accent système) :

1. **La grille des `Récents` en héros** : vignettes 16:9 (~160×90) occupant tout l'espace, badge + bordure accent sur le `Wallpaper` actif, clic droit pour retirer un élément ou ouvrir son emplacement, et une tuile « + » en première position qui ouvre le dialogue Parcourir (elle résout aussi l'état vide au premier lancement).
2. **Une barre du bas à 3 contrôles** : play/pause (pilote la `Pause manuelle`), volume (flyout : slider + % + muet), et ⚙ (flyout compact : cadrage, vitesse, boucle, démarrage avec Windows, puis les actions `Retirer le fond d'écran` et Quitter).

Le bandeau de dépôt disparaît ; le drop reste actif sur toute la fenêtre avec un overlay plein cadre « Dépose pour appliquer » pendant le drag. Les erreurs passent en snackbar éphémère. Le menu du tray est intouché.

Direction complète figée dans `DESIGN.md`, section « Refonte de la fenêtre (session de grilling 2026-07-19) ».

## User Stories

1. As an `Utilisateur`, I want la grille des `Récents` en élément dominant de la fenêtre, so that ré-appliquer un `Wallpaper` — mon geste principal — soit immédiat.
2. As an `Utilisateur`, I want des vignettes 16:9 (~160×90) plutôt que carrées, so that l'aperçu ne recadre pas mes wallpapers qui sont presque toujours 16:9.
3. As an `Utilisateur`, I want une bordure accent et un badge sur la vignette du `Wallpaper` actif, so that je voie d'un coup d'œil ce qui joue actuellement.
4. As an `Utilisateur`, I want cliquer une vignette pour ré-appliquer ce `Wallpaper`, so that je retrouve le comportement actuel des `Récents` (conservé).
5. As an `Utilisateur`, I want un clic droit « Retirer des récents » sur une vignette, so that je purge les entrées que je ne compte plus utiliser.
6. As an `Utilisateur`, I want un clic droit « Ouvrir l'emplacement du fichier » sur une vignette, so that je retrouve le fichier source sur mon disque.
7. As an `Utilisateur`, I want une tuile « + » en première position de la grille, au même format que les vignettes, so that je puisse parcourir mes fichiers sans chrome permanent dédié.
8. As an `Utilisateur`, I want qu'au premier lancement la grille ne contienne que la tuile « + » avec un hint « dépose un fichier ou clique », so that l'état vide soit auto-explicatif sans écran d'onboarding.
9. As an `Utilisateur`, I want déposer un fichier n'importe où sur la fenêtre, so that je n'aie pas à viser une zone précise (comportement actuel conservé).
10. As an `Utilisateur`, I want un overlay plein cadre « Dépose pour appliquer » pendant un drag au-dessus de la fenêtre, so that la capacité de drop soit signalée au moment où elle sert — au lieu d'un bandeau permanent.
11. As an `Utilisateur`, I want un bouton play/pause icône dans la barre du bas, so that je pose ou lève la `Pause manuelle` d'un clic.
12. As an `Utilisateur`, I want que le bouton play/pause reflète l'état réel de la `Pause manuelle`, so that l'icône reste juste même quand je change l'état depuis le tray.
13. As an `Utilisateur`, I want une icône volume ouvrant un flyout avec slider, pourcentage et toggle Muet, so that je règle le son sans que ces contrôles occupent l'écran en permanence.
14. As an `Utilisateur`, I want un flyout ⚙ regroupant cadrage (Cover/Fit/Fill), vitesse (0.5×/1×/1.5×/2×), boucle et démarrage avec Windows, so that les `Réglages de lecture` secondaires soient à un clic sans encombrer la fenêtre.
15. As an `Utilisateur`, I want `Retirer le fond d'écran` et Quitter dans le flyout ⚙, sous un séparateur, so that ces actions rares restent accessibles depuis la fenêtre sans mobiliser des boutons permanents.
16. As an `Utilisateur`, I want que chaque changement dans un flyout s'applique immédiatement au `Wallpaper` en cours, so that le comportement actuel des `Réglages de lecture` (application instantanée, persistance) soit conservé.
17. As an `Utilisateur`, I want une snackbar éphémère en bas de la grille pour « format non supporté ou fichier introuvable », so that l'erreur soit visible puis disparaisse au lieu d'occuper un bandeau.
18. As an `Utilisateur`, I want une fenêtre redimensionnable (~640×480 par défaut) avec une grille qui re-wrappe, so that je voie plus de vignettes si j'agrandis.
19. As an `Utilisateur`, I want la fenêtre en Fluent Windows 11 natif (Mica, contrôles Wpf.Ui, icônes Segoe Fluent, accent système), so that l'app ressemble à un réglage Windows moderne cohérent avec mon OS.
20. As an `Utilisateur`, I want le menu du tray inchangé, so that mes gestes à distance (pause, volume, retirer, quitter) restent exactement ceux que je connais.

## Implementation Decisions

- **Refonte de la vue seule + deux extensions du cœur** : la restructuration est confinée à la fenêtre principale (XAML + code-behind). Le cœur (`AppService`) ne gagne que deux choses : une opération « retirer des récents » et l'exposition du `Wallpaper` actif (déjà présent via le wallpaper courant des `Réglages`).
- **« Retirer des récents »** : retire l'entrée de la liste persistée et notifie le changement d'état ; ne touche ni à la lecture en cours ni au `Wallpaper` actif (retirer des `Récents` la vignette du wallpaper actif ne déclenche pas `Retirer le fond d'écran`).
- **« Wallpaper actif »** : dérivé du wallpaper courant persisté — pas de nouvel état. La grille marque la vignette dont le chemin correspond.
- **Aucune nouvelle dépendance** : Mica (`WindowBackdropType`), flyouts, snackbar, icônes — tout vient de Wpf.Ui déjà embarqué et des primitives WPF.
- **Overlay de drop** : piloté par les événements drag de la fenêtre (enter/leave/drop), simple calque plein cadre au-dessus de la grille.
- **Vignettes** : le générateur de miniatures existant est conservé ; le rendu passe en 16:9 avec remplissage uniforme (crop), fallback texte inchangé pour les fichiers sans miniature.
- **Sémantique inchangée** : `Pause manuelle`/`Pause auto`, application immédiate + persistance des `Réglages de lecture`, purge des `Récents` morts à la lecture, `Retirer le fond d'écran` → `Restauration du bureau` — rien de tout ça ne change.
- **Tray intouché** : contenu, libellés et synchronisation d'état actuels conservés à l'identique.

## Testing Decisions

- **Un seul seam, celui qui existe déjà** : `AppService` construit avec un fake de `IPlayerManager` et des `Réglages` isolés dans un dossier temporaire (`Settings.DirOverride`). Prior art : `AppServiceTests` (xUnit).
- **Ce qu'on teste (comportement externe uniquement)** :
  - « Retirer des récents » retire l'entrée, persiste, notifie `StateChanged`, et ne touche ni les players ni le wallpaper courant — y compris quand l'entrée retirée est le `Wallpaper` actif.
  - Le `Wallpaper` actif exposé correspond au wallpaper courant, et devient nul après `Retirer le fond d'écran`.
- **Ce qu'on ne teste pas** : la couche WPF (grille, flyouts, overlay, Mica, snackbar) reste vérifiée visuellement, comme aujourd'hui — pas de framework d'automatisation UI introduit (décision explicite du cadrage).
- Un bon test ici ne connaît ni le XAML ni les contrôles : il n'observe que l'état de `AppService`/`Réglages` et les appels au fake `IPlayerManager`.

## Out of Scope

- **Menu du tray** : aucun changement (ni contenu, ni libellés, ni icônes).
- **Aperçu animé au survol des vignettes** : coûteux (décodage/mpv par tuile) pour un gain faible.
- **Onboarding / premier lancement guidé** : sans objet, usage perso.
- **Navigation multi-vues** (page Réglages séparée) : 6 réglages ne justifient pas une navigation.
- **Épinglage / favoris / bibliothèque** : les `Récents` restent un simple historique (conforme au glossaire).
- **Réglages par wallpaper** : les `Réglages de lecture` restent globaux.

## Further Notes

- La cible produit a été recalibrée lors de ce cadrage : **usage perso uniquement** (écart documenté dans `DESIGN.md` — la v1 visait « distribuable »). Densité et rapidité d'accès priment sur la découvrabilité.
- `DESIGN.md` §« Refonte de la fenêtre (session de grilling 2026-07-19) » est la spec de référence pour tous les détails visuels ; en cas de divergence pendant l'implémentation, argumenter d'abord, puis répercuter dans `DESIGN.md`.
- Vocabulaire : voir `CONTEXT.md` (glossaire canonique — `Wallpaper`, `Récents`, `Pause manuelle`/`Pause auto`, `Retirer le fond d'écran`, `Restauration du bureau`, `Réglages de lecture`).
