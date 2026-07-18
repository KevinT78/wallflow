# Wallflow

Wallflow est une application Windows qui applique un **wallpaper** animé (GIF, WebP animé, vidéo) ou
fixe derrière les icônes du bureau, sur tous les écrans, avec une pause automatique quand une
application passe en plein écran ou que le PC est sur batterie.
Ce fichier est un glossaire — le vocabulaire canonique du domaine, rien d'autre.

> **Vue produit** : [docs/VUE-PRODUIT.md](./docs/VUE-PRODUIT.md) · **Architecture technique** :
> [docs/ARCHITECTURE-TECHNIQUE.md](./docs/ARCHITECTURE-TECHNIQUE.md) · **Décisions produit** :
> [DESIGN.md](./DESIGN.md).
> _Généré au stade kickoff (2026-07-18), avant tout code — dérivé de DESIGN.md et du system design._
<!-- doc-provenance: commit=none generated=2026-07-18 stage=kickoff -->

## Langage

### Acteurs (rôles)

**Utilisateur** :
La personne qui utilise le PC. Unique acteur du produit : il dépose un fichier, il obtient un
`Wallpaper`. Pas de compte, pas de rôle, pas de multi-utilisateur.
_Avoid_: user, client

### Objets métier

**Wallpaper** :
Le fichier média appliqué en fond d'écran : GIF, WebP animé, mp4/webm ou image fixe. Un seul
`Wallpaper` actif à la fois, affiché à l'identique sur tous les écrans, en boucle, muet, cadré
« cover ». C'est le dernier appliqué qui est restauré au démarrage de Windows.
_Avoid_: fond d'écran animé vs statique (pas de distinction dans le produit : même objet), thème,
skin

**Récents** :
Liste des 10 derniers `Wallpaper` appliqués, affichée en grille dans la fenêtre avec leurs
vignettes. Cliquer un récent le ré-applique. C'est la seule « bibliothèque » du produit.
_Avoid_: bibliothèque, collection, favoris (rien n'est épinglable)

**Pause manuelle** :
Arrêt de la lecture décidé par l'`Utilisateur` (bouton de la fenêtre ou menu du tray). Ne se lève
que par l'`Utilisateur`.
_Avoid_: stop (la lecture reprend où elle en était, rien n'est déchargé)

**Pause auto** :
Arrêt de la lecture décidé par l'application quand une app est en plein écran ou que le PC est sur
batterie / économie d'énergie. Se lève seule quand la condition disparaît.

**Réglages** :
Les deux seuls interrupteurs du produit : démarrage avec Windows, et pause auto. Persiste aussi le
`Wallpaper` courant et les `Récents`.
_Avoid_: préférences, options, settings (dans la prose)

### Faux-amis à ne pas confondre

- **Pause manuelle** vs **Pause auto** : deux drapeaux distincts — la fin d'un jeu plein écran lève
  la pause auto mais ne doit jamais lever une pause manuelle posée avant.
- **`Wallpaper`** (l'objet du produit) vs **fond d'écran Windows** (le réglage natif de l'OS) :
  Wallflow n'utilise pas le mécanisme natif ; il affiche sa propre fenêtre derrière les icônes. Le
  fond d'écran natif reste intact derrière et réapparaît si Wallflow quitte.
- **`Récents`** (historique re-cliquable) vs bibliothèque (n'existe pas : aucun fichier n'est copié
  ni géré, Wallflow pointe vers les fichiers de l'`Utilisateur` là où ils sont).
