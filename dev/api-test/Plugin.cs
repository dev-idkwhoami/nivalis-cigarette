using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Attributes;
using NivalisMods.Cigarette.Api;
using Nivalis;
using UnityEngine.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;

namespace NivalisMods.Cigarette.ApiTest;

[BepInPlugin("local.nivalis.cigarette.apitest", "Cigarette API Test", "1.0.0")]
[BepInDependency(CigaretteApi.PluginId, BepInDependency.DependencyFlags.SoftDependency)]
public sealed class Plugin : BasePlugin
{
    internal static ManualLogSource Logger = null!;
    internal static string LogPath = null!;
    private ApiTestRunner? _runner;
    public override void Load()
    {
        Logger = Log;
        LogPath = Path.Combine(Paths.BepInExRootPath, "Cigarette.ApiTest", "latest.log");
        _runner = AddComponent<ApiTestRunner>();
        Log.LogInfo("API test ready: aim at a valid railing with empty hands; F10 runs, F11 cancels. Log: " + LogPath);
    }
    public override bool Unload()
    {
        if (_runner != null) UnityEngine.Object.Destroy(_runner);
        return true;
    }
}

// Test client references only the API library, never Cigarette's implementation.
public sealed class ApiTestRunner : MonoBehaviour
{
    private IEnumerator<bool>? _run;
    private ICigaretteApi? _api;
    private StreamWriter? _log;
    private readonly Dictionary<SmokingItem, ItemAvailability> _availability = new();
    private readonly Dictionary<SmokingItem, bool> _automatic = new();
    private readonly Dictionary<EffectType, bool> _enabled = new();
    private readonly Dictionary<Guid, SmokingSessionInfo> _started = new();
    private readonly Dictionary<Guid, SmokingEndedEvent> _ended = new();
    private readonly Dictionary<Guid, SmokingStartFailedEvent> _failed = new();
    private readonly Dictionary<Guid, float> _consumed = new(), _deltas = new();
    private readonly HashSet<Guid> _owned = new();
    private int _passes, _failures, _changes, _throwingCalls, _skips;
    private float _began;
    private bool _subscribed;
    private float _railingPitch;
    private int _aimPlayer, _aimScene;
    private bool _aimCaptured;

    public ApiTestRunner(IntPtr pointer) : base(pointer) { }

    public void Update()
    {
        try
        {
            var keyboard = Keyboard.current;
            if (keyboard?.f11Key.wasPressedThisFrame == true && _run != null)
            { Finish("CANCELLED by F11"); return; }
            if (keyboard?.f10Key.wasPressedThisFrame == true && _run == null) Begin();
            var run = _run;
            if (run != null && !run.MoveNext()) Finish("COMPLETED");
        }
        catch (Exception e)
        {
            Trace("FAIL", e.ToString());
            _failures++;
            Finish("FAILED / BLOCKED; see preceding diagnostic");
        }
    }

    [HideFromIl2Cpp]
    private void Begin()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Plugin.LogPath)!);
        if (File.Exists(Plugin.LogPath))
            File.Move(Plugin.LogPath, Path.Combine(Path.GetDirectoryName(Plugin.LogPath)!,
                $"previous-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.log"));
        _log = new StreamWriter(Plugin.LogPath, false) { AutoFlush = true };
        _began = Time.unscaledTime;
        _passes = _failures = _changes = _throwingCalls = _skips = 0;
        _aimCaptured = false;
        _availability.Clear(); _automatic.Clear(); _enabled.Clear();
        _started.Clear(); _ended.Clear(); _failed.Clear(); _consumed.Clear(); _deltas.Clear(); _owned.Clear();
        Trace("BEGIN", $"API test 1.0.0; contract {CigaretteApi.ContractVersion}; Unity {Application.unityVersion}");
        Check(CigaretteApi.TryGetInstance(out _api), "TryGetInstance returns an API provider");
        Check(_api!.IsReady, "IsReady: load a save before running");
        Check(_api.Sessions.GetState() == SmokingState.Idle && _api.Sessions.GetCurrent() == null,
            "Session is idle before test");
        var position = _api.Sessions.ValidatePosition();
        Trace("PREFLIGHT", position.ToString());
        Check(position.Success, "Valid railing position: aim at a nearby usable railing with empty hands");
        CaptureRailingPitch();
        foreach (var effect in Enum.GetValues<EffectType>())
            Check(_api.Effects.GetState(effect).DirectRemainingSeconds == 0, "No pre-existing direct activation: " + effect);
        Check(Math.Abs(Time.timeScale - 1f) < 0.001f, "Normal world speed before test; wait for any existing slow motion to finish");
        foreach (var item in Enum.GetValues<SmokingItem>())
        {
            _availability[item] = _api.Items.GetAvailability(item);
            _automatic[item] = _api.Effects.IsAutomaticEffectsEnabled(item);
        }
        foreach (var effect in Enum.GetValues<EffectType>()) _enabled[effect] = _api.Effects.IsEnabled(effect);
        _api.Events.SmokingStarted += ThrowingSubscriber;
        _api.Events.SmokingStarted += OnStarted;
        _api.Events.ConsumptionChanged += OnConsumed;
        _api.Events.SmokingEnded += OnEnded;
        _api.Events.SmokingStartFailed += OnFailed;
        _api.Events.ItemAvailabilityChanged += OnAvailability;
        _subscribed = true;
        _run = Exercise().GetEnumerator();
        Trace("INSTRUCTION", "Keep the game focused. Do not move while smoking. If prompted to re-aim, point at the railing again. F11 cancels.");
    }

    [HideFromIl2Cpp]
    private IEnumerable<bool> Exercise()
    {
        var api = _api!;
        Trace("SETTINGS", api.Sessions.GetSettings().ToString());
        Trace("SETTINGS", api.Effects.GetSettings().ToString());
        foreach (var item in Enum.GetValues<SmokingItem>())
        {
            var settings = api.Items.GetSettings(item);
            Trace("SETTINGS", item + ": " + settings);
            Check(settings.SessionDurationSeconds >= 20 && settings.SessionDurationSeconds <= 600, "Duration is bounded seconds: " + item);
            api.Effects.SetAutomaticEffectsEnabled(item, false);
            Check(!api.Effects.IsAutomaticEffectsEnabled(item), "Disable automatic effects: " + item);
            api.Effects.SetAutomaticEffectsEnabled(item, true);
            Check(api.Effects.IsAutomaticEffectsEnabled(item), "Enable automatic effects: " + item);
            api.Effects.SetAutomaticEffectsEnabled(item, false); // Do not leave earned after-effects behind from test smoking.
            var before = _changes;
            foreach (var state in new[] { ItemAvailability.Available, ItemAvailability.Disabled, ItemAvailability.Hidden })
            {
                api.Items.SetAvailability(item, state);
                Check(api.Items.GetAvailability(item) == state, $"Availability readback: {item} = {state}");
                if (state != ItemAvailability.Available)
                {
                    Check(api.Sessions.CanStart(item).Code == ResultCode.ItemDisabled, "CanStart rejects " + state);
                    Check(api.Sessions.TryStart(item).Code == ResultCode.ItemDisabled, "TryStart rejects " + state);
                }
                yield return true;
                yield return true;
            }
            Check(_changes - before >= 2, "Availability events delivered: " + item);
            api.Items.SetAvailability(item, ItemAvailability.Available);
        }
        Check(api.Effects.StartEffect(EffectType.CameraDistortion, float.NaN).Code == ResultCode.InvalidArgument,
            "StartEffect rejects invalid duration");
        Check(api.Effects.StartEffect(EffectType.CameraDistortion, 5f, 2f).Code == ResultCode.InvalidArgument,
            "StartEffect rejects invalid strength");
        yield return true;

        // Cancel an accepted approach by hiding its item before it can start.
        foreach (var wait in AwaitReady(SmokingItem.Cigarette)) yield return wait;
        var cancelled = Start(SmokingItem.Cigarette);
        api.Items.SetAvailability(SmokingItem.Cigarette, ItemAvailability.Hidden);
        foreach (var wait in Await(() => _failed.ContainsKey(cancelled), 5f, "cancelled approach event")) yield return wait;
        Check(!_started.ContainsKey(cancelled) && !_ended.ContainsKey(cancelled), "Cancelled approach never emits started/ended");
        Check(_failed[cancelled].Reason == SmokingEndReason.AvailabilityChanged, "Approach cancellation reason");
        api.Items.SetAvailability(SmokingItem.Cigarette, ItemAvailability.Available);
        yield return true;

        foreach (var item in Enum.GetValues<SmokingItem>())
        {
            foreach (var wait in AwaitReady(item)) yield return wait;
            var duration = api.Items.GetSettings(item).SessionDurationSeconds;
            var id = Start(item);
            Check(api.Sessions.GetState() == SmokingState.Approaching, "Accepted session is approaching: " + item);
            Check(api.Sessions.TryStart(item).Code == ResultCode.Busy, "Overlapping session rejected");
            foreach (var wait in Await(() => _started.ContainsKey(id) || _failed.ContainsKey(id), 12f, "SmokingStarted: " + item)) yield return wait;
            Check(!_failed.ContainsKey(id), "Session starts successfully: " + item);
            Check(_started[id].SessionDurationSeconds == duration, "Duration snapshot matches settings: " + item);
            api.Items.SetAvailability(item, ItemAvailability.Disabled);
            Check(api.Sessions.GetState() == SmokingState.Smoking, "Disabling does not terminate active smoking");
            api.Items.SetAvailability(item, ItemAvailability.Available);
            foreach (var wait in Await(() => _consumed.GetValueOrDefault(id) > 0f || _ended.ContainsKey(id), 18f, "first consumption: " + item)) yield return wait;
            Check(_consumed.GetValueOrDefault(id) > 0f, "Consumption observed: " + item);
            var current = api.Sessions.GetCurrent()!;
            Check(current.SessionId == id && current.ConsumedFraction > 0 && current.ConsumedFraction <= 1,
                "Current session identity and consumption");
            Check(Math.Abs(current.ConsumedSeconds - current.ConsumedFraction * duration) < 0.001f,
                "Fraction converts to equivalent consumed seconds");
            var stop = api.Sessions.Stop(); Trace("CALL Stop", stop.ToString());
            Check(stop.Success, "Stop accepted");
            foreach (var wait in Await(() => _ended.ContainsKey(id), 12f, "SmokingEnded: " + item)) yield return wait;
            Check(_ended[id].Reason == SmokingEndReason.ApiStopped, "Explicit stop reason: " + item);
            Check(Math.Abs(_deltas.GetValueOrDefault(id) - _ended[id].Session.ConsumedFraction) < 0.0001f,
                "Consumption deltas match final total: " + item);
            Check(api.Sessions.GetState() == SmokingState.Idle && api.Sessions.GetCurrent() == null, "Cleanup returned to idle");
            foreach (var wait in Delay(1f)) yield return wait;
        }
        Check(_throwingCalls >= 3 && _started.Count >= 3, "Throwing subscriber did not block normal event listeners");
        Check(api.Sessions.Stop().Success, "Stop while idle is harmless");

        // A full cigarette validates natural completion without leaving an automatic high.
        var completeDuration = api.Items.GetSettings(SmokingItem.Cigarette).SessionDurationSeconds;
        if (completeDuration <= 120f)
        {
            foreach (var wait in AwaitReady(SmokingItem.Cigarette)) yield return wait;
            var id = Start(SmokingItem.Cigarette);
            Trace("INSTRUCTION", $"Natural-completion test: leave controls alone for approximately {completeDuration:F0} seconds.");
            foreach (var wait in Await(() => _ended.ContainsKey(id) || _failed.ContainsKey(id), completeDuration + 20f, "natural completion")) yield return wait;
            Check(_ended.ContainsKey(id) && _ended[id].Reason == SmokingEndReason.Completed, "Natural completion reason");
            Check(_ended[id].Session.ConsumedFraction >= 0.999f, "Natural completion consumed 100 percent");
        }
        else
        {
            _skips++;
            Trace("SKIP", "Natural completion exceeds 120 seconds; all three partial-session tests still run.");
        }

        foreach (var effect in Enum.GetValues<EffectType>())
        {
            api.Effects.SetEnabled(effect, true);
            Check(api.Effects.IsEnabled(effect), "Enable effect: " + effect);
            var baseline = Time.timeScale;
            var result = api.Effects.StartEffect(effect, 20f, 1f);
            Trace("CALL StartEffect", effect + ": " + result);
            Check(result.Success, "Direct effect accepted: " + effect);
            Check(api.Effects.GetState(effect).DirectRemainingSeconds > 0, "Direct timer exists: " + effect);
            Trace("VISUAL_REVIEW", $"Observe {effect} now. Rendering/placement appearance requires your confirmation.");
            foreach (var wait in Delay(effect == EffectType.Apparitions ? 12f : 4f)) yield return wait;
            if (effect == EffectType.InhalantSlowMotion && api.Effects.GetSettings().MaskWorldSpeed < 0.999f)
                Check(Time.timeScale < baseline - 0.001f, "Inhalant slow motion changes world speed");
            var remaining = api.Effects.GetState(effect).DirectRemainingSeconds;
            Check(remaining > 0 && remaining < 20f, "Direct effect timer counts down: " + effect);
            api.Effects.SetEnabled(effect, false);
            Check(!api.Effects.IsEnabled(effect), "Suppress effect: " + effect);
            Check(api.Effects.GetState(effect).DirectRemainingSeconds > 0, "Suppression retains timer");
            if (effect == EffectType.InhalantSlowMotion)
                Check(Math.Abs(Time.timeScale - baseline) < 0.001f, "Suppression restores world speed");
            foreach (var wait in Delay(1f)) yield return wait;
            Check(api.Effects.GetState(effect).DirectRemainingSeconds < remaining, "Timer advances while suppressed");
            api.Effects.SetEnabled(effect, true);
            foreach (var wait in Delay(1f)) yield return wait;
            api.Effects.StopEffect(effect);
            Check(api.Effects.GetState(effect).DirectRemainingSeconds == 0, "StopEffect clears direct timer: " + effect);
            if (effect == EffectType.InhalantSlowMotion)
                Check(Math.Abs(Time.timeScale - baseline) < 0.001f, "StopEffect restores world speed");
        }
        Check(api.Effects.StartEffect(EffectType.CameraDistortion, 0.5f).Success, "Short effect accepted");
        foreach (var wait in Await(() => api.Effects.GetState(EffectType.CameraDistortion).DirectRemainingSeconds == 0,
            5f, "direct effect expiry")) yield return wait;
        Check(api.Effects.GetState(EffectType.CameraDistortion).DirectStrength == 0, "Expiry clears strength");
    }

    [HideFromIl2Cpp]
    private Guid Start(SmokingItem item)
    {
        var result = _api!.Sessions.TryStart(item);
        Trace("CALL TryStart", item + ": " + result);
        if (result.SessionId is Guid allocated) _owned.Add(allocated);
        Check(result.Success && result.SessionId.HasValue, "Start accepted: " + item);
        return result.SessionId!.Value;
    }

    [HideFromIl2Cpp]
    private void CaptureRailingPitch()
    {
        var character = PlayerManager._instance?.LocalPlayer?.Character;
        var pov = character?.Controller?.FirstPersonPOV;
        if (character == null || pov == null) throw new InvalidOperationException("Cannot capture test railing pitch: no player camera.");
        _railingPitch = pov.m_VerticalAxis.Value;
        if (!float.IsFinite(_railingPitch)) throw new InvalidOperationException("Invalid player camera pitch.");
        _aimPlayer = character.GetInstanceID();
        _aimScene = SceneManager.GetActiveScene().handle;
        _aimCaptured = true;
        Trace("TEST CAMERA", $"Captured railing pitch {_railingPitch:F3} degrees for this test run.");
    }

    [HideFromIl2Cpp]
    private void RestoreRailingPitch()
    {
        if (!_aimCaptured || _api == null || _api.Sessions.GetState() != SmokingState.Idle)
            throw new InvalidOperationException("Test pitch restoration requires an idle session and a captured railing aim.");
        var character = PlayerManager._instance?.LocalPlayer?.Character;
        var pov = character?.Controller?.FirstPersonPOV;
        if (character == null || pov == null || character.GetInstanceID() != _aimPlayer ||
            SceneManager.GetActiveScene().handle != _aimScene)
            throw new InvalidOperationException("Player/scene changed during test; rerun F10 at a railing to capture a new aim.");
        var vertical = pov.m_VerticalAxis;
        var before = vertical.Value;
        vertical.Value = Mathf.Clamp(_railingPitch, vertical.m_MinValue, vertical.m_MaxValue);
        pov.m_VerticalAxis = vertical;
        Trace("TEST CAMERA", $"Restored railing pitch {before:F3} -> {vertical.Value:F3} degrees; normal position validation follows.");
    }

    [HideFromIl2Cpp]
    private IEnumerable<bool> AwaitReady(SmokingItem item)
    {
        // The camera transform follows Cinemachine's axis on a later frame.
        // Restore only between sessions, never fight the player's view during smoking.
        RestoreRailingPitch();
        yield return true;
        yield return true;
        var deadline = Time.unscaledTime + 45f;
        var reported = false;
        while (true)
        {
            var result = _api!.Sessions.CanStart(item);
            if (result.Success) { Trace("CALL CanStart", item + ": Success"); yield break; }
            if (!reported)
            {
                Trace("WAIT", $"Re-aim at the railing with empty hands if necessary. {item}: {result}");
                reported = true;
            }
            if (result.Code == ResultCode.AssetsUnavailable || Time.unscaledTime > deadline)
                throw new InvalidOperationException($"Cannot test {item}: {result}. Move to a populated street/valid rail and rerun F10.");
            yield return true;
        }
    }

    [HideFromIl2Cpp]
    private IEnumerable<bool> Await(Func<bool> predicate, float seconds, string description)
    {
        var deadline = Time.unscaledTime + seconds;
        while (!predicate())
        {
            if (Time.unscaledTime > deadline) throw new TimeoutException(description);
            yield return true;
        }
    }

    [HideFromIl2Cpp]
    private IEnumerable<bool> Delay(float seconds)
    {
        var until = Time.unscaledTime + seconds;
        while (Time.unscaledTime < until) yield return true;
    }

    [HideFromIl2Cpp]
    private void ThrowingSubscriber(SmokingSessionInfo session)
    {
        _throwingCalls++;
        Trace("EXPECTED", "Intentional subscriber exception for isolation check: " + session.SessionId);
        throw new InvalidOperationException("EXPECTED Cigarette.ApiTest subscriber isolation probe");
    }

    [HideFromIl2Cpp]
    private void OnStarted(SmokingSessionInfo value)
    {
        if (!_started.TryAdd(value.SessionId, value)) FailEvent("Duplicate SmokingStarted: " + value.SessionId);
        Trace("EVENT SmokingStarted", value.ToString());
    }

    [HideFromIl2Cpp]
    private void OnConsumed(ConsumptionChangedEvent value)
    {
        var id = value.Session.SessionId;
        var previous = _consumed.GetValueOrDefault(id);
        if (!_started.ContainsKey(id) || _ended.ContainsKey(id) || value.DeltaFraction <= 0 ||
            !float.IsFinite(value.Session.ConsumedFraction) || value.Session.ConsumedFraction < previous ||
            value.Session.ConsumedFraction > 1 || Math.Abs(value.Session.ConsumedFraction - previous - value.DeltaFraction) > 0.0001f)
            FailEvent("Invalid consumption ordering/amount: " + value);
        _consumed[id] = value.Session.ConsumedFraction;
        _deltas[id] = _deltas.GetValueOrDefault(id) + value.DeltaFraction;
        Trace("EVENT ConsumptionChanged", value.ToString(), false);
    }

    [HideFromIl2Cpp]
    private void OnEnded(SmokingEndedEvent value)
    {
        if (!_started.ContainsKey(value.Session.SessionId) || !_ended.TryAdd(value.Session.SessionId, value))
            FailEvent("Invalid/duplicate SmokingEnded: " + value);
        Trace("EVENT SmokingEnded", value.ToString());
    }

    [HideFromIl2Cpp]
    private void OnFailed(SmokingStartFailedEvent value)
    {
        if (!_failed.TryAdd(value.Session.SessionId, value) || _started.ContainsKey(value.Session.SessionId))
            FailEvent("Invalid/duplicate SmokingStartFailed: " + value);
        Trace("EVENT SmokingStartFailed", value.ToString());
    }

    [HideFromIl2Cpp]
    private void OnAvailability(ItemAvailabilityChangedEvent value)
    {
        _changes++;
        Trace("EVENT ItemAvailabilityChanged", value.ToString());
    }

    [HideFromIl2Cpp]
    private void FailEvent(string message) { _failures++; Trace("FAIL", message); }

    [HideFromIl2Cpp]
    private void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
        _passes++;
        Trace("PASS", description);
    }

    [HideFromIl2Cpp]
    private void Trace(string kind, string message, bool console = true)
    {
        var line = $"{DateTime.UtcNow:O} +{Time.unscaledTime - _began:F3}s [{kind}] {message}";
        _log?.WriteLine(line);
        if (console) Plugin.Logger.LogInfo(line);
    }

    [HideFromIl2Cpp]
    private void Finish(string outcome)
    {
        _run?.Dispose(); _run = null;
        if (_api != null && _subscribed)
        {
            var failuresBeforeCleanup = _failures;
            Cleanup(() =>
            {
                var active = _api.Sessions.GetCurrent();
                if (active != null && _owned.Contains(active.SessionId)) _api.Sessions.Stop();
            });
            foreach (var effect in _enabled.Keys) Cleanup(() => _api.Effects.StopEffect(effect));
            foreach (var pair in _enabled) Cleanup(() => _api.Effects.SetEnabled(pair.Key, pair.Value));
            foreach (var pair in _automatic) Cleanup(() => _api.Effects.SetAutomaticEffectsEnabled(pair.Key, pair.Value));
            foreach (var pair in _availability) Cleanup(() => _api.Items.SetAvailability(pair.Key, pair.Value));
            _api.Events.SmokingStarted -= ThrowingSubscriber;
            _api.Events.SmokingStarted -= OnStarted;
            _api.Events.ConsumptionChanged -= OnConsumed;
            _api.Events.SmokingEnded -= OnEnded;
            _api.Events.SmokingStartFailed -= OnFailed;
            _api.Events.ItemAvailabilityChanged -= OnAvailability;
            _subscribed = false;
            Trace("CLEANUP", _failures == failuresBeforeCleanup
                ? "Restored original item availability, effect permissions and automatic-effect flags. No config file was edited."
                : "Restoration had failures; inspect CLEANUP FAILURE entries.");
        }
        if (_log != null)
        {
            Trace("SUMMARY", $"{outcome}; assertions passed={_passes}; failures={_failures}; skipped={_skips}. VISUAL REVIEW STILL REQUIRED. Log: {Plugin.LogPath}");
            _log.Dispose(); _log = null;
        }
        _api = null;
        _aimCaptured = false;
    }

    [HideFromIl2Cpp]
    private void Cleanup(Action action)
    {
        try { action(); } catch (Exception e) { _failures++; Trace("CLEANUP FAILURE", e.ToString()); }
    }

    public void OnDisable() => Finish("CANCELLED: test component disabled");
    public void OnDestroy() => Finish("CANCELLED: test component destroyed");
}
