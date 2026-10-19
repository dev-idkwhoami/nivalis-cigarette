# Local API integration test mod

Build with `make build-api-test GAME_PATH="..."`. This builds the API-enabled
Cigarette variant and the separate `dev/api-test/bin/Nivalis.Cigarette.ApiTest.dll`.
The test mod depends on the API contract library, not the provider's assembly.
It is never bundled in either player archive.

Install the API-enabled `Nivalis.Cigarette.dll`, `Nivalis.Cigarette.Api.dll`, and
`Nivalis.Cigarette.ApiTest.dll` into `BepInEx/plugins/` while the game is closed.
Replace existing Cigarette DLLs instead of keeping multiple copies. Preserve
`cigarette.cfg`; the test does not edit configuration files.

1. Start the game and load a save. A populated street is preferable: mask and
   apparition assets must already be loaded by the game.
2. Stand at a usable railing with empty hands and aim at it until the smoking
   prompt appears. Start without an existing high or slow-motion effect.
3. Press **F10** once. Keep the game focused and leave movement controls alone.
   At default 60-second durations the run takes about two minutes. It starts
   and stops each item, then lets one cigarette finish naturally. The runner
   captures the initial valid railing pitch and restores that pitch before each
   new start, allowing two frames for the camera to update. This avoids manually
   looking down again after the animation leaves the camera facing straight.
   It does not alter normal Cigarette behavior, move the player, restore yaw,
   bypass position validation, or apply an old pitch after a scene/player change.
   If the target actually moved, manual repositioning may still be needed.
4. Watch camera distortion, apparitions and slow motion in the final part.
   A passing API state assertion does not establish that the visuals look right.
5. **F11** cancels. Completion, cancellation and failures restore the original
   item availability, automatic-effect flags and effect permissions. Test-created
   direct effects are stopped and a test-owned smoking session is asked to stop.

The current run is written and flushed to
`BepInEx/Cigarette.ApiTest/latest.log`. Starting another run archives the previous
log in the same directory. The final `SUMMARY` records outcome, assertions,
failures and skips. Progress also goes to BepInEx's normal log.

Coverage: optional discovery/readiness, all settings getters, item availability
states/events, disabled start rejection, validated approach, approach cancellation,
all three session starts, busy rejection, live consumption and delta totals,
explicit stop and cleanup, natural completion, automatic-effect toggles,
effect permission/activation/suppression/timer/expiry, and slow-motion restoration.
Natural completion is explicitly skipped if cigarette duration exceeds 120 seconds.

The runner deliberately throws from one `SmokingStarted` subscriber to verify
that another subscriber still receives the event. BepInEx errors containing
`EXPECTED Cigarette.ApiTest subscriber isolation probe` are intentional. Other
exceptions, missing assets, timeouts and cleanup errors must be investigated.

Not covered automatically: absent-provider discovery after physically removing
the mod, wheel disabled/hidden appearance (the API states are checked), correctness
of apparition placement or image quality, save/load during smoking, or every
player-driven cancellation case. Review the run log and visuals before claiming
live compatibility. No test runs until F10 is pressed.
