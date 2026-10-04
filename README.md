# Cigarette

A BepInEx IL2CPP mod for Nivalis Nights. This README covers building, testing
and packaging releases.

## Smoking options (1.1.0)

At an eligible railing, left-click to open the native four-section radial menu:
**Cigarette**, **Joint**, **Inhalant mask**, and **Close**. The hint says
"Choose smoking item". The three items use embedded coloured voxel-art PNG icons
from `assets/icons/`, with native icon tinting disabled. Close retains the native X.
This replaces the hidden Shift-click selection. The native
wheel handles mouse/controller selection and cancellation; choosing an item starts
only after it closes and the player/railing are revalidated.
The joint follows the supplied photo: a slender, gently tapered body, pale rolled
tip with a dark opening, subtle mottled rolling paper and a crimped, twisted end.
The cone’s wide end is another 10% wider, with the same narrow mouthpiece and short shoulder before the thin twist.
The twisted closure burns first; subsequent burning cuts back the wide end and
reduces the ember radius, including on the dropped prop.
Smoking durations are independent in `BepInEx/config/cigarette.cfg`:
`[Smoking] SessionDurationSeconds` controls cigarettes,
`[Cannabis] SessionDurationSeconds` controls cannabis, and
`[Mask] SessionDurationSeconds` controls the inhalant mask. All default to 60 seconds
and accept 20–600 seconds. Movement/Escape lowers the hands. Cigarettes/joints are
dropped; the reusable mask is lowered and put away.

The mask borrows the city `CyberVape` mesh/material without copying NPC scripts,
colliders, or the automatic smoke emitter. Its dedicated cupped grip leaves a gap
between the middle and ring fingers for the thin stem. The grip anchors at the
bowl-to-stem junction, with curled fingers cupping the rounded underside. The resting
palm tilts slightly upward and the hand sits higher to keep the mask clear of the rail.
Resting wrist alignment follows the forearm; axial palm rotation is carried into the
forearm while preserving the solved hand position, including while putting it away. During a draw the face
opening aligns below the camera, with the nose edge intended to remain visible.
The mask uses a fixed cupped grip with per-finger curl reductions: index 15%,
middle 25%, ring 50%, little 55%, and thumb 20% from the original grip. Model placement uses
its established grip anchor before the fingers open, preserving the mask and wrist
positions. Dynamic mesh fitting is disabled; the same pose is used at rest and while
inhaling. The face target stays unchanged. The final visual fit still requires an
in-game check.

The mask uses bundled `mask_inhale.wav` for its 2.3-second inhale/bubbling sound,
then the existing exhale recordings without visible smoke. It does not generate a
cannabis high. `[Mask] InhaleVolume` defaults to 0.125;
exhaling uses `[Audio] ExhaleVolume` (default 0.025).
The recording is embedded in the DLL, just like the cigarette recordings; no external
audio file or file-path setting is needed.
Existing cigarette/joint recordings are unchanged.

Only consumed draws earn high time: a full default blunt earns three minutes
after smoking, and cancelling early earns proportionally less. The timer holds
during blunt smoking, counts down afterwards, and fades over its final 30 seconds.
Additional blunts add time, capped at 30 minutes. Menus/focus loss pause the timer;
changing scene or player clears the effect. Ordinary cigarettes and the mask do not add time.

The mod borrows only the loaded `Female_Singer` frame textures (the lady
with the microphone stand) from the game. The singer is 2.1 metres tall by default,
50% larger than the first prototype. Additive apparitions fade into fixed world positions and
fade out after 9–16 seconds. A shared 6–12 second random timer limits spawning
to two at once. Standing placements are 3–7 metres away; moving placements are
9–18 metres ahead. Ground, slope, support, clearance and line-of-sight checks
reject unsuitable spots. The singer turns upright toward the camera while remaining
anchored to the same ground position; placement reserves her whole turning footprint.
A failed placement is skipped until the next timer.
Ground probes can reach 120 metres below the player, so lower terraces and streets
can host a singer when viewed from a balcony. Clear line of sight is still required;
the singer remains grounded and does not appear through walls or the balcony floor.
If the singer animation is not loaded, it retries the asset lookup every 30 seconds.
No game textures are copied into the mod or distributed.

All effects, including hallucinations, stay off throughout smoking and lowering
the prop. Afterwards camera effects ease in over two seconds and the apparition
spawn timer starts. Starting another smoke hides existing apparitions until it ends.
Camera roll reaches 2.5 degrees left/right with smooth transitions and pauses.
It is applied before the native post-processing render callback and restored
after rendering. RGB separation reconstructs the rendered world image from three
independently offset source channels. It applies to visible object edges throughout
the image, including the centre, before overlay HUD rendering. Each colour channel
contributes exactly once, preserving flat colours instead of applying a tint.
`[Cannabis] RgbShiftMinPixels` and `RgbShiftMaxPixels` default to 5 and 15 pixels
(each accepts 0–32). The horizontal channel offset smoothly moves toward a new random
value every half-second, staying within the range at full high strength. The effect
still fades in/out with the high. Equal limits give a fixed amount; set both to zero
to disable RGB. Reversed limits are sorted. These replace `RgbShiftPixels`. Missing settings are added on startup with these
defaults; existing configured values are preserved when updating.
The previous native chromatic-aberration hook is removed: the live log exposed an
invalid intensity readback, and its radial effect did not match the requested look.
The experimental wave pass is now off by default (`WobbleStrength = 0`). When enabled,
a separate command buffer warps the completed camera image on
a 48-by-32 UV grid, producing moving horizontal/vertical waves. Its two render
textures follow the camera resolution and are released with the effect. Only
image softness uses a five-tap horizontal/vertical blur, cycling clear–soft–clear
over twelve seconds. The former depth-of-field volume is removed so this blur does
not depend on zoom, focus distance or game profiles. `BlurRadiusPixels` controls
the peak sampling radius (default 4, range 0–8). Zoom's movement lock no longer hides
hallucinations or camera effects. Rendering failures are isolated: an image-effect
error cannot switch off the 2.5-degree tilt.
`[Cannabis]` in `BepInEx/config/cigarette.cfg` exposes
`HighDurationMultiplier`, `ApparitionHeightMetres`, `CameraDriftDegrees`,
`WobbleStrength`, `RgbShiftMinPixels`, `RgbShiftMaxPixels`, `BlurRadiusPixels` and `SoftFocus`. `CameraDriftDegrees` now controls roll directly
(default 2.5, maximum 5 degrees). Setting roll/wobble to zero disables each;
`SoftFocus = false` disables softness. Defaults are deliberately subtle.

Build and pure timing checks do not verify native IL2CPP rendering. In-game checks:

- Open the railing wheel and check its four choices, hover labels, Close, Escape,
  and controller selection. The opening click must not activate a choice. Moving
  away/changing player or scene must not start a stale selection.
- Confirm ordinary cigarettes and the white cone joint still work.
- Choose the mask: inspect the middle/ring canister gap, thumb wrap, palm-up rest,
  rail clearance and nose-edge visibility at the face (also while looking/zooming).
  Cancel mid-draw and finish a full session; the mask should lower without being
  dropped, burning, exhaling smoke, or starting a high. Repeat after switching items.
- Check the bundled mask inhale. Exhale audio should play without particles; cancellation
  must stop playback. Cigarette/joint audio should remain unchanged.
- Stop before the first draw, halfway through, and after a full session; compare
  the after-effect duration (zero, partial and about three minutes respectively).
- Stand still, then walk around: apparitions should stay anchored, use free ground,
  animate, turn toward the camera without tilting, and fade. Only the mic-stand singer
  should appear, with no billboard backgrounds.
- Check that singers, blur, head drift and RGB separation start only
  after lowering the blunt. Starting another smoke should immediately clear the camera.
- Hold right-click to zoom: the singer must remain visible. Check that the camera
  leans both ways by 2.5 degrees, with pauses. Look at sharp object edges in the centre
  and corners: they should have small offset RGB outlines without broad colour fog. Check
  for an inverted image or black edges and try a resolution change during the high.
- Stand still without zooming: blur should slowly rise and fall over twelve seconds.
  Verify it continues while zoomed, and that RGB at zero leaves blur/tilt working.
- Open/close a menu and change scene during the high. Check normal camera look,
  hands, input and post-processing afterwards. Tune softness/drift for comfort.
- Check the BepInEx log for `Cannabis apparition sequences` and any unavailable
  shader/filter messages, plus `Cannabis render hook active` reporting RGB separation
  in pixels. `Cannabis image compositor ready` confirms shader-pass selection; the
  shipped shader uses uppercase `LIGHTMODE` / `FORWARDBASE` tags. Session credit, visibility suppression,
  placement failure categories and successful spawn height are logged as well.
  The RGB render path and below-balcony placement still need
  in-game visual verification; a build or hook log alone does not prove its appearance.

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

After putting the mask away, the world slows to 50% speed. Only consumed inhalations
earn time, using the same proportional accounting as the joint: a complete 60-second
session earns 180 seconds at the default multiplier of 3; a quarter earns 45 seconds.
`[Mask] WorldSpeed` accepts 0.1–1 (default 0.5; 1 disables slow motion).
`[Mask] AfterEffectDurationMultiplier` controls earned duration (0–10, default 3).
The effect and its real-time countdown wait during smoking/lowering and menus.
There are 0.4-second fades at the start and end. New mask sessions add to remaining
time, capped at 30 minutes. Scene/player changes and mod cleanup clear the effect.
Scaled world animation and physics slow down; unscaled systems and audio do not.
External time changes, including pause, take priority. Mask inhale volume is 0.125.

The native player update skips movement below time scale 1. During mask-owned
slow motion only, the mod temporarily restores scale 1 around that local player
update, then restores the slow scale even on exceptions. Native movement locks,
input and collisions remain intact. The native movement timestep is multiplied by
the captured world scale, so walking, sprinting and falling slow by the same amount
as the world, including the effect's fades. Pauses and unrelated slow motion are excluded.

## Adding unsupported railings locally

Use the optional, separate `Nivalis.Cigarette.RailDev.dll` to capture unsupported
surfaces and toggle exact-object trial rules without rebuilding. It is never part
of the release ZIP. See [local rail development](docs/rail-development.md) for
1–4 hotkeys, captures, trial overrides, and opt-in build/install commands.
