# PRD — Réglages d'image (luminosité, contraste, saturation)

## Problem Statement

Les `Réglages de lecture` actuels (cadrage, boucle, vitesse) ne touchent qu'au rythme et au cadrage
de l'animation, jamais à son rendu colorimétrique. Un GIF trop sombre pour rester lisible derrière
les icônes du bureau, ou une vidéo trop saturée pour un usage prolongé à l'écran, ne peuvent être
corrigés que dans un éditeur externe avant import — aucun réglage de Wallflow n'agit sur l'image
elle-même.

## Solution

Ajouter 3 réglages de lecture configurables par l'utilisateur : **Luminosité**, **Contraste**,
**Saturation** (chacun sur une échelle -100 à 100, défaut 0 = neutre, identique à l'échelle native
mpv). Mêmes propriétés que les `Réglages de lecture` existants : globaux (pas par wallpaper),
persistés dans `settings.json`, appliqués immédiatement, dans la même section de la fenêtre
principale (flyout ⚙ Réglages).

## User Stories

1. En tant qu'`Utilisateur`, je veux ajuster la luminosité de mon `Wallpaper`, pour qu'un fichier
   trop sombre ou trop clair reste confortable derrière mes icônes.
2. En tant qu'`Utilisateur`, je veux ajuster le contraste, pour adoucir ou accentuer une image trop
   plate ou trop dure.
3. En tant qu'`Utilisateur`, je veux ajuster la saturation, pour désaturer un fichier trop criard
   ou raviver un fichier terne.
4. En tant qu'`Utilisateur`, je veux voir la valeur courante de chaque réglage à côté de son
   slider, pour savoir où je me situe par rapport au neutre (0).
5. En tant qu'`Utilisateur`, je veux un moyen rapide de revenir aux valeurs neutres, pour annuler
   un réglage sans avoir à recaler chaque slider à la main.
6. En tant qu'`Utilisateur`, je veux que ces réglages soient persistés et restaurés au lancement,
   comme les `Réglages de lecture` existants.
7. En tant qu'`Utilisateur`, je veux que les réglages s'appliquent immédiatement sans redémarrer
   l'application, pour un feedback instantané.

## Implementation Decisions

- **Settings** (`src/Wallflow/Settings.cs`) : 3 nouvelles propriétés, même pattern de clamp que
  `Speed` :
  - `Brightness` (int, -100 à 100, défaut 0)
  - `Contrast` (int, -100 à 100, défaut 0)
  - `Saturation` (int, -100 à 100, défaut 0)
- **MpvPlayer** (`src/Wallflow/MpvPlayer.cs`) : 3 nouvelles méthodes `Apply*` publiques, même
  niveau de granularité que `ApplyLoop`/`ApplySpeed` (une méthode par réglage, pas de bundling
  comme `ApplyVideoFit`) :
  - `ApplyBrightness(int value)` → propriété mpv `brightness`
  - `ApplyContrast(int value)` → propriété mpv `contrast`
  - `ApplySaturation(int value)` → propriété mpv `saturation`
  - Propriétés natives de l'égaliseur vidéo mpv, déjà bornées -100/100 côté mpv — pas de filtre
    `vf` custom à maintenir.
- **PlayerManager.ApplySettings** : étend la boucle existante avec les 3 nouveaux appels
  `Apply*`, sur le même modèle que `VideoFit`/`Loop`/`Speed`. Aucun changement d'`IPlayerManager` :
  la méthode prend déjà tout l'objet `Settings`.
- **UI fenêtre** (`MainWindow.xaml`, flyout `SettingsFlyout`) : 3 sliders (-100 à 100, pas de 1)
  sous la section `Vitesse`, chacun avec un readout de sa valeur à côté (même convention que le
  readout `%` du volume, issue 006). Un bouton/lien **« Réinitialiser »** remet les 3 à 0 en un
  clic — nécessaire ici (contrairement à la vitesse en paliers) car un slider continu sans repère
  visuel au neutre est facile à laisser décalé sans s'en rendre compte.
- **Emplacement dans le flyout** : le flyout passe de 6 à 9 réglages effectifs. Pas de nouvelle
  navigation (DESIGN.md l'exclut explicitement) — les 3 sliders sont regroupés sous un sous-titre
  « Image » pour garder le flyout lisible, sans changer sa structure à une seule vue.
- **Persistance** : globale (un jeu de réglages pour tous les wallpapers, comme l'existant).

## Testing Decisions

- **Ce qui fait un bon test** : comportement observable (clamp, round-trip JSON, propagation à
  `IPlayerManager`), pas le câblage UI (vérifié visuellement, comme pour les paliers de vitesse).
- **Modules testés** :
  1. `Settings` (`SettingsTests.cs`) — défauts à 0, round-trip JSON, clamp aux bornes (-100/100
     et au-delà) pour chacune des 3 propriétés.
  2. `AppService.ApplyPlaybackSettings` (`AppServiceTests.cs`) — le test existant qui vérifie que
     `IPlayerManager.ApplySettings()` reçoit l'objet `Settings` couvre déjà la propagation, aucune
     nouvelle méthode sur `FakePlayerManager` n'est nécessaire.
- **Non testé unitairement** : les nouvelles méthodes `MpvPlayer.Apply*` (pas de tests existants
  sur `MpvPlayer` — il n'expose que des appels natifs libmpv, vérifiés live comme le reste de la
  classe).

## Out of Scope

- **Teinte / gamma** : mpv les expose aussi (`hue`, `gamma`), mais aucune user story ne les
  demande — à ajouter séparément si le besoin apparaît.
- **Réglages par wallpaper** : persistance globale seulement, comme les `Réglages de lecture`
  existants (déjà écarté dans `docs/PRD-reglages-lecture.md`).
- **Aperçu en direct dans la grille des récents / vignettes** : les sliders agissent sur le
  wallpaper affiché, pas sur les miniatures.
- **Filtres avancés (LUT, courbes, égaliseur multi-bandes)** : hors scope produit (DESIGN.md,
  non-objectifs).
- **Raccourcis clavier globaux** pour ces réglages.

## Further Notes

- Les valeurs hors limites sont clampées à l'écriture, comme `Speed` (`-150` → `-100`, `150` →
  `100`).
- Une image fixe accepte ces 3 réglages normalement (contrairement à la boucle, qui n'a pas de
  sens sur une image fixe) — pas de cas particulier à gérer.
- Pas d'ADR nécessaire : extension directe du pattern `Réglages de lecture` déjà en place, aucune
  décision architecturale nouvelle.
