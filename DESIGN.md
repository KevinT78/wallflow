# Wallflow — DESIGN.md

Source de vérité produit. Tout écart doit être argumenté ici avant d'être codé.

## Produit

- **Quoi** : app Windows distribuable qui applique un fond d'écran animé (GIF, WebP animé, mp4/webm) ou fixe, derrière les icônes du bureau.
- **Positionnement** : pas de différenciateur revendiqué face à Wallpaper Engine / Lively — l'effort est donc calibré au minimum viable, zéro feature spéculative.
- **Non-objectifs v1** : wallpapers HTML/interactifs, bibliothèque en ligne, wallpaper différent par écran, réglages de lecture, multi-plateforme.

## Décisions figées (session de grilling 2026-07-18)

| Sujet | Décision |
|---|---|
| Cible | App distribuable, Windows 10/11 uniquement |
| Formats | GIF, WebP animé, mp4/webm, images fixes — pas de HTML |
| Stack | .NET 8 + WPF, libmpv comme moteur de lecture unique, P/Invoke pour WorkerW |
| Multi-écran | Même wallpaper sur tous les écrans (une instance mpv par écran) |
| Performance | Pause automatique si app en plein écran OU sur batterie/économie d'énergie. Non négociable. |
| Lecture | Défauts figés : muet, cadrage cover, boucle infinie. Aucun réglage de lecture en v1. |
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

## Points de vigilance connus

- **WorkerW** : technique non documentée par Microsoft (SendMessage 0x052C sur Progman) — peut casser sur une mise à jour Windows. Référence d'implémentation : code source de Lively Wallpaper.
- **Poids** : `mpv-2.dll` pèse ~50-70 Mo → le zip portable fera ~60-80 Mo. Accepté (pas de positionnement « léger »).
- **SmartScreen** : binaire non signé → avertissement au premier lancement. Accepté pour la v1.
- **Portable + auto-start** : si l'utilisateur déplace le dossier, la clé `Run` pointe dans le vide → l'app réécrit sa clé à chaque lancement.
