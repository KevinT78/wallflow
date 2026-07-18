# Issue 001 — Prefactor : extraire IPlayerManager + projet de test

> **Statut : ✅ Implémentée** (commits ce90784, f52f6ef, 46b6b16)

## What to build

Extraire une interface `IPlayerManager` de `PlayerManager` pour permettre l'injection de
dépendance dans `AppService`. Créer un projet de test xunit. Aucun changement de comportement
ni d'UI.

## Acceptance criteria

- [x] `IPlayerManager` définie avec les méthodes : `Load`, `PauseAll`, `ResumeAll`,
      `ApplySettings`, `Rebuild`, `Dispose`
- [x] `PlayerManager` implémente `IPlayerManager`
- [x] `AppService` accepte `IPlayerManager` dans son constructeur (l'appelant existant passe
      `PlayerManager` comme avant)
- [x] Pas de régression : `dotnet build` réussi
- [x] Projet `tests/Wallflow.Tests/` créé avec xunit, ciblant `net8.0-windows10.0.19041.0`
- [x] Un test vide qui prouve que le projet compile et s'exécute (`dotnet test` passe)

## Blocked by

None — can start immediately
