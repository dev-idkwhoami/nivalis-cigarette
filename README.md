# Cigarette

A BepInEx IL2CPP mod for **Nivalis Nights**, adding cigarettes, joints and an inhalant mask at railings or while standing or walking. This repository contains smoking animations, railing alignment, visual effects and an optional integration API.

## Requirements

- .NET SDK 8 (the mod targets .NET 6; managed checks target .NET 8).
- GNU Make, Bash and standard Unix utilities.
- The game initialized with BepInEx 6 IL2CPP.
- Mod Companion is included in both downloads and provides the configurable hotkey and in-game settings. Builds use the shared DLL in `dependencies/`.

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

Extract one archive into the game directory. Both variants replace the same mod DLL under `BepInEx/plugins/`; install only one at a time. Configuration is preserved. Both archives include `Nivalis.ModCompanion.dll` at the shared plugin path; allow it to replace the existing copy. Archives contain only DLLs.

## Configuration and controls

Settings live in `BepInEx/config/cigarette.cfg`, including per-item durations in **seconds**, audio levels and after-effects.

Face a nearby railing with empty hands and look down. Left-click to choose an item. For standing smoking, press **X** with empty hands away from an interaction target; rebind this under **Mod Companion → Cigarette → Controls → Start / stop smoking**. Standing uses a single chest-height resting pose. Standing and walking share one continuous session, with mostly vertical sway following the native free-arm animation. Native hand playback runs at 50% speed while walking during free smoking and at normal speed while sprinting; player movement speed and smoking timing are unchanged. Press the same hotkey again or interact with something to end free smoking; items still finish when consumed. Sprinting is disabled by default and can be enabled in Mod Companion. Movement or Escape still stops railing smoking. Joints produce visual effects and hallucinations, while the mask produces temporary slow motion. The boat’s rear-deck rails are supported.

In Mod Companion, **General** lets you enable cigarettes, cannabis and the inhalant mask individually. **Selection mode** is a select field with **Classic** (default) and **Dynamic**. Classic starts directly with one available item and opens the wheel with multiple items. Dynamic starts the last successfully used item on a short tap/release of the smoking hotkey or railing left-click; on first use, or when the remembered item is unavailable, it uses the first available item (cigarette, cannabis, then inhalant mask). The remembered item is shared between standing and railing smoking and saved across restarts. Hold either control to open the wheel, even with one available item. **Wheel hold duration (seconds)** is a slider from 0.1 to 2 seconds in 0.05-second steps, defaulting to 0.25 seconds. During active free smoking, Dynamic mode stops the session only on a short tap of the smoking hotkey; holding it leaves the session running without opening the wheel. Changing gameplay context or losing the railing while holding cancels the pending input. Turning everything off prevents new smoking sessions. User preferences take precedence over API availability restrictions.

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

## Sloped railing contacts

Hand probes follow the verified railing slope instead of assuming both hands
are at the aimed centre's height. This fixes the probe gap around 20 degrees on
otherwise supported slopes. The existing roughly 32-degree top-surface limit,
physical surface checks, hand reach and collision checks remain in place.
Failed hand placement logs distinguish a missed surface, another collider,
height/slope rejection and an unreachable arm pose. Known issue: some winter railings near snow still fail hand placement when
another collider obstructs the probes. This change does not bypass collision or
enlarge player reach. Managed geometry checks do not verify the rendered hand pose.
