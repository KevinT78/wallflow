# Wallflow

Wallflow est une application Windows qui applique un **wallpaper** animé (GIF, WebP animé, vidéo) ou
fixe derrière les icônes du bureau, sur tous les écrans, avec une pause automatique quand une
application passe en plein écran ou que le PC est sur batterie.
Ce fichier est un glossaire — le vocabulaire canonique du domaine, rien d'autre.

> **Vue produit** : [docs/VUE-PRODUIT.md](./docs/VUE-PRODUIT.md) · **Architecture technique** :
> [docs/ARCHITECTURE-TECHNIQUE.md](./docs/ARCHITECTURE-TECHNIQUE.md) · **Décisions produit** :
> [DESIGN.md](./DESIGN.md).
> _Généré au commit `f897b4a` (2026-08-12, + arbre de travail modifié)._
<!-- doc-provenance: commit=f897b4a generated=2026-08-12 -->

## Langage

### Acteurs (rôles)

**Utilisateur** :
La personne qui utilise le PC. Unique acteur du produit : il dépose un fichier, il obtient un
`Wallpaper`. Pas de compte, pas de rôle, pas de multi-utilisateur.
_Avoid_: user, client

### Objets métier

**Wallpaper** :
Le fichier média appliqué en fond d'écran : GIF, WebP animé, mp4/webm ou image fixe. Un seul
`Wallpaper` actif à la fois, affiché à l'identique sur tous les écrans, selon les `Réglages de
lecture` courants (par défaut : en boucle, muet, cadré « cover », vitesse normale). C'est le
dernier appliqué qui est restauré au démarrage de Windows.
_Avoid_: fond d'écran animé vs statique (pas de distinction dans le produit : même objet), thème,
skin

**Récents** :
Liste des 10 derniers `Wallpaper` appliqués, affichée en grille (le « héros » de la fenêtre) avec
leurs vignettes. Cliquer un récent le ré-applique ; la vignette du `Wallpaper` actif y porte un
marquage (bordure + badge). Clic droit sur une vignette : `Retirer des récents` ou ouvrir
l'emplacement du fichier. C'est la seule « bibliothèque » du produit.
_Avoid_: bibliothèque, collection, favoris (rien n'est épinglable)

**Retirer des récents** :
Action de l'`Utilisateur` (clic droit sur une vignette) qui ôte une entrée de la liste des
`Récents`. N'affecte **pas** la lecture : retirer la vignette du `Wallpaper` actif ne l'arrête pas
(il continue de jouer, il n'est simplement plus dans la grille). Distinct de `Retirer le fond
d'écran`.
_Avoid_: supprimer (aucun fichier n'est effacé du disque)

**Pause manuelle** :
Arrêt de la lecture décidé par l'`Utilisateur` (bouton de la fenêtre ou menu du tray). Ne se lève
que par l'`Utilisateur`.
_Avoid_: stop (la lecture reprend où elle en était, rien n'est déchargé)

**Pause auto** :
Arrêt de la lecture décidé par l'application quand une app est en plein écran ou que le PC est sur
batterie / économie d'énergie. Se lève seule quand la condition disparaît.

**Retirer le fond d'écran** :
Action de l'`Utilisateur` (bouton fenêtre ou menu tray) qui supprime le `Wallpaper` actif : il n'y
a plus de wallpaper courant, le bureau Windows natif est rendu (`Restauration du bureau`), mais
l'app reste vivante dans le tray. Réversible en cliquant un `Récents`. Persistant : au démarrage
suivant, sans wallpaper courant, le bureau reste normal. Distinct de `Pause manuelle` (qui fige
l'image sans rendre le bureau) et de `Quitter` (qui ferme l'app).
_Avoid_: stop, arrêter (ambigus avec la pause)

**Réglages** :
Ce que le produit persiste et restaure : démarrage avec Windows, pause auto, les `Réglages de
lecture`, le `Wallpaper` courant et les `Récents`.
_Avoid_: préférences, options, settings (dans la prose)

**Réglages de lecture** :
Les trois réglages qui gouvernent comment le `Wallpaper` est joué : **cadrage** (cover / fit /
fill), **boucle** (on/off) et **vitesse** (0.25x–4x). Globaux (pas par wallpaper), appliqués
immédiatement, persistés. Le son est retiré du produit (`Wallpaper` toujours muet, aucun contrôle
de volume) — voir écart DESIGN.md du 2026-08-10.
_Avoid_: options de lecture, paramètres vidéo, volume, muet (ce ne sont plus des réglages)

**Diaporama Windows** :
Le réglage natif de Windows qui fait défiler périodiquement un dossier d'images en fond d'écran
(intervalle et mélange réglés dans les paramètres Windows). Distinct du `Wallpaper` de Wallflow, qui
n'a aucune notion de diaporama (un seul `Wallpaper` actif à la fois). Tant qu'un `Wallpaper` Wallflow
est actif, Wallflow coupe ce diaporama pour supprimer son tick périodique — qui repeignait l'écran
par-dessus le `Wallpaper` (flicker) — puis le restaure à l'identique au `Retirer le fond d'écran` ou
au `Quitter`.
_Avoid_: slideshow, diaporama (sans « Windows » — ce n'est pas une fonction de Wallflow)

### Faux-amis à ne pas confondre

- **Pause manuelle** vs **Pause auto** : deux drapeaux distincts — la fin d'un jeu plein écran lève
  la pause auto mais ne doit jamais lever une pause manuelle posée avant.
- **`Wallpaper`** (l'objet du produit) vs **fond d'écran Windows** (le réglage natif de l'OS) :
  Wallflow n'utilise pas le mécanisme natif ; il affiche sa propre fenêtre derrière les icônes. Le
  réglage natif reste enregistré, mais l'écran ne le réaffiche **pas** tout seul quand Wallflow
  retire sa fenêtre : Wallflow doit explicitement demander à Windows de le repeindre
  (`Restauration du bureau`), sinon le bureau reste blanc.

**Restauration du bureau** :
Action de rendre à l'`Utilisateur` son fond d'écran Windows natif, en demandant à l'OS de le
repeindre. Déclenchée quand l'app quitte et par `Retirer le fond d'écran`. Sans elle, retirer la
fenêtre de Wallflow laisse un bureau blanc.
- **`Wallpaper`** (l'objet de Wallflow) vs **`Diaporama Windows`** (le réglage natif de l'OS qui fait
  défiler des images) : Wallflow ne propose pas de diaporama ; il coupe celui de Windows pendant qu'un
  `Wallpaper` est actif, puis le restaure. Ne pas confondre non plus avec les `Récents`, qui ne
  défilent pas tout seuls.
- **`Récents`** (historique re-cliquable) vs bibliothèque (n'existe pas : aucun fichier n'est copié
  ni géré, Wallflow pointe vers les fichiers de l'`Utilisateur` là où ils sont).
- **`Retirer des récents`** (ôte une vignette de la grille, la lecture continue) vs `Retirer le fond
  d'écran` (arrête le `Wallpaper` actif et rend le bureau natif). Retirer des récents l'entrée du
  wallpaper actif ne déclenche jamais un `Retirer le fond d'écran`.
