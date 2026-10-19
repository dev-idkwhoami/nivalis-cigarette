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

Captures also include `surfaces` diagnostics for objects within 4 m: an all-layer
ray including triggers and player descendants, nearby colliders (including
disabled ones), and nearby visible-mesh objects even when they have no collider.
Each nearby list is capped at 128 entries and includes the uncapped count.
Renderer bounds intersecting the aim ray are listed first; this is a bounding-box
test, not proof that the ray touched the rendered mesh. Collider bounds containing
the eye are recorded because a ray starting inside a collider can miss its surface.
Disabled collider bounds may be empty, so their transform positions are also used
for proximity. These diagnostics run only when capturing and do not alter physics.
If a boat railing produces empty `hits`, press 1 again with this version and inspect
`surfaces` before adding rules: name overrides cannot make missing collision hittable.

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

## Player boat railings

The three reviewed rear-deck rails are now supported by the main Cigarette DLL:
the rear cross rail, the long side rail, and the short opposite-side return.
RailDev is not required. Geometry was measured from the top faces of
`Boat (New)/combined_combine_Boat` and checked against the two 2026-10-19 captures.
Other boat surfaces are not included.

The boxes live in a separate `LocalPhysicsMode.Physics3D` scene and are queried
only by Cigarette. They never collide with the player, boat, objects or water and
never change native layers. Their transforms follow the boat model; resting hand,
rig and exit-animation anchors follow the same translation and rotation. Native
boat colliders still block targeting through cabin walls. Boat physics and save
data remain untouched. The former prototype outlines and toggle hotkeys are removed.

The 1.5 m camera ray and top-height checks still apply. A target up to 30 cm beyond
configured horizontal reach can open the smoking wheel, provided the horizontal
view is within 35 degrees either side of perpendicular to the rail. A near-side
surface probe establishes that heading; a top normal or combined-mesh bounds
cannot determine it. If a reliable side cannot be found, the tooltip stays hidden.

Selecting an item smoothly aligns the camera yaw/body and moves toward a stand-off
point perpendicular to the selected contact. The destination is 88% of configured reach (a 12% proportional buffer),
independent of starting distance. Players too far away move forward; players too
close gently back away to the same destination using native collision/ground checks. The nominal transition is 0.65 seconds; rejected
native movement can catch up for up to 2 seconds before cancellation. Progress
uses actual remaining distance, so rejected tiny steps do not consume the plan.
Remaining placement error within 5% of configured reach needs no further movement,
but the contact must still be within the actual reach limit and yaw alignment must
finish. The movement budget has a proportional floor to tolerate small native
placement corrections on the boat.
The selected contact and heading follow the boat. Rotating away from the original
camera ray during setup is expected and no longer cancels the approach.

Native `PlayerCharacterController.Move` retains ground/collision checks and
boat-local navigation. Movement/Escape, UI interruption, scene/player changes
and obstructed movement cancel setup. No teleport fallback. Native `RotateCamera`
updates the same yaw used by `Rotation`, so camera and animation heading agree.
Both hands must find reachable contacts; missing contacts cancel instead of
leaving a hand floating in its default pose.

For final testing without RailDev, check both captured locations and oblique aim,
resting hands during boat bobbing, a draw to the mouth, cancellation halfway through
an approach, and an ordinary land railing. Capture hotkeys remain opt-in in RailDev.

Unity reference: [isolated 3D physics scenes](https://docs.unity3d.com/2020.3/Documentation/ScriptReference/SceneManagement.LocalPhysicsMode.Physics3D.html).

### Mask/boat timestep correction, 2026-10-19

Boat railing alignment was confirmed in-game. The subsequent mask test reported
bow-down sinking and a later snap back to the water. Inspection of the installed
native binary found `BuoyantBody.FixedUpdate` at `0x180889390` reading
`Time.fixedDeltaTime` into its water job; the job's `Execute` at `0x1830778b0`
multiplies the resulting buoyancy and pull forces by that timestep. The caller
then passes those forces to `Rigidbody.AddForceAtPosition` with mode 0 (`Force`).
Reducing fixedDeltaTime therefore also reduces lift relative to gravity.

`MaskSlowMotion` now preserves the captured physics step and changes only world
time scale. Managed regression checks cover fade-in/out, several speed/step
settings, cancellation, pause and external time ownership. The native analysis
supports this correction, and the user confirmed the mask/boat fix in-game.

The original step scaling preserved the real-time physics cadence during slow
motion. Keeping the native step trades that smoothness for the game's calibrated
force balance: at a 0.02-second step, half speed means 25 physics updates per real
second, and 0.1 speed means 5. Player movement uses the native frame update with
its own scaled delta; mask duration and fades use unscaled frame time. Neither
depends on the former fixed-step adjustment. No additional functional dependency
was found in these paths; strong slow motion can still look less smooth.

## Promoted captures: Seaside Boardwalk, 2026-10-04

Permanent main-mod support now includes `_Detail_Props/Tech/Tech_Construction (8)`
and `(1)` (exact BoxCollider hierarchy matches), plus the electronic-store stairs
using `Floating_Platform_Shops_2_Stairs_3_1` in `9_Seaside_Boardwalk`.
The three captures passed the existing horizontal reach, same-collider top,
normal and height checks. No new blocker exclusions or reach changes were needed.
Their local trial rules may remain installed but are no longer required.
