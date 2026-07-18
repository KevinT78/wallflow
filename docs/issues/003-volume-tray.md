# Issue 003 — Volume dans le tray

## What to build

Ajouter un contrôle de volume et un indicateur muet dans le menu contextuel de l'icône de
la zone de notification (tray), pour permettre à l'Utilisateur de régler le volume sans
ouvrir la fenêtre.

## Acceptance criteria

- [ ] Le menu du tray contient un sous-menu ou une entrée pour le volume
- [ ] Un slider (ou un équivalent WPF-UI) permet de régler le volume 0-100 depuis le tray
- [ ] L'état muet est visible dans le tray (icône ou texte)
- [ ] Le toggle muet est accessible depuis le tray
- [ ] Les changements depuis le tray sont immédiats et persistés
- [ ] L'état du tray est synchronisé avec celui de la fenêtre (si l'un change, l'autre suit)
- [ ] `dotnet build` réussi

## Blocked by

- Issue 002
