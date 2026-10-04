# Local railing development

`Nivalis.Cigarette.RailDev.dll` is an optional BepInEx developer plugin. It depends
on Cigarette; Cigarette does **not** depend on it. Normal builds and releases do not
build or include this DLL. The main detector retains the existing approved rails,
reach limits and top-surface checks; its name predicate is simply a separate method
so this tool can extend it at runtime.

## Use in-game

Restart once after installing both updated DLLs. Stand near an unsupported railing,
aim at its physical surface, and press a number key (no modifier):

| Key | Action |
| --- | --- |
| 1 | Capture the current rejection reason, scene/hierarchy/mesh, player and hit positions, all colliders along the aim ray, layers, and downward top probe. |
| 2 | Toggle trial railing support for the nearest non-bypassed environment collider. Saves immediately; try left-clicking normally. |
| 3 | Toggle a local bypass for the nearest environment collider, for an identified invisible player blocker. It is skipped only by railing targeting, not physical collision. |
| 4 | Reload edited trial rules. Invalid files keep the current rules. |

2 and 3 also save a capture before changing the rule. Notifications and the BepInEx
log confirm each action. Tools probe 3 metres to diagnose distant hits; activation
still requires the normal 1.5 m camera ray, configured horizontal reach, and valid
top on the same collider. If trial support still fails, capture again with 1 to
see the remaining failure. A naming override cannot manufacture a usable top.
3 only affects the forward target ray; a blocker obstructing the downward top probe
will still be reported and needs review.

Files live under `BepInEx/config/Cigarette.RailDev/`:

- `capture-<timestamp>.json`: evidence to review/add permanent support.
- `trial-rails.json`: local rail/blocker overrides, persistent across restarts.
- `trial-rails.json.bak`: previous trial rules before the last change.

Each rule targets an exact scene, hierarchy, collider type, mesh name and world
position (5 cm coordinate tolerance). It does not whitelist all copies of a shared
building mesh. Scene changes work without rescanning the world. Captures happen
only on hotkeys; no continuous scene scan or diagnostic log spam.

Once a trial works, send its capture (and a screenshot if the geometry is unclear).
We can promote the reviewed scene/mesh or exact object identity into the main mod's
permanent rules. Developer trial files are not release support and never get packaged.
Removing just the RailDev DLL and restarting removes all local overrides, while
permanent railing support continues to work. Keep captures/rules for the next session.

## Build/install

From the cigarette repository:

```sh
make build-rail-dev GAME_PATH="/path/to/game" DOTNET="/path/to/dotnet"
make test-rail-dev DOTNET="/path/to/dotnet"
make install-rail-dev GAME_PATH="/path/to/game" DOTNET="/path/to/dotnet"
```

Output: `dev/rail-tools/bin/Nivalis.Cigarette.RailDev.dll`.
`install-rail-dev` explicitly installs both main and development DLLs locally.
Normal `make package` stages **only** `Nivalis.Cigarette.dll`, even if RailDev was
built and installed. No version change or publishing is needed for local iteration.

## Promoted captures: Seaside Boardwalk, 2026-10-04

Permanent main-mod support now includes `_Detail_Props/Tech/Tech_Construction (8)`
and `(1)` (exact BoxCollider hierarchy matches), plus the electronic-store stairs
using `Floating_Platform_Shops_2_Stairs_3_1` in `9_Seaside_Boardwalk`.
The three captures passed the existing horizontal reach, same-collider top,
normal and height checks. No new blocker exclusions or reach changes were needed.
Their local trial rules may remain installed but are no longer required.
