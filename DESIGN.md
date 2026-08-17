# Wallflow — DESIGN.md

Source de vérité produit. Tout écart doit être argumenté ici avant d'être codé.

## Produit

- **Quoi** : app Windows qui applique un fond d'écran animé (GIF, WebP animé, mp4/webm) ou fixe, derrière les icônes du bureau.
- **Positionnement** : pas de différenciateur revendiqué face à Wallpaper Engine / Lively — l'effort est donc calibré au minimum viable, zéro feature spéculative.
- **Cible (écart 2026-07-19)** : usage **perso uniquement** — la v1 visait « distribuable » ; recalibré lors du grilling UI/UX. Conséquence : densité et rapidité d'accès priment sur la découvrabilité ; pas d'onboarding, pas de pédagogie. La distribution (zip portable) reste possible mais ne dicte plus les choix d'UI.
- **Non-objectifs v1** : wallpapers HTML/interactifs, bibliothèque en ligne, wallpaper différent par écran, multi-plateforme. (~~réglages de lecture~~ — livrés depuis, voir écart ci-dessous.)

## Décisions figées (session de grilling 2026-07-18)

| Sujet | Décision |
|---|---|
| Cible | App distribuable, Windows 10/11 uniquement |
| Formats | GIF, WebP animé, mp4/webm, images fixes — pas de HTML |
| Stack | .NET 8 + WPF, libmpv comme moteur de lecture unique, P/Invoke pour WorkerW |
| Multi-écran | Même wallpaper sur tous les écrans (une instance mpv par écran) |
| Performance | Pause automatique si app en plein écran OU sur batterie/économie d'énergie. Non négociable. |
| Lecture | Défauts : muet, cadrage cover, boucle infinie. **Écart (branche `feat/playback-settings`)** : réglages de lecture livrés — cadrage, vitesse par paliers, boucle (voir [docs/PRD-reglages-lecture.md](docs/PRD-reglages-lecture.md)). **Écart (carte `menu-tray-vermillon`, 2026-08-10)** : le son est retiré entièrement (UI fenêtre/tray + `Settings.Volume`/`Muted` + plumbing mpv) — l'app reste muette en dur, aucun contrôle de volume nulle part. |
| UI | Icône tray + fenêtre minimale : drag & drop, grille des récents, bouton pause |
| Démarrage | Auto-start via clé registre `Run` écrite par l'app (toggle), démarre dans le tray, restaure le dernier wallpaper |
| Distribution | Zip portable seul, GitHub Releases. Pas d'installeur, pas de Store. |
| Nom | Wallflow — `wallflow.exe` |

## Direction visuelle

- Sombre, sobre, langage Fluent/Windows 11 (WPF-UI ou équivalent) : coins arrondis, accent discret.
- L'app s'efface : les vignettes des wallpapers sont le visuel principal.
- Une seule fenêtre, pas d'écran de réglages dédié (les toggles et actions rapides vivent dans le
  menu tray ou la fenêtre, pas dans un écran de settings séparé).

### Écart : élargissement de l'UI « minimale » (issue 005, 2026-07-18)

La fenêtre et le menu tray gagnent deux actions supplémentaires — **Retirer le fond d'écran** et
**Quitter** (déjà dans le tray, ajouté à la fenêtre). Justification : le seul « Quitter » existant
(tray uniquement) laissait un bureau blanc au clic (bug — le fond natif Windows ne réapparaît pas
tout seul sans repaint explicite) et n'offrait aucun moyen d'arrêter le fond sans fermer l'app. Ce
n'est pas un écran de réglages : deux boutons d'action, dans la même fenêtre unique. Le compteur de
boutons de la fenêtre principale passe de 0 (v1 initiale) à 2 ; jugé toujours « minimal » au sens du
non-objectif (pas d'écran dédié, pas de settings avancés).

### Écart : habillage Vermillon de la fenêtre (carte wayfinder `menu-tray-vermillon`, 2026-08-10)

**Le premier bullet ci-dessus (Fluent/Windows 11 natif, accent discret) ne s'applique plus à la
fenêtre principale.** Décision prise après grilling + prototype côte-à-côte (Option A « accent
vermillon seul sur chrome Fluent/Mica » vs Option B « palette et police complètes ») : Option B
retenue. La fenêtre adopte la palette du design system Vermillon (tokens extraits dans
[docs/design/vermillon-tokens.md](docs/design/vermillon-tokens.md)) — fond crème `#F4F2F0`,
encre `#0E0D12`, accent vermillon `#FF3B21`, secondaire lavande `#A99BC1`, ombres portées
**dures** sans flou, coins quasi jamais arrondis, titre en serif éditoriale (`Shippori Mincho`,
à embarquer comme police réelle en implémentation). Wpf.Ui reste la lib de composants sous-jacente
(pas de nouvelle dépendance), mais son thème Fluent/Mica par défaut est remplacé par ces tokens.

**Le tray n'est pas concerné** : il reste un `ContextMenu` Win32 natif, inchangé visuellement —
seuls ses items sont réorganisés (voir carte). Traçabilité complète (grilling, prototype, ticket
de décision) : `.scratch/menu-tray-vermillon/`. Pas encore implémenté à la date de cet écart.

## Refonte de la fenêtre (session de grilling 2026-07-19)

Constat : la fenêtre est devenue un « tas de contrôles » — bandeau de dépôt, récents, 4 groupes de
réglages et 4 boutons empilés, sans hiérarchie. Direction figée ci-dessous ; le **tray est hors
périmètre** (il couvre déjà les gestes à distance et doit rester bête et rapide).

### Structure : deux zones, c'est tout

**1. Grille des récents — le héros.** Le geste principal après le premier jour est « ré-appliquer
un wallpaper » ; la grille occupe tout l'espace.

- Vignettes **16:9** (~160×90), pas carrées : un wallpaper est presque toujours 16:9, le crop
  carré le trahit.
- **Wallpaper actif marqué** : bordure accent + petit badge sur sa vignette. C'est la *seule*
  indication de l'état courant dans la fenêtre (choix assumé : pas de « now playing » dans la
  barre du bas).
- Clic = appliquer. **Clic droit** → menu contextuel minimal : « Retirer des récents »,
  « Ouvrir l'emplacement du fichier ».
- **Tuile « + » en première position**, au même format que les vignettes : clic = dialogue
  Parcourir. L'état vide se résout tout seul — au premier lancement la grille ne contient que
  cette tuile avec un hint « dépose un fichier ou clique ».

**2. Barre du bas — 3 contrôles seuls**, alignés à droite, icônes Segoe Fluent :

| Contrôle | Comportement |
|---|---|
| Play/Pause | Bouton icône, reflète et pilote la pause manuelle |
| ⚙ Réglages | Icône → flyout compact (pas un menu) : cadrage (segmented Cover/Fit/Fill), vitesse (segmented 0.5×–2×), boucle (toggle), démarrer avec Windows (toggle), séparateur, « Retirer le fond d'écran », « Quitter » |

**Écart (carte `menu-tray-vermillon`, 2026-08-10)** : le contrôle Volume est retiré — plus de son
nulle part dans l'app (voir table des décisions figées, ligne Lecture). La barre du bas passe de 3
à 2 contrôles.

### Drag & drop

- Actif sur **toute la fenêtre** (comportement actuel conservé).
- Pendant le drag : **overlay plein cadre** « Dépose pour appliquer ».
- **Plus de bandeau de dépôt permanent** — il ne servait qu'à signaler une capacité déjà globale.

### Conventions et défauts (décidés, non re-discutables sans argument)

- **Esthétique** : Fluent Windows 11 natif à fond, via Wpf.Ui déjà embarqué — `Mica` en backdrop,
  contrôles Fluent, icônes Segoe Fluent, accent système. Zéro nouvelle dépendance de style.
- **Fenêtre** : redimensionnable, ~640×480 par défaut, grille responsive (wrap).
- **Erreurs** (« format non supporté ou fichier introuvable ») : Snackbar/InfoBar éphémère en bas
  de la grille — plus de StatusText dans un bandeau.
- **Libellés en français**, ton sobre. Aucune animation décorative — uniquement les transitions
  natives des contrôles Wpf.Ui.

### Explicitement écarté

- **Tray** : intouché (contenu et libellés actuels conservés).
- **Aperçu animé au survol des vignettes** : coûteux (décodage/mpv par tuile) pour un gain
  faible.
- **Onboarding / premier lancement guidé** : sans objet en usage perso.
- **Navigation multi-vues** (page Réglages séparée) : 6 réglages ne justifient pas une
  navigation ; le flyout suffit.

## Performance (grilling 2026-07-19, confirmé sur mesure Release 2026-08-17)

Budget **< 3 % CPU** quand un wallpaper joue, confirmé — pas révisé — après re-mesure en
Release sur le binaire réel (commit `806bb29`, protocole figé en
[#24](https://github.com/KevinT78/wallflow/issues/24), chiffres bruts dans
`tools/baseline-results.csv`, méthode dans
[#25](https://github.com/KevinT78/wallflow/issues/25)). Tous les scénarios mesurés — y compris le
mp4 1080p, jugé perdu d'avance lors du grilling initial — tiennent le budget avec une marge large
(médiane 0,37-0,51 % CPU en régime ; pic isolé 7,29 % sur `image-fixe-fenetre-ouverte`, jamais en
régime). Machine de référence : i5-6300U + HD 520, portable, **secteur uniquement** (l'app se met
en pause auto sur batterie, cf. Surveillance d'activité — la consommation débranchée est nulle par
construction).

**Critère d'ancrage** ([#26](https://github.com/KevinT78/wallflow/issues/26)) : le % CPU/GPU n'est
pas le critère de jugement au quotidien — personne n'ouvre le Gestionnaire des tâches par
réflexe. Le critère qui compte : **pas de ventilateur qui s'emballe, pas de ralentissement
perçu.** Le chiffre reste la preuve à produire en cas de doute (re-mesure via
`tools/measure-baseline.ps1`), pas le déclencheur.

**Le GPU n'est pas budgété — décision assumée, pas un oubli.** Le cache ffmpeg (ligne suivante)
échange délibérément du CPU contre du GPU ; la re-mesure le confirme chiffré : 24-25 % GPU moyen
en cache webp chaud. Le critère observable ci-dessus couvre CPU et GPU ensemble (un GPU chargé
chauffe aussi le ventilateur) — pas de second budget chiffré à maintenir en parallèle.

**mp4 1080p optimisé** ([#27](https://github.com/KevinT78/wallflow/issues/27), résolu
2026-08-17) : 51,61 % GPU moyen mesuré en #25 ramené à **29,47 % moyen** (médiane de 3 passages,
même protocole, même média de référence) par un seul changement dans `MpvPlayer` —
`cscale=bilinear`. Cause : mpv (`vo=gpu-next`) reconstruit la chrominance 4:2:0 → 4:4:4 à
**chaque frame quelle que soit la résolution** (même à 1:1 vidéo/écran, aucun scaling au sens
classique) ; son filtre de chroma par défaut est cher sur un iGPU faible (HD 520 : moteur 3D
38,9 % → 17,4 %, décodage matériel inchangé ~10 %). Deux autres leviers candidats testés et
écartés par mesure : `vo=gpu` (plus lourd que `gpu-next`, 57,7 % GPU) et `hwdec` explicite
(`d3d11va` ≈ `auto`, aucun gain — `auto` négocie déjà le bon backend). `scale=bilinear` et
`dither=no` ajoutés en combinaison n'apportaient aucun gain mesurable au-delà de `cscale` seul.
Troisième levier du ticket (cadence de rendu) non exploré : `cscale` seul explique déjà tout
l'écart mesuré côté moteur 3D, rien n'indiquait un rendu plus rapide que l'écran n'en a besoin.

- **Bug corrigé (cause racine n°1)** : recréer un player mpv émet un `DisplaySettingsChanged`,
  que l'app retransformait en `Rebuild()` → boucle infinie à ~33 Hz (mp4 : 43 % CPU). Garde par
  signature d'écrans dans `PlayerManager.Rebuild` ; mp4 retombé à ~7 %.
- **Écart : conversion GIF/webp → mp4 en cache.** Le décodage GIF/webp de mpv est 100 % CPU
  (webp 17 Mo mesuré à 27 %) ; aucun tuning mpv ne le ramène sous le budget. Décision : au
  premier Apply d'un `.gif`/`.webp`, conversion ffmpeg en H.264 dans
  `%LOCALAPPDATA%\Wallflow\cache` (clé = chemin+taille+date), l'original joue pendant la
  conversion (~20-30 s pour 17 Mo sur la machine de référence) puis bascule à chaud. Les récents
  et `settings.json` gardent le chemin d'origine. Sans `ffmpeg.exe` ou sur échec : l'original
  joue tel quel — le cache est une optimisation, jamais un point de défaillance. Réconciliation
  (`WallpaperCache.PruneOrphans`) après chaque mutation des récents (ajout au-delà de la limite de
  10, retrait manuel, purge des sources supprimées au démarrage) : tout `.mp4` du dossier cache qui
  ne correspond à la clé d'aucun récent actuel est supprimé.
- **Conséquence distribution** : le zip embarque `ffmpeg.exe` (~145 Mo build BtbN GPL, dans
  `lib/` hors git comme libmpv — un build ≥ 7.1 est requis pour décoder le webp animé).
- **Vignettes async** : la grille s'affiche immédiatement (placeholder = nom du fichier),
  le décodage shell (`IShellItemImageFactory`) part sur le thread pool et remplace le
  placeholder à l'arrivée — l'ouverture ne bloque plus sur les miniatures (cible < 200 ms).

## Points de vigilance connus

- **WorkerW** : technique non documentée par Microsoft (SendMessage 0x052C sur Progman) — peut casser sur une mise à jour Windows. Référence d'implémentation : code source de Lively Wallpaper.
- **Poids** : `mpv-2.dll` pèse ~50-70 Mo → le zip portable fera ~60-80 Mo. Accepté (pas de positionnement « léger »).
- **SmartScreen** : binaire non signé → avertissement au premier lancement. Accepté pour la v1.
- **Portable + auto-start** : si l'utilisateur déplace le dossier, la clé `Run` pointe dans le vide → l'app réécrit sa clé à chaque lancement.
