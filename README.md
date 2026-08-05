# Wallflow

Application Windows qui applique un wallpaper animé (GIF, WebP animé, vidéo) ou fixe derrière les
icônes du bureau, sur tous les écrans, avec pause automatique en plein écran ou sur batterie.

> Glossaire : [CONTEXT.md](./CONTEXT.md) · Doc : [docs/README.md](./docs/README.md) ·
> Décisions produit : [DESIGN.md](./DESIGN.md)

## Mise en place

Deux binaires natifs sont **exclus du dépôt** (trop lourds pour git) et doivent être posés dans
`lib/` avant de builder :

| Fichier | Rôle | Où le prendre |
|---------|------|---------------|
| `lib/libmpv-2.dll` | lecteur qui rend le wallpaper | [shinchiro](https://github.com/shinchiro/mpv-winbuild-cmake/releases/latest) — asset `mpv-dev-x86_64-*.7z` |
| `lib/ffmpeg.exe` | conversion GIF/WebP animé → mp4 du cache | [BtbN](https://github.com/BtbN/FFmpeg-Builds/releases/latest) — asset `ffmpeg-n*-win64-gpl-*.zip`, **≥ 7.1** |

Un wizard interactif fait la procédure avec toi — il ouvre les pages de téléchargement, dit quel
fichier prendre, copie les binaires dans `lib/` et contrôle la version de ffmpeg :

```bash
bash tools/setup-binaries.sh
```

Environ 6 minutes. Le script est idempotent : relance-le sans risque, il saute ce qui est déjà en
place.

> Le `.csproj` copie ces binaires **sous condition d'existence** : sans eux, `dotnet build` reste
> vert mais l'app ne peut lire aucun wallpaper. C'est l'état de `lib/` qui fait foi, pas le build.

## Build

```bash
dotnet build
dotnet test
```
