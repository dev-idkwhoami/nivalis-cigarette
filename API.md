# Cigarette integration API

Namespace: `NivalisMods.Cigarette.Api`. Contract version: 1.

This reference describes the public contract in [api/CigaretteApi.cs](api/CigaretteApi.cs).

- [Setup and optional discovery](#setup-and-optional-discovery)
- [Items and configuration](#items-and-configuration)
- [Sessions](#sessions)
- [Events and consumption](#events-and-consumption)
- [Effects](#effects)
- [Method reference](#method-reference)
- [Results, snapshots, and settings](#results-snapshots-and-settings)
- [Event reference](#event-reference)
- [Integration recipes](#integration-recipes)
- [Verification](#verification)

## Setup and optional discovery

Players using this integration need `Nivalis.Cigarette-<version>-API.zip`.
The normal `Nivalis.Cigarette-<version>.zip` is standalone and does not register
an API provider. If the API library is present alongside that standalone build,
`TryGetInstance` safely returns false. Install one variant, not both.

Build the provider with `make build-api`, or package it with `make package-api`.
`make package` produces both player archives. Reference
`Nivalis.Cigarette.Api.dll` from `bin/api/`, and ship it at
`BepInEx/plugins/Nivalis.Cigarette.Api.dll`. All integrating mods use that same
shared file and assembly identity. Do not rename/embed a private copy. Cigarette's
own package also includes it. The library depends only on .NET 6, not Unity,
BepInEx, or `Nivalis.Cigarette.dll`.

Declare `[BepInDependency(CigaretteApi.PluginId, BepInDependency.DependencyFlags.SoftDependency)]`
on your BepInEx plugin. This orders loading when Cigarette is installed without
requiring it. The API library itself **must** still be installed.

```csharp
using BepInEx;
using BepInEx.Unity.IL2CPP;
using NivalisMods.Cigarette.Api;

[BepInPlugin("example.survival", "Survival", "1.0.0")]
[BepInDependency(CigaretteApi.PluginId, BepInDependency.DependencyFlags.SoftDependency)]
public sealed class SurvivalPlugin : BasePlugin
{
    public override void Load()
    {
        if (!CigaretteApi.TryGetInstance(out var api))
        {
            Log.LogInfo("Cigarette is absent; smoking integration is inactive.");
            return;
        }
        api.Events.ConsumptionChanged += change =>
            Log.LogInfo($"{change.Session.Item}: +{change.DeltaSeconds:F3}s, " +
                $"{change.Session.ConsumedFraction:P1} consumed, session {change.Session.SessionId}");
    }
}
```

`TryGetInstance` is thread-safe and returns false until Cigarette has registered
its implementation. It does not require a loaded save. `api.IsReady` indicates
an active controller and player/hands; `Sessions.CanStart` additionally checks
current gameplay, position, assets, and player readiness. Retrieve the instance
again if initially unavailable. The provider lives for the process lifetime;
Cigarette does not support plugin hot-unload.

All instance API calls and event subscription changes belong on Unity's main
thread. Game-state operations reject other threads with `InvalidOperationException`. Invalid enum
values throw `ArgumentOutOfRangeException`. Expected gameplay rejections are
returned as `ApiResult` with `Code`, `Message`, and optional `SessionId`.

## Items and configuration

`SmokingItem`: `Cigarette`, `Joint`, `InhalantMask`.

```csharp
var current = api.Items.GetAvailability(SmokingItem.Joint);
api.Items.SetAvailability(SmokingItem.Joint, ItemAvailability.Disabled);
var settings = api.Items.GetSettings(SmokingItem.Joint);
```

Availability is global, in-memory, and last-write-wins:

| State | Wheel | Programmatic start |
| --- | --- | --- |
| `Available` | Visible and usable | Allowed after validation |
| `Disabled` | Visible, inactive | Rejected |
| `Hidden` | Omitted | Rejected |

A change closes an open smoking wheel to discard stale choices. Disabling an
item cancels its pending approach; a session that has already started can finish.
Use `Sessions.Stop()` to stop it explicitly. Hiding every item removes the railing
prompt. No persistent config file is changed by these API controls.

Read-only, immutable settings snapshots:

- `Items.GetSettings(item)`: session duration in seconds, after-effect duration
  multiplier, inhale volume, and exhale volume.
- `Sessions.GetSettings()`: maximum railing distance and downward look limit.
- `Effects.GetSettings()`: inhalant world speed, apparition height, camera roll,
  wobble, soft focus, blur, and RGB offsets.

Snapshots expose effective bounded values/defaults (including sorted RGB limits).
They are not live configuration handles. A session captures its duration when
its approach is accepted. Use the event/session's `SessionDurationSeconds` when
calculating consumption; a later config change must not change that calculation.

## Sessions

```csharp
var position = api.Sessions.ValidatePosition();
var readiness = api.Sessions.CanStart(SmokingItem.Joint);
var result = api.Sessions.TryStart(SmokingItem.Joint);
// result.Success means approach accepted, not that inhalation has begun.
var state = api.Sessions.GetState();
var session = api.Sessions.GetCurrent(); // null when idle
api.Sessions.Stop();
```

`ValidatePosition` checks the current player's aimed-at railing, reach, top
surface, and facing using the normal railing validation. It does not move the
player or reserve a position. It is not a pathfinding guarantee. `CanStart` also
checks item availability, readiness, busy/UI state, idle empty hands, and loaded
assets. Neither validation method starts anything.

`TryStart` rechecks validation, begins the normal collision-aware approach, and
returns a unique session ID. The controller validates the selected contact and
player again after approaching. It never teleports the player or replaces an
active session. A blocked approach is cancelled. Rejection during preflight
returns no session ID and produces no lifecycle events. A setup failure after
allocation can return an ID and queue a failure event; see the method reference.

State sequence: `Idle -> Approaching -> Smoking -> Finishing -> Idle`.
An approach may return directly to idle without ever smoking. `Stop` cancels an
approach immediately, or uses normal lowering/cleanup for an active session.
Stopping while idle or already finishing is harmless. Poll `GetState` or observe
the terminal event to know when cleanup has finished.

## Events and consumption

- `SmokingStarted(SmokingSessionInfo)`: animation/item setup succeeded.
- `ConsumptionChanged(ConsumptionChangedEvent)`: positive consumption since the
  previous notification, plus an immutable cumulative snapshot.
- `SmokingEnded(SmokingEndedEvent)`: one terminal event after cleanup for an
  actually started session, with final consumption and an end reason.
- `SmokingStartFailed(SmokingStartFailedEvent)`: an accepted approach failed or
  was cancelled before smoking began; carries its session ID and failure reason.
- `ItemAvailabilityChanged(ItemAvailabilityChangedEvent)`: previous/new global
  state; setting the same value twice does not produce another event.

Notifications are queued until the controller's update has completed (also
flushed on controller teardown). They are delivered on the main thread in order.
Consumption and terminal events cannot interrupt animation cleanup. Exceptions
in one subscriber are logged and do not stop other subscribers. Event callbacks
may call the API; events created by those calls wait for the next dispatch.
Unsubscribe your handlers when your integration is disposed.

`ConsumedFraction` is 0–1 (0–100%), monotonic within one session. It advances only
during inhalation; raising, holding between draws, approaching and lowering do
not count. `DeltaFraction` reports newly consumed amount. Totals apply to all
three items; for the reusable mask this is a session dose, not destroyed hardware.

```csharp
float equivalentConsumedSeconds = session.ConsumedFraction * session.SessionDurationSeconds;
float equivalentConsumedMilliseconds = equivalentConsumedSeconds * 1000f;
// Convenience properties: session.ConsumedSeconds and change.DeltaSeconds.
```

These are **equivalent session seconds**, not elapsed wall time or literal
seconds spent inhaling. They also exclude the after-effect multiplier. For a
60-second session, 25% means 15 equivalent consumed seconds. At multiplier 3,
that earns 45 seconds of automatic after-effect before existing caps/countdown.

## Effects

`EffectType`: `CameraDistortion` (roll, RGB, blur and optional wobble),
`Apparitions`, and `InhalantSlowMotion`. Slow motion changes world timing and is
explicitly separate from visual effects.

```csharp
api.Effects.SetAutomaticEffectsEnabled(SmokingItem.Joint, false);
bool automatic = api.Effects.IsAutomaticEffectsEnabled(SmokingItem.Joint);
api.Effects.SetEnabled(EffectType.CameraDistortion, false);
bool allowed = api.Effects.IsEnabled(EffectType.CameraDistortion);
api.Effects.SetEnabled(EffectType.CameraDistortion, true);
var result = api.Effects.StartEffect(EffectType.CameraDistortion, 20f, 0.75f);
var effect = api.Effects.GetState(EffectType.CameraDistortion);
api.Effects.StopEffect(EffectType.CameraDistortion);
```

These controls are separate:

- **Automatic effects:** toggles earning future after-effect time from that item.
  Consumption events continue. Already earned time remains; disabled consumption
  is never awarded retroactively when reenabled. Cigarettes have no built-in
  automatic after-effect, so their flag has no visual consequence.
- **Enabled:** globally suppresses/allows a particular effect, both automatic and
  direct. Suppression does not clear its timers. Disabling removes that effect;
  enabling resumes any remaining time subject to normal gameplay visibility.
- **StartEffect/StopEffect:** controls one global direct activation per effect.
  Starting again replaces its duration/strength. Stopping clears only direct
  activation; suppress the effect to hide an automatic contribution as well.

Direct duration must be finite, > 0 and <= 1800 seconds; strength must be finite
and in 0–1. Invalid values return `InvalidArgument`. Direct activations count
unscaled seconds while gameplay is available and no smoking/lowering animation
is active, including while globally suppressed. Automatic timers retain the
mod's existing timing behavior. Direct and automatic strengths combine using
maximum, not addition. Configured visual intensity still applies.

`GetState(effect)` reports permission and the remaining **direct** activation,
not proof that pixels are currently visible. Rendering still respects camera,
UI, smoking, and asset availability. Apparitions require loaded singer assets
and a valid placement. Direct activation success means accepted; verify actual
rendering in the game. Scene/player changes and controller teardown clear effect
timers; global item/permission settings persist for the process.

## Method reference

### Discovery and API groups

```csharp
bool CigaretteApi.TryGetInstance(out ICigaretteApi? api)
```

Returns `true` and the shared instance when the provider has registered. Returns
`false` and `null` otherwise. This method is safe on any thread, including before
loading a save. It cannot protect against a missing **API library**: the caller
must distribute that library even when Cigarette itself is optional.

`CigaretteApi.PluginId` is `"local.nivalis.cigarette"`;
`CigaretteApi.ContractVersion` is `1`. ContractVersion identifies this library's
contract, not the installed smoking mod's version or a runtime compatibility
negotiation result.

| `ICigaretteApi` property | Type | Meaning |
| --- | --- | --- |
| `IsReady` | `bool` | An active smoking controller and player hands exist. This does not guarantee a valid position, an available item, or unobstructed gameplay. |
| `Items` | `IItemsApi` | Global availability and item settings. |
| `Sessions` | `ISessionsApi` | Validation, starting/stopping, and session snapshots. |
| `Effects` | `IEffectsApi` | Effect permission, automatic earning, and direct activations. |
| `Events` | `ISmokingEvents` | Main-thread notifications for all sessions, including wheel-initiated ones. |

### `api.Items`

| Signature | Behavior and return value |
| --- | --- |
| `ItemAvailability GetAvailability(SmokingItem item)` | Reads the current global state. Every item defaults to `Available`. |
| `void SetAvailability(SmokingItem item, ItemAvailability availability)` | Replaces the global state. An actual change queues `ItemAvailabilityChanged`, invalidates the open wheel, and cancels this item's pending approach if disabled/hidden. It does not terminate an already started session. Repeating the current value is a no-op. |
| `ItemSettings GetSettings(SmokingItem item)` | Returns a new read-only snapshot of the effective settings for this item. Does not change config or capture a session. |

These methods can be used before gameplay is ready once an API instance exists.
Invalid enum values throw; there is no custom-item registration API in contract 1.

### `api.Sessions`

| Signature | Behavior and return value |
| --- | --- |
| `SessionSettings GetSettings()` | Returns the effective railing distance and look-down limit. Available without a loaded save. |
| `ApiResult ValidatePosition()` | Checks the local player's current aimed-at railing. Returns `NotReady` without a player/controller, `InvalidPosition` with a diagnostic message for unsuitable geometry/facing, or `Success`. It does not check item availability or reserve the position. |
| `ApiResult CanStart(SmokingItem item)` | Full preflight check without starting. Tests availability, readiness, active sequence/wheel, gameplay, position, player hands, and required assets. The result is a snapshot, not a guarantee that a later call will succeed. |
| `ApiResult TryStart(SmokingItem item)` | Repeats validation and initiates the normal approach. On acceptance returns `Success` with a non-null `SessionId`. Observe events to learn whether setup actually succeeds. No explicit coordinates or bypass-validation option is exposed. |
| `ApiResult Stop()` | Stops the global current sequence, regardless of which mod or the wheel started it. Cancels an approach immediately; active smoking enters normal lowering. Returns `NotReady` without a gameplay controller/player, otherwise normally `Success`, including when idle. It does not clear earned after-effects. |
| `SmokingState GetState()` | Returns `Idle`, `Approaching`, `Smoking`, or `Finishing`. Available without a loaded save. |
| `SmokingSessionInfo? GetCurrent()` | Returns an immutable current snapshot or `null` when idle. Available without a loaded save. Retained snapshots do not update automatically. |

A preflight rejection does not allocate a session or emit lifecycle events. If
setup throws **after** an ID was allocated, `TryStart` can return `Failed` with
that ID and also queue `SmokingStartFailed`. Use `SessionId` to correlate the
result and event rather than treating them as two separate failed attempts.

Calling `Stop` while already finishing does not replace the previously chosen
end reason. An exceptional runtime failure may return `Failed`; inspect the mod
log for details. Do not spin or repeatedly retry inside one frame. The native
wheel's close transition can briefly return `Busy` until a subsequent update.

### `api.Effects`

| Signature | Behavior and return value |
| --- | --- |
| `bool IsAutomaticEffectsEnabled(SmokingItem item)` | Reads whether new consumption can earn built-in after-effect time. Defaults to `true`. |
| `void SetAutomaticEffectsEnabled(SmokingItem item, bool enabled)` | Enables/disables future automatic earning for this item. Does not delete existing earned time, disable consumption events, or affect direct activations. |
| `bool IsEnabled(EffectType effect)` | Reads the global permission for an effect. Defaults to `true`. Permission is not an activity/visibility indicator. |
| `void SetEnabled(EffectType effect, bool enabled)` | Allows or suppresses both automatic and direct contributions. Suppression releases the corresponding visual/time effect without discarding its remaining timer. |
| `EffectStatus GetState(EffectType effect)` | Returns enabled state and remaining duration/strength of direct activation. Does not expose accumulated automatic time or actual rendering status. |
| `EffectSettings GetSettings()` | Returns a read-only snapshot of configured effect intensities, dimensions, and world speed. |
| `ApiResult StartEffect(EffectType effect, float durationSeconds, float strength = 1f)` | Replaces this effect's direct activation. Returns `InvalidArgument` for invalid numeric inputs, `NotReady` without a player/controller, or `Success` after acceptance. Does not start smoking or change global permission. |
| `void StopEffect(EffectType effect)` | Clears this effect's direct activation and refreshes suppression. Harmless if no direct activation exists. Automatic contributions remain. |

Only `StartEffect` requires `IsReady`. Permission, automatic-earning, and settings
methods can be used at the main menu. Direct activation can be accepted while
paused or suppressed; its timer follows the rules in [Effects](#effects).
`StartEffect(..., strength: 0f)` stores a zero-strength activation; it does not
suppress a stronger automatic contribution. Use `SetEnabled(false)` for that.

There are no effect-started, effect-ended, or settings-changed events in this
contract. Poll the appropriate getters when those values are needed.

## Results, snapshots, and settings

### `ApiResult`

| Property | Type | Description |
| --- | --- | --- |
| `Code` | `ResultCode` | Machine-readable outcome. |
| `Success` | `bool` | Exactly `Code == ResultCode.Success`. |
| `Message` | `string` | Human-readable diagnostic; may be empty. Do not parse it as a stable identifier. |
| `SessionId` | `Guid?` | ID of an allocated session where relevant; otherwise `null`. |

| `ResultCode` | Meaning |
| --- | --- |
| `Success` | The requested check or operation succeeded/was accepted. |
| `NotReady` | No suitable player/controller is available. |
| `GameplayUnavailable` | Pause, focus loss, or blocking UI prevents starting. |
| `Busy` | Another sequence or the smoking wheel is active. |
| `ItemDisabled` | The requested item is `Disabled` or `Hidden`. |
| `InvalidPosition` | Railing, distance, surface, or facing validation failed. |
| `PlayerNotReady` | The player is not idle/on foot/empty-handed or another interaction prevents smoking. |
| `AssetsUnavailable` | Required animation or inhalant-mask assets are not loaded. |
| `ApproachCancelled` | An accepted approach was stopped or could not complete. See the failure event's reason/message. |
| `Failed` | An unexpected operation/setup failure occurred. Consult the log. |
| `InvalidArgument` | Invalid direct-effect duration or strength. Invalid enum values throw instead. |

A result reports one blocking condition, not a list of every unmet requirement.

### `SmokingSessionInfo`

| Property | Type | Description |
| --- | --- | --- |
| `SessionId` | `Guid` | Unique across accepted sessions, including attempts that fail before smoking. |
| `Item` | `SmokingItem` | Cigarette, joint, or inhalant mask. |
| `State` | `SmokingState` | State when the snapshot was made. A terminal event retains the last session phase; `GetState()` normally reads `Idle` after cleanup. There is no `Ended` enum value. |
| `SessionDurationSeconds` | `float` | Effective duration captured at acceptance, unaffected by later config changes. |
| `ConsumedFraction` | `float` | Cumulative equivalent consumption, 0–1. |
| `ConsumedSeconds` | `float` | Computed as fraction multiplied by captured duration. |

An event's snapshot is historical: another subscriber may already have stopped
or started a session by the time your handler queries the current global state.
Use the payload's ID and values to process the event itself.

### Configuration snapshots

All numeric fields below are `float`; `SoftFocus` is `bool`. Duration fields use
seconds. Volume and strength values use normalized fractions, not percentages.

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

Non-finite config values fall back to the implementation's defaults. These
snapshots expose the supported integration settings, not an arbitrary accessor
for every internal configuration entry. Editing a snapshot or creating a copy
with `with { ... }` does not change the mod.

`EffectStatus` contains `bool Enabled`, `float DirectRemainingSeconds`, and
`float DirectStrength`. Direct remaining time and strength become zero when the
activation expires or is stopped. An effect can still be visible due to an
automatic contribution even when both direct fields are zero.

## Event reference

Subscribe with named handlers when you need to unsubscribe later:

```csharp
api.Events.ConsumptionChanged += OnConsumptionChanged;
// On integration disposal, on the main thread:
api.Events.ConsumptionChanged -= OnConsumptionChanged;
```

| Event signature | Payload and delivery |
| --- | --- |
| `event Action<SmokingSessionInfo>? SmokingStarted` | A session successfully created its animation and held item. Payload state is `Smoking`. This is not itself evidence of consumption. |
| `event Action<ConsumptionChangedEvent>? ConsumptionChanged` | `Session` is the cumulative snapshot; `DeltaFraction` and computed `DeltaSeconds` describe only newly consumed amount. Notifications may arrive every update during a draw; they are not one-per-puff. |
| `event Action<SmokingEndedEvent>? SmokingEnded` | `Session` contains final totals; `Reason` identifies termination; `Message` is optional diagnostic text. Emitted once for a started session after cleanup. |
| `event Action<SmokingStartFailedEvent>? SmokingStartFailed` | `Session` identifies the accepted attempt; `Reason` describes cancellation/failure; `Failure` is an `ApiResult` with code/message/ID. No `SmokingStarted` or `SmokingEnded` is emitted for that attempt. |
| `event Action<ItemAvailabilityChangedEvent>? ItemAvailabilityChanged` | `Item`, `Previous`, and `Current` describe an actual global state change. The getter may already reflect a later queued change. |

Events are not replayed to subscribers that join after dispatch. Subscribing
mid-session does not give you the consumption that happened earlier. Read
`GetCurrent()` if your integration needs an initial snapshot.

| `SmokingEndReason` | Interpretation |
| --- | --- |
| `Completed` | The normal session duration was reached. |
| `UserCancelled` | Player cancellation; the current approach implementation also uses this reason for a blocked/invalidated approach. Consult `Failure.Message`/`Failure.Code` for detail. |
| `ApiStopped` | `Sessions.Stop()` ended the sequence. |
| `AvailabilityChanged` | The item was disabled/hidden during approach. |
| `GameplayUnavailable` | Gameplay became unavailable and the smoking flow stopped/finished. |
| `SceneChanged` | The active session's scene/player-hands context changed. |
| `ControllerDisabled` | Controller teardown or disabling stopped the sequence. |
| `Failed` | Setup, animation, or another runtime operation failed. |

Reason values are shared by end and start-failure payloads; not every reason is
emitted in every phase. Do not assume every invalidated approach can distinguish
user movement from blocked travel or gameplay changes.

## Integration recipes

### Optional discovery and a validated start

Run this helper on Unity's main thread, for example from your mod's own input
handler. It accepts an `Action<string>` so the caller can connect its logger.

```csharp
static void StartJoint(Action<string> log)
{
    if (!CigaretteApi.TryGetInstance(out var api))
    {
        log("Cigarette is not installed or has not initialized.");
        return;
    }

    // CanStart is useful for button hints. TryStart performs its own checks,
    // so a separate preflight call is unnecessary for this actual action.
    var result = api.Sessions.TryStart(SmokingItem.Joint);
    log(result.Success
        ? $"Approaching railing; session {result.SessionId}. Waiting for SmokingStarted."
        : $"Could not start: {result.Code}: {result.Message}");
}
```

### Inventory-driven availability

```csharp
static void UpdateJointAvailability(ICigaretteApi api, bool unlocked, int quantity)
{
    var desired = !unlocked ? ItemAvailability.Hidden
        : quantity <= 0 ? ItemAvailability.Disabled
        : ItemAvailability.Available;

    if (api.Items.GetAvailability(SmokingItem.Joint) != desired)
        api.Items.SetAvailability(SmokingItem.Joint, desired);
}
```

This applies to the one shared wheel and API start path. Availability is global;
another mod can overwrite it. Do not blindly restore an old value on disposal
unless your integration intends to change the current shared policy.

### Apply a timed buff once on the first actual consumption

This helper is a complete managed integration component. Construct and dispose
it on the main thread. Supply your survival mod's buff implementation as the
callback. It does not impose any particular buff framework.

```csharp
using System;
using System.Collections.Generic;
using NivalisMods.Cigarette.Api;

public sealed class SmokingBuffIntegration : IDisposable
{
    private readonly ICigaretteApi _api;
    private readonly Action<SmokingItem, TimeSpan> _applyBuff;
    private readonly HashSet<Guid> _credited = new();

    public SmokingBuffIntegration(
        ICigaretteApi api,
        Action<SmokingItem, TimeSpan> applyBuff)
    {
        _api = api;
        _applyBuff = applyBuff;
        _api.Events.ConsumptionChanged += OnConsumption;
        _api.Events.SmokingEnded += OnEnded;
    }

    private void OnConsumption(ConsumptionChangedEvent change)
    {
        if (change.DeltaFraction <= 0f || !_credited.Add(change.Session.SessionId))
            return;

        // Example policy: one 30-second buff per consumed session.
        _applyBuff(change.Session.Item, TimeSpan.FromSeconds(30));
    }

    private void OnEnded(SmokingEndedEvent ended)
    {
        _credited.Remove(ended.Session.SessionId);
    }

    public void Dispose()
    {
        _api.Events.ConsumptionChanged -= OnConsumption;
        _api.Events.SmokingEnded -= OnEnded;
        _credited.Clear();
    }
}
```

For proportional benefits, use `change.DeltaSeconds` on each consumption event
instead. Do not repeatedly add the cumulative `Session.ConsumedSeconds`, which
would double-count prior consumption. If using the mod's multiplier, decide
whether to capture it for your integration at `SmokingStarted` or intentionally
read the latest value for every draw. Cigarette only captures session duration;
your buff policy owns any additional snapshot/persistence rules.

### Read current consumption and configured duration

```csharp
var configured = api.Items.GetSettings(SmokingItem.Joint);
log($"New sessions use {configured.SessionDurationSeconds:F1} seconds.");

var current = api.Sessions.GetCurrent();
if (current != null)
{
    log($"{current.Item}: {current.ConsumedFraction * 100f:F1}% consumed.");
    log($"Equivalent consumption: {current.ConsumedSeconds:F3}s " +
        $"({current.ConsumedSeconds * 1000f:F0}ms).");
}
```

Here `log` is an `Action<string>` supplied by your integration. The configured
duration is appropriate for future sessions; the current snapshot supplies the
correct duration for an already accepted session.

### Replace automatic visuals with survival-mod effects

```csharp
// Prevent future joint consumption from earning built-in after-effect time.
api.Effects.SetAutomaticEffectsEnabled(SmokingItem.Joint, false);

// Also hide already-earned contributions, if that is your intended policy.
api.Effects.SetEnabled(EffectType.CameraDistortion, false);
api.Effects.SetEnabled(EffectType.Apparitions, false);

// Later, enable and activate an independent visual for a survival event.
api.Effects.SetEnabled(EffectType.CameraDistortion, true);
var result = api.Effects.StartEffect(
    EffectType.CameraDistortion, durationSeconds: 20f, strength: 0.75f);
if (!result.Success)
    log($"Visual request rejected: {result.Code}: {result.Message}");
```

Reenabling a visual also allows any still-existing automatic contribution. To
stop only the direct activation, call `StopEffect`. To guarantee that the effect
is suppressed regardless of its source, call `SetEnabled(false)`. Slow motion
has its own `EffectType.InhalantSlowMotion` permission and activation state.

## Verification

`make test` checks discovery with no provider, contract assembly isolation,
global availability, readiness/position rejection, accepted versus started
sessions, immutable duration snapshots, partial/deduplicated consumption,
cancellation/completion, effect timers, subscriber failure isolation, event
reentrancy, and main-thread enforcement. It uses a fake gameplay adapter.

`make package` also builds against the game bindings and checks shipped resources.
The API archive contains both DLLs; the standalone archive contains only a
provider-free smoking DLL. Packaging also checks variant-specific assembly
references and API implementation presence, plus exact archived files and bytes.
These managed checks do not establish live
IL2CPP registration, inactive wheel rendering, placement, animation cleanup, or
effect appearance. Those require the subsequent key-triggered integration test
mod and an in-game run with log review and visual confirmation.
