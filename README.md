# Cigarette

A BepInEx IL2CPP mod for **Nivalis Nights**, adding cigarettes, joints and an inhalant mask at railings or while standing still. This repository contains smoking animations, railing alignment, visual effects and an optional integration API.

## Requirements

- .NET SDK 8 (the mod targets .NET 6; managed checks target .NET 8).
- GNU Make, Bash and standard Unix utilities.
- The game initialized with BepInEx 6 IL2CPP.
- Mod Companion 1.0.3 or newer in `BepInEx/plugins/` for compilation and the configurable standing hotkey (optional for railing-only play).

Launch the game with BepInEx once to generate bindings. Builds reference its `BepInEx/core/` and `BepInEx/interop/` assemblies. Run commands from this repository’s root.

## Build and test

```sh
make build GAME_PATH="/path/to/Nivalis Nights"
make test
```

Use `make build-api` for the API-enabled variant. Outputs go to `bin/standalone/` and `bin/api/`. You can export `NIVALIS_GAME_PATH` instead of passing `GAME_PATH`; use `DOTNET=...` if the SDK is not on PATH.

Managed checks cover session timing, audio, positioning and the API contract. Native interactions and rendering also need in-game verification.

## Install and package

Close the game before installing a development build.

| Target | Purpose |
| --- | --- |
| `make install` | Build and install the standalone mod |
| `make install-api` | Build and install the API-enabled mod and API library |
| `make package` | Build, test and package both variants with SHA-256 files |
| `make clean` | Remove build outputs |

Pass `GAME_PATH="..."` to game-dependent targets. Release archives go to `bin/`:

- **`Nivalis.Cigarette-<version>.zip`** — standalone, for normal play.
- **`Nivalis.Cigarette-<version>-API.zip`** — includes integration support for cooperating mods.

Extract one archive into the game directory. Both variants replace the same mod DLL under `BepInEx/plugins/`; install only one at a time. Configuration is preserved.

## Configuration and controls

Settings live in `BepInEx/config/cigarette.cfg`, including per-item durations in **seconds**, audio levels and after-effects.

Face a nearby railing with empty hands and look down. Left-click to choose an item. For standing smoking, press **F8** with empty hands away from an interaction target; rebind this under **Mod Companion → Cigarette → Controls → Smoke while standing**. Standing uses a single chest-height resting pose. Movement or Escape stops the session. The sprint toggle is reserved for future walking support and has no effect yet. Joints produce visual effects and hallucinations, while the mask produces temporary slow motion. The boat’s rear-deck rails are supported.

## Mod integration API

The optional API lets other mods start smoking sessions, react to consumption, control item availability and manage effects. See [API documentation](API.md) for setup, methods, events and examples.

## Publish a release

Set matching versions in `src/Cigarette.csproj` and `Plugin.Version`, update `docs/releases/`, then commit and push. With authenticated Git and GitHub CLI:

```sh
make deploy GAME_PATH="/path/to/Nivalis Nights"
```

Deployment requires a clean checkout, builds both archives, creates a missing version tag, and verifies uploaded files before publishing. It pushes tags, not branches, and does not overwrite published releases.

## Source layout

| Location | Contents |
| --- | --- |
| `src/` | Plugin, smoking, railing alignment and effects |
| `api/` | Public integration contract |
| `assets/` | Embedded models, icons and audio |
| `tests/` | Managed regression and package checks |
| `dev/` | Optional API test mod and railing tools |
| `tools/` | Build, installation and release helpers |

See [local railing development](docs/rail-development.md) for unsupported-surface tooling. Development mods are excluded from release archives.
