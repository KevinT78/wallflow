# Issue 004 — Boucle infinie cassée sur les WebP animés

> **Statut : 🔴 Rouverte — tentative revertée.** Le changement proposé
> (`image-display-duration=inf` → `keep-open=yes`) a été implémenté puis **annulé** :
> vérifié en live, `keep-open=yes` fige le WebP animé sur sa **1ère** image (pire que l'état
> initial). L'hypothèse des *Implementation Decisions* est donc **fausse** sur la libmpv
> embarquée (v0.41.0). Code revenu à `image-display-duration=inf`. À ne retenter qu'avec
> vérification live à chaque essai d'option mpv.

> **Triage** : `ready-for-agent`
> Issu de la session grilling du 2026-07-18.

## Problem Statement

En tant qu'`Utilisateur`, quand j'applique un **WebP animé** comme `Wallpaper`, il **ne boucle
pas** : il joue une fois puis se fige sur une image. Un GIF équivalent, lui, boucle correctement.

## Solution

Un `Wallpaper` WebP animé **boucle indéfiniment** comme un GIF ou une vidéo, indépendamment du
format déposé, tout en gardant les images fixes affichées en permanence.

## User Stories

1. En tant qu'`Utilisateur`, je veux qu'un `Wallpaper` WebP animé boucle indéfiniment, afin
   d'obtenir le même rendu qu'avec un GIF ou une vidéo.
2. En tant qu'`Utilisateur`, je veux qu'une image WebP **fixe** reste affichée en permanence, afin
   qu'un fond statique ne disparaisse pas au bout d'une seconde.
3. En tant qu'`Utilisateur`, je veux que le comportement de boucle soit indépendant du format
   (GIF, WebP, mp4/webm), afin de ne pas avoir à me soucier du type de fichier que je dépose.
4. En tant qu'`Utilisateur`, je veux que le réglage `Boucle` (on/off) continue de fonctionner sur
   un WebP animé, afin de pouvoir aussi le jouer une seule fois si je le choisis.

## Implementation Decisions

- Dans `MpvPlayer`, remplacer l'option figée `image-display-duration=inf` par `keep-open=yes` (en
  conservant `loop-file=inf`). Rationale : ffmpeg classe souvent un WebP **animé** comme *image*,
  donc `image-display-duration=inf` le fige sur une frame ; `keep-open=yes` tient la dernière frame
  d'une image fixe **et** laisse `loop-file` boucler une animation.
- Un `.webp` peut être fixe **ou** animé et rien ne les distingue par l'extension : le même jeu
  d'options doit couvrir les deux cas.
- Interaction avec le réglage `Boucle` : `ApplyLoop(false)` → `loop-file=no` ; avec `keep-open=yes`
  une animation non bouclée se fige sur sa dernière frame au lieu de finir sur du noir.

## Testing Decisions

- **Aucun test unitaire** : c'est un réglage natif du moteur mpv, non observable sans rendu réel.
  `MpvPlayer` n'a aucun test aujourd'hui (P/Invoke), et on ne rend pas ce module testable ici.
- **Vérification live obligatoire** (build → run) sur des fichiers réels :
  - un WebP **animé** boucle indéfiniment ;
  - une image WebP **fixe** reste affichée en permanence ;
  - un GIF et une vidéo continuent de boucler (non-régression) ;
  - `Boucle` = off sur un WebP animé → joue une fois puis se fige (pas de noir).

## Out of Scope

- Détection programmatique animé/fixe côté application (on laisse mpv décider).
- Rendre `MpvPlayer` testable unitairement.

## Further Notes

- Le combo `keep-open=yes` + `loop-file=inf` est l'hypothèse la plus probable mais **doit être
  confirmé en live** sur la libmpv embarquée — si le comportement diffère, ajuster les options mpv
  reste dans le périmètre de cette issue.
