# Cigarette

A BepInEx IL2CPP mod for Nivalis Nights. This README covers building, testing
and packaging releases.

## Requirements

- .NET SDK 8 (the mod targets .NET 6; checks target .NET 8).
- GNU Make, Bash and standard Unix utilities.
- The game with BepInEx 6 IL2CPP and generated interop assemblies.

Game references are read from `BepInEx/core/` and `BepInEx/interop/`.
The first build may need network access to restore .NET reference packages.
Run commands from the repository root.

## Build and test

```sh
make build GAME_PATH="../Nivalis Nights"
make test
```

Use your game directory, or export `NIVALIS_GAME_PATH`. Output is
`bin/Nivalis.Cigarette.dll`. Tests cover session timing, cigarette burn and audio
processing; they do not require the game or validate Unity interactions.

The Makefile uses `dotnet` from PATH or `.tools/dotnet/dotnet` when present.
Override with `DOTNET=...`. Builds default to `CONFIGURATION=Release`.
Local caches stay in `.tools/`. `make clean` removes generated build output.

## Release package

```sh
make package GAME_PATH="../Nivalis Nights"
```

Builds, checks embedded audio and assembly version, and creates
`bin/Nivalis.Cigarette-<version>.zip` plus a SHA-256 file. Override the output directory
with `PACKAGE_DIR=...`. ZIP creation uses the .NET SDK; Python is not required.
The archive contains only:

```text
BepInEx/plugins/Nivalis.Cigarette.dll
```

## Publish a release

Set matching versions in `src/Cigarette.csproj` and `Plugin.Version`, commit
and push the source, then run with authenticated Git and GitHub CLI (`gh`):

```sh
make deploy GAME_PATH="../Nivalis Nights"
```

Deployment requires a clean checkout, runs checks and creates a missing version
tag. It uploads the ZIP and checksum to a draft, verifies the downloaded assets,
then publishes. Existing tags are never moved and published releases are not
overwritten. Deployment pushes tags, not branches.
