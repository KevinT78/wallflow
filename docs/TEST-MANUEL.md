# Test manuel — Wallflow

Protocole de vérification manuelle sur machine réelle, feature par feature. Pour chaque test :
objectif, prérequis, étapes **exactes** (chemins de menu, libellés de l'UI), résultat attendu,
vérification technique (commande PowerShell à coller).

> Référence UI : barre du bas de la fenêtre = 3 boutons — « Lecture / Pause », « Volume », « Réglages »
> (infobulles). Le menu du tray = icône « Wallflow » dans la zone de notification.
> Invariants purs couverts par `dotnet test` ; ce document couvre le live-only.

## Préparation

1. Build et lancement :
   ```powershell
   dotnet build src\Wallflow\Wallflow.csproj
   .\src\Wallflow\bin\Debug\net8.0-windows10.0.19041.0\wallflow.exe
   ```
   Prérequis : `lib\libmpv-2.dll` et `lib\ffmpeg.exe` existent (copiés par le csproj à côté de l'exe).
   Fenêtre visible = la fenêtre Wallflow. Wallpaper jouant = vérifiable à l'œil sur le bureau.
2. Journal des traces silencieuses (à garder ouvert pendant tous les tests) :
   ```powershell
   Get-ChildItem "$env:LOCALAPPDATA\Wallflow\logs" -Filter *.log | Select-Object -Last 1 | Get-Content -Tail 50
   ```
   Chemins de référence :
   - Réglages : `%LOCALAPPDATA%\Wallflow\settings.json`
   - Journal : `%LOCALAPPDATA%\Wallflow\logs\wallflow-YYYYMMDD.log`

---

## 1. Erreur de lecture → Snackbar (nouveau)

**Objectif** : un fichier illisible est signalé à l'Utilisateur au lieu d'un écran figé silencieux.

**Étapes** :
1. Créer un fichier corrompu (texte renommé en `.mp4`) :
   ```powershell
   Set-Content -Path "$env:TEMP\corrompu.mp4" -Value "ceci n'est pas une video"
   ```
2. Glisser-déposer `%TEMP%\corrompu.mp4` dans la fenêtre Wallflow (le voile « Dépose pour appliquer »
   apparaît pendant le drag).

**Résultat attendu** :
- Une Snackbar rouge apparaît **en bas de la fenêtre**, titre « Impossible d'appliquer », pendant 4 s.
- La grille des `Récents` et le wallpaper courant ne changent pas.

**Vérification technique** :
- `settings.json` : `Recents` ne contient **pas** `corrompu.mp4` ; `LastWallpaper` inchangé.
- Journal : une ligne `WARN`/`ERROR` évoquant la lecture (mpv) est apparue.

---

## 2. Économie d'énergie (nouveau)

**Objectif** : pause auto quand le mode Économie d'énergie est actif, **même si le PC est branché**
(cas corrigé : avant, seul `PowerLineStatus` détectait la batterie).

**Étapes** :
1. Appliquer un wallpaper (drop d'une vidéo).
2. Activer le mode **manuellement**, PC branché :
   - Win10 : Paramètres → Système → **Batterie** → « Économiseur de batterie » → activer.
   - Win11 : Paramètres → Système → **Alimentation et batterie** → « Efficacité énergétique » → activer.
3. Observer le bureau ; désactiver ensuite.

**Résultat attendu** :
- Pause **immédiate** (< 1 s) à l'activation.
- Reprise **immédiate** à la désactivation.
- Le menu du tray « Pause » **reste décoché** (c'est une pause auto, pas manuelle).

**Vérification technique** : rien de persistant (état en mémoire) ; vérifier la réactivité = immédiate.

**Limite connue** : la détection d'économie d'énergie (`PowerGetActiveOverlayScheme`) n'existe que
sur **Windows 11**. Sur Windows 10 elle est neutralisée (détectée une fois absente puis court-circuitée,
voir log `WARN … ActivityMonitor.Poll`) : seules la batterie et le plein écran déclenchent la pause auto.

---

## 3. Plein écran — réactivité du hook (nouveau)

**Objectif** : la détection est événementielle (hook `EVENT_SYSTEM_FOREGROUND`), plus de latence de poll.

**Étapes** :
1. Appliquer une vidéo animée (avec mouvement visible).
2. Lancer une vidéo en **plein écran** (ex. VLC : `Alt`+`Entrée`, ou un jeu fenêtré en plein écran).
3. Quitter le plein écran.

**Résultat attendu** :
- Pause quasi instantanée (< 100 ms) au passage en plein écran.
- Reprise immédiate au retour.

**Précisions** :
- Une fenêtre simplement **maximisée** ne doit **pas** déclencher la pause (bounds ≠ moniteur entier).
- Ce test valide aussi la marche du hook : en cas d'échec du hook (log `WARN … SetWinEventHook`),
  le comportement revient au poll 2 s (pause en ≤ 2 s) — à distinguer du résultat ci-dessus.

---

## 4. Sortie de veille → Resync (nouveau)

**Objectif** : mpv peut perdre son contexte D3D pendant la veille ; le wallpaper doit reprendre à la
reprise (`PowerModeChanged` = `Resume` → `Resync`).

**Étapes** :
1. Appliquer une vidéo avec mouvement visible, s'assurer qu'elle joue.
2. Mettre le PC en veille : Démarrer → ⏻ → **Mettre en veille**.
3. Réveiller (touche/clic), observer le bureau.

**Résultat attendu** :
- À la reprise, la vidéo **avance à nouveau** (le compteur de mouvement repart), sans écran figé.
- Pas de clignotement noir prolongé (teardown + reload complet des players).

**Vérification technique** :
- Journal : une ligne `INFO … Restauration du dernier wallpaper au démarrage` ne doit **pas** apparaître
  (la reprise ne relance pas l'app : c'est le `Resync` in-process). Aucune erreur `mpv` dans le log.

---

## 5. Volume → une seule écriture disque (nouveau)

**Objectif** : pendant le drag du slider, la valeur est poussée à chaud aux players **sans** écrire
`settings.json` ; persistance unique à la fin du drag.

**Étapes** :
1. Ouvrir un 2ᵉ terminal PowerShell avec un surveillant d'écriture (il imprime `écrit` à chaque
   modification du fichier) :
   ```powershell
   $f = "$env:LOCALAPPDATA\Wallflow\settings.json"
   Get-Content $f
   while ($true) {
     $t = (Get-Item $f).LastWriteTime
     Start-Sleep -Milliseconds 200
     if ((Get-Item $f).LastWriteTime -ne $t) { "écrit: $((Get-Item $f).LastWriteTime)" }
   }
   ```
2. Dans Wallflow : bouton **Volume** (barre du bas) → le flyout s'ouvre (slider + « Muet »).
3. Faire glisser le slider **lentement** sur toute la course (~3 s), puis relâcher.

**Résultat attendu** :
- **Pendant** le drag : aucune ligne `écrit` (le volume change bien à l'oreille, mais pas sur disque).
- **Au relâchement** : exactement **une** ligne `écrit`.

**Vérification technique** :
- `Get-Content "$env:LOCALAPPDATA\Wallflow\settings.json"` → `"Volume"` = valeur finale du slider
  (ex. `76`), valeur entière.

---

## 6. Purge des récents morts (nouveau)

**Objectif** : un fichier supprimé du disque disparaît de la grille **et** du JSON (avant : le JSON
gardait le chemin mort).

**Étapes** :
1. Appliquer 3 fichiers (A, B, C) — chacun passe en tête des `Récents`.
2. Supprimer B du disque (Explorer : clic droit → Supprimer).
3. Déclencher une sauvegarde : bouton **Réglages** → basculer « Boucle infinie » ou « Muet » (n'importe
   quelle action qui écrit), ou `Quitter` proprement.

**Résultat attendu** :
- Fenêtre ouverte : B n'apparaît plus dans la grille.

**Vérification technique** :
```powershell
Get-Content "$env:LOCALAPPDATA\Wallflow\settings.json" | Select-String "Recents"
```
- Le chemin de B a disparu du tableau `Recents` ; A et C restent.

---

## 7. Diaporama résilient au crash (issue 008)

**Objectif** : le snapshot du diaporama Windows est persisté ; un crash ne perd plus la config.

**Étapes** :
1. Configurer le fond natif en **diaporama** : Paramètres → Personnalisation → Arrière-plan →
   **Diaporama**, choisir un dossier d'images, intervalle **1 minute**.
2. Appliquer un wallpaper Wallflow.
3. Vérifier la coupure : attendre > 1 min sans action — l'image du diaporama ne doit **pas** changer
   (anti-flicker).
4. Tuer brutalement l'app (crash simulé) :
   ```powershell
   taskkill /F /IM wallflow.exe
   ```
5. Relancer `wallflow.exe` ; attendre que le wallpaper revienne.
6. Depuis la fenêtre : bouton **Réglages** → « **Retirer le fond d'écran** ».

**Résultat attendu** :
- Étape 3 : défilement stoppé tant que le wallpaper est actif.
- Étape 5 : le wallpaper revient au démarrage.
- Étape 6 : le bureau natif revient **et** le diaporama **défile à nouveau** (image change après ~1 min),
  avec les mêmes images et le même intervalle qu'à l'étape 1.

**Vérification technique** :
- Après l'étape 2 **et après le crash**, avant l'étape 6 :
  ```powershell
  Get-Content "$env:LOCALAPPDATA\Wallflow\settings.json" | Select-String "SlideshowSnapshot" -Context 0,6
  ```
  → bloc présent avec `FolderPath`, `IntervalMs`, `Shuffle`.
- Après l'étape 6 : le bloc `SlideshowSnapshot` a **disparu** du JSON.

---

## 8. Empaquetage

**Objectif** : le script produit le zip portable avec les binaires natifs.

**Étapes** :
```powershell
.\tools\package.ps1
Expand-Archive -Path .\dist\wallflow-dev-win-x64.zip -DestinationPath "$env:TEMP\wallflow-pkg"
.\"$env:TEMP\wallflow-pkg\wallflow.exe"
```

**Résultat attendu** :
- Zip créé dans `dist\` (nom = dernier tag git, sinon `dev`).
- L'exe dézippé se lance : la fenêtre Wallflow s'ouvre, un drop applique un wallpaper.

**Vérification technique** :
```powershell
[System.IO.Compression.ZipFile]::OpenRead(".\dist\wallflow-dev-win-x64.zip").Entries.FullName
```
→ contient `wallflow.exe`, `libmpv-2.dll`, `ffmpeg.exe`.
- Portable : déplacer le dossier extrait, relancer → l'app marche (elle réécrit sa clé `Run` à son chemin).

---

## 9. Régression générale

| # | Étape exacte | Résultat attendu |
|---|---|---|
| 9.1 | Drop un **GIF**, un **WebP animé**, un **.mp4**, un **.webm**, une **image .png** | Chaque fichier s'applique : image animée ou fixe, **muet** par défaut, en **boucle**, cadrage **cover** (les bords sont rognés, pas de bandes noires) |
| 9.2 | Clic **Réglages** → radios Cadrage | Cover/Fit/Fill changent le cadrage **immédiatement** |
| 9.3 | Clic **Réglages** → radios Vitesse (0.5×/1×/1.5×/2×) | La vitesse de lecture change immédiatement |
| 9.4 | Clic **Réglages** → « Boucle infinie » off | La vidéo s'arrête à la fin |
| 9.5 | Ouvrir la fenêtre après plusieurs Apply | La vignette du wallpaper actif porte **bordure + badge** ; les 10 derniers s'affichent |
| 9.6 | Clic droit sur une vignette | Menu « Retirer des récents » et « Ouvrir l'emplacement du fichier » (Explorer ouvre le fichier sélectionné) |
| 9.7 | Clic droit → « Retirer des récents » sur la vignette **de l'actif** | La vignette quitte la grille mais la **lecture continue** (jamais de retrait du fond d'écran) |
| 9.8 | Bouton **Réglages** → « Retirer le fond d'écran » | Le bureau natif revient (pas de blanc), l'app reste dans le tray ; re-cliquer un `Récents` ré-applique |
| 9.9 | Tray → « Retirer le fond d'écran » | Même comportement que 9.8 |
| 9.10 | Tray → « Quitter » | L'app se ferme, le bureau natif revient (pas de blanc) |
| 9.11 | Tray → « Pause » / fenêtre → « Lecture / Pause » | Pause manuelle : l'image se fige, ne se lève que par l'Utilisateur |

## 10. Démarrage avec Windows (clé `Run`)

**Étapes** :
1. Clic **Réglages** → activer « **Démarrer avec Windows** ».
2. Vérifier la clé :
   ```powershell
   reg query "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v Wallflow
   ```
   → `REG_SZ` pointant vers le chemin **courant** de `wallflow.exe`.
3. Déplacer le dossier de l'exe, relancer l'app une fois, re-vérifier la clé → chemin **mis à jour**.
4. Redémarrer Windows (ou se déconnecter/reconnecter) → le wallpaper revient sans rien lancer.

## Nettoyage après tests

```powershell
Remove-Item "$env:TEMP\corrompu.mp4" -ErrorAction SilentlyContinue
Remove-Item "$env:TEMP\wallflow-pkg" -Recurse -Force -ErrorAction SilentlyContinue
```
- Remettre le fond natif Windows sur **image fixe** si le test 7 l'a laissé en diaporama.
- Si « Démarrer avec Windows » a été activé pour le test 10 et qu'on ne veut pas le garder :
  Paramètres → Réglages de Wallflow → désactiver.
