# Cigarette integration API

Namespace: `NivalisMods.Cigarette.Api` · Contract version: **2** · [Public contract](api/CigaretteApi.cs)

## Setup and discovery

Players need the **API-enabled archive** (`-API.zip`). The standalone build does not provide an API.

Reference `Nivalis.Cigarette.Api.dll` from `bin/api/` and distribute it at `BepInEx/plugins/Nivalis.Cigarette.Api.dll`. Use one shared copy; do not rename or embed it. The library targets .NET 6 and has no Unity, BepInEx or main-mod dependency. Build it with `make build-api`.

Declare a soft dependency on your plugin:

```csharp
[BepInDependency(CigaretteApi.PluginId, BepInDependency.DependencyFlags.SoftDependency)]
```

Then discover the provider:

```csharp
using NivalisMods.Cigarette.Api;

if (!CigaretteApi.TryGetInstance(out var api))
    return; // Cigarette is absent, standalone, or not initialized yet.
```

**The API DLL must still be installed**, even when Cigarette itself is optional. `TryGetInstance` cannot protect against a missing referenced assembly.

- `TryGetInstance` is thread-safe; retry later if initialization is pending.
- All instance calls and event subscriptions belong on Unity’s main thread.
- `api.IsReady` means a controller and player hands exist, not that smoking can start.
- Expected rejections return `ApiResult`; invalid enums throw `ArgumentOutOfRangeException`, and game-state calls from other threads throw `InvalidOperationException`.
- The provider lives for the process lifetime; plugin hot-unload is unsupported.

`CigaretteApi.PluginId` is `"local.nivalis.cigarette"`. `ContractVersion` identifies the API contract, not the installed mod version.

## Items

`SmokingItem`: `Cigarette`, `Joint`, `InhalantMask`.

| `api.Items` method | Result / behavior |
| --- | --- |
| `GetUserEnabledItems()` | Immutable snapshot of user-enabled items, before API restrictions. |
| `IsEnabledByUser(item)` | Read-only user preference for one item. |
| `GetAvailability(item)` | Effective `ItemAvailability`: user-disabled items always return `Hidden`. |
| `SetAvailability(item, availability)` | Sets the in-memory API restriction; cannot enable user-disabled items. |
| `GetSettings(item)` | Returns an immutable `ItemSettings` snapshot. |

| Availability | Wheel | API start |
| --- | --- | --- |
| `Available` | Visible and usable | Allowed after validation |
| `Disabled` | Visible, inactive | Rejected |
| `Hidden` | Omitted | Rejected |

Changes close the open wheel. Disabling/hiding cancels a pending approach but lets an already-started session finish; use `Sessions.Stop()` to stop it. With no effectively available items, the railing prompt and hotkey selection are suppressed. Exactly one available item starts directly; two or more open the wheel.

User preferences always take precedence. There is no public setter for them. API restrictions are **global, in-memory and last-write-wins between mods**. They never edit config. Reenabling a user preference reveals the most recent API restriction. Availability events report effective changes only. Preference queries describe the user settings even when an API restriction hides or disables an item.

Contract 2 adds the two read-only preference queries. Ship the updated API DLL with integrations that use them. Existing methods remain available. Custom items are not supported.

```csharp
// Example inventory policy; unlocked and quantity come from your mod.
api.Items.SetAvailability(SmokingItem.Joint,
    !unlocked ? ItemAvailability.Hidden :
    quantity <= 0 ? ItemAvailability.Disabled : ItemAvailability.Available);
```

## Sessions

| `api.Sessions` method | Result / behavior |
| --- | --- |
| `GetSettings()` | Returns `SessionSettings`: effective reach and look-down limit. |
| `ValidatePosition()` | Returns `ApiResult` for the aimed-at railing, surface, reach and facing. No movement or reservation. |
| `CanStart(item)` | Returns full preflight `ApiResult`, including availability, gameplay, busy state, hands and assets. |
| `TryStart(item)` | Revalidates and starts the normal approach. Acceptance returns `Success` and a `SessionId`. |
| `Stop()` | Returns `ApiResult`. Cancels the global approach or lowers the active item, whoever started it. Harmless when idle/finishing; earned effects remain. |
| `GetState()` | Returns `Idle`, `Approaching`, `Smoking` or `Finishing`. |
| `GetCurrent()` | Returns an immutable `SmokingSessionInfo`, or `null` when idle. |

Normal flow: `Idle → Approaching → Smoking → Finishing → Idle`.

**Accepted is not started.** Wait for `SmokingStarted` or `SmokingStartFailed`. Validation is a snapshot, not a guarantee: the normal approach still checks collision, alignment and reach. No coordinate or validation-bypass option is exposed.

Preflight rejection has no session ID or lifecycle events. A setup failure after allocation can return `Failed` with an ID and also emit `SmokingStartFailed`; correlate by ID. `Stop()` preserves an existing finishing reason. Poll state or wait for the terminal event to confirm cleanup.

```csharp
var result = api.Sessions.TryStart(SmokingItem.Joint);
// TryStart already performs CanStart checks.
Log.LogInfo(result.Success
    ? $"Approach accepted: {result.SessionId}"
    : $"Cannot start: {result.Code}: {result.Message}");
```

Settings/state getters work without a loaded save. Validation, start and stop can return `NotReady`. The wheel’s close transition may briefly return `Busy`; retry on a later update, not in a tight loop.

## Events and consumption

Subscribe through `api.Events`. Events include sessions started from the wheel.

| Event | Payload / meaning |
| --- | --- |
| `SmokingStarted` | `SmokingSessionInfo`: animation and held item are ready; consumption may still be zero. |
| `ConsumptionChanged` | `ConsumptionChangedEvent`: cumulative `Session`, plus `DeltaFraction` and `DeltaSeconds`. May fire each update during inhalation, not once per puff. |
| `SmokingEnded` | `SmokingEndedEvent`: final `Session`, `Reason`, `Message`. Once per started session, after cleanup. |
| `SmokingStartFailed` | `SmokingStartFailedEvent`: accepted `Session`, `Reason`, `Failure` (`ApiResult`). That attempt emits neither started nor ended. |
| `ItemAvailabilityChanged` | `ItemAvailabilityChangedEvent`: `Item`, `Previous`, `Current`. Only actual changes emit an event. |

Events are queued, delivered in order on the main thread after controller updates, and flushed on teardown. Subscriber exceptions are logged without blocking other handlers. Callbacks may use the API; newly queued events wait for the next dispatch. Use payload snapshots and IDs because current global state may already differ.

Unsubscribe on disposal. Events are not replayed; use `GetCurrent()` when joining mid-session.

### Consumption units

`ConsumedFraction` is **0–1 (0–100%)**, increasing only during inhalation. For the reusable mask it represents a session dose. Approach, pauses between draws and lowering do not count.

```csharp
float seconds = session.ConsumedFraction * session.SessionDurationSeconds;
float milliseconds = seconds * 1000f;
// Same total: session.ConsumedSeconds
// Newly consumed amount: change.DeltaSeconds
```

These are **equivalent session seconds**, not elapsed time or literal inhalation time. Duration is captured when the session is accepted; later config changes do not alter it. For a 60-second session, 25% means 15 consumed seconds; multiplier 3 earns 45 after-effect seconds before caps/countdown.

Use deltas for incremental benefits, not the cumulative total on every event:

```csharp
void OnConsumption(ConsumptionChangedEvent change)
{
    // Replace with your survival mod's own buff implementation.
    AddBuffTime(change.Session.Item, change.DeltaSeconds * 3f);
}

api.Events.ConsumptionChanged += OnConsumption;
// When disposing the integration, on the main thread:
api.Events.ConsumptionChanged -= OnConsumption;
```

For a once-per-session buff, track the first positive delta by `SessionId` and remove that ID on `SmokingEnded`.

### End reasons

| `SmokingEndReason` | Meaning |
| --- | --- |
| `Completed` | Normal duration reached. |
| `UserCancelled` | Player cancelled, or the approach was blocked/invalidated. |
| `ApiStopped` | `Sessions.Stop()` was called. |
| `AvailabilityChanged` | Item disabled/hidden during approach. |
| `GameplayUnavailable` | Gameplay became unavailable. |
| `SceneChanged` | Scene/player-hands context changed. |
| `ControllerDisabled` | Controller disabled or torn down. |
| `Failed` | Runtime/setup failure. |

Reasons are shared by ended/start-failed events. Not every phase distinguishes blocked travel from user cancellation; inspect the failure code and message.

## Effects

`EffectType`: `CameraDistortion` (roll, RGB, blur, optional wobble), `Apparitions`, `InhalantSlowMotion` (world timing).

| `api.Effects` method | Result / behavior |
| --- | --- |
| `IsAutomaticEffectsEnabled(item)` | Returns whether consumption earns automatic effect time; defaults to `true`. |
| `SetAutomaticEffectsEnabled(item, enabled)` | Changes future earning. Existing time and consumption events remain; no retroactive credit. Cigarettes have no automatic effect. |
| `IsEnabled(effect)` | Returns global permission; defaults to `true`. |
| `SetEnabled(effect, enabled)` | Allows/suppresses both automatic and direct effects without clearing timers. |
| `GetState(effect)` | Returns `EffectStatus`: `Enabled`, `DirectRemainingSeconds`, `DirectStrength`. |
| `GetSettings()` | Returns an immutable `EffectSettings` snapshot. |
| `StartEffect(effect, durationSeconds, strength = 1f)` | Returns `ApiResult`; replaces the single global direct activation. Does not start smoking or enable a suppressed effect. |
| `StopEffect(effect)` | Clears only direct activation; automatic effects remain. Harmless if already stopped. |

Direct duration must be finite and **greater than 0 and at most 1800 seconds**; strength must be finite and **0–1**. Invalid values return `InvalidArgument`. Only `StartEffect` requires a ready player/controller; the other effect methods can be used at the main menu.

Direct timers count unscaled seconds during available gameplay, excluding smoking/lowering—even when suppressed. Direct and automatic strengths combine by maximum. Zero direct strength does not suppress automatic effects.

Status reports direct activation, **not visible rendering or automatic time**. Effects still depend on gameplay, camera and assets; apparitions also need valid placement. Scene/player changes and teardown clear timers. Global permissions and availability persist for the process. There are no effect/settings-change events; poll getters as needed.

```csharp
// Replace built-in joint effects with your integration's policy.
api.Effects.SetAutomaticEffectsEnabled(SmokingItem.Joint, false);
api.Effects.SetEnabled(EffectType.Apparitions, false);
api.Effects.SetEnabled(EffectType.CameraDistortion, false);

// Later, trigger a direct visual effect.
api.Effects.SetEnabled(EffectType.CameraDistortion, true);
var result = api.Effects.StartEffect(EffectType.CameraDistortion, 20f, 0.75f);

// Clear direct activation, or suppress all contributions:
api.Effects.StopEffect(EffectType.CameraDistortion);
api.Effects.SetEnabled(EffectType.CameraDistortion, false);
```

Reenabling an effect also permits any remaining automatic contribution.

## Results and snapshots

`ApiResult` contains `Code`, `Message`, nullable `SessionId`, and `Success` (`Code == Success`). A result reports one blocking condition. Messages are diagnostics, not stable identifiers.

| `ResultCode` | Meaning |
| --- | --- |
| `Success` | Check passed or operation accepted. |
| `NotReady` | Player/controller unavailable. |
| `GameplayUnavailable` | Pause, lost focus or blocking UI. |
| `Busy` | Another sequence or wheel is active. |
| `ItemDisabled` | Item disabled or hidden. |
| `InvalidPosition` | Invalid railing, reach, surface or facing. |
| `PlayerNotReady` | Hands, movement or another interaction blocks smoking. |
| `AssetsUnavailable` | Required assets are not loaded. |
| `ApproachCancelled` | Accepted approach could not complete. |
| `Failed` | Unexpected failure; inspect the log. |
| `InvalidArgument` | Invalid direct-effect duration/strength. |

`SmokingSessionInfo` contains `SessionId`, `Item`, `State`, `SessionDurationSeconds`, `ConsumedFraction` and computed `ConsumedSeconds`. Snapshots never update in place. Terminal payloads retain their last phase; there is no `Ended` state, and the current API state normally reads `Idle` after cleanup.

### Configuration

Settings snapshots expose effective bounded values/defaults. They are read-only: changing a record copy does not update the mod. Use item settings for future sessions and the session snapshot’s captured duration for current consumption.

All numeric fields are `float`; `SoftFocus` is `bool`. Durations use seconds, and volume/strength use normalized fractions.

| Snapshot / field | Effective range | Meaning |
| --- | --- | --- |
| `ItemSettings.SessionDurationSeconds` | 20–600 | Nominal full-session duration. Default 60. |
| `ItemSettings.AfterEffectDurationMultiplier` | 0–10 | Automatic after-effect seconds per equivalent consumed second. Cigarettes return 0; joint/mask default to 3. |
| `ItemSettings.InhaleVolume` | 0–1 | Item-specific inhale volume. |
| `ItemSettings.ExhaleVolume` | 0–1 | Shared exhale volume. |
| `SessionSettings.MaximumRailDistanceMetres` | 0.2–0.65 | Final activation reach. Preflight can allow the existing short approach allowance beyond this. |
| `SessionSettings.LookDownDegrees` | 0–45 | Downward camera limit while smoking. |
| `EffectSettings.MaskWorldSpeed` | 0.1–1 | Configured inhalant world-speed multiplier at full strength; 1 means no slowdown. |
| `EffectSettings.ApparitionHeightMetres` | 0.5–3.75 | Apparition height. |
| `EffectSettings.CameraDriftDegrees` | 0–5 | Maximum camera roll amplitude. |
| `EffectSettings.WobbleStrength` | 0–10 | Optional full-screen wave intensity. |
| `EffectSettings.SoftFocus` | true/false | Enables the blur cycle. |
| `EffectSettings.BlurRadiusPixels` | 0–8 | Peak configured blur radius. |
| `EffectSettings.RgbShiftMinPixels` | 0–32 | Lower RGB offset bound, sorted against the upper bound. |
| `EffectSettings.RgbShiftMaxPixels` | 0–32 | Upper RGB offset bound. |

Non-finite config values use defaults. These snapshots cover integration settings, not every internal config entry.

## Testing

`make test` checks the contract and state machine with a fake gameplay adapter. `make package` additionally checks both variants and archive contents. For in-game checks, see the [optional API test mod](dev/api-test/README.md); managed tests alone do not verify Unity rendering or interactions.
