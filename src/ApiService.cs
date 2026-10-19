using NivalisMods.Cigarette.Api;

namespace NivalisMods.Cigarette;

// No Unity dependencies: the public contract/state machine is testable without a game process.
internal interface IApiRuntime
{
    bool IsReady { get; }
    ApiResult ValidatePosition();
    ApiResult CanStart(SmokingItem item);
    ApiResult TryStart(SmokingItem item);
    ApiResult Stop();
    void AvailabilityChanged(SmokingItem item);
    void RefreshEffects();
    ItemSettings GetSettings(SmokingItem item);
    EffectSettings GetEffectSettings();
    SessionSettings GetSessionSettings();
}

internal sealed class ApiService : ICigaretteApi, IItemsApi, ISessionsApi, IEffectsApi, ISmokingEvents
{
    private readonly IApiRuntime _runtime;
    private readonly Action<Exception> _log;
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private readonly ItemAvailability[] _availability = new ItemAvailability[3];
    private readonly bool[] _automatic = { true, true, true };
    private readonly bool[] _enabled = { true, true, true };
    private readonly float[] _remaining = new float[3], _strength = new float[3];
    private readonly Queue<Action> _notifications = new();
    private SmokingSessionInfo? _current;
    private bool _started, _dispatching;

    internal ApiService(IApiRuntime runtime, Action<Exception> log) { _runtime = runtime; _log = log; }
    public IItemsApi Items => this;
    public ISessionsApi Sessions => this;
    public IEffectsApi Effects => this;
    public ISmokingEvents Events => this;
    public bool IsReady { get { CheckThread(); return _runtime.IsReady; } }
    public event Action<SmokingSessionInfo>? SmokingStarted;
    public event Action<ConsumptionChangedEvent>? ConsumptionChanged;
    public event Action<SmokingEndedEvent>? SmokingEnded;
    public event Action<SmokingStartFailedEvent>? SmokingStartFailed;
    public event Action<ItemAvailabilityChangedEvent>? ItemAvailabilityChanged;

    private void CheckThread()
    {
        if (Environment.CurrentManagedThreadId != _thread)
            throw new InvalidOperationException("Cigarette API calls must run on the Unity main thread.");
    }
    private int Index<T>(T value) where T : struct, Enum
    {
        CheckThread();
        if (!Enum.IsDefined(typeof(T), value)) throw new ArgumentOutOfRangeException(nameof(value));
        return Convert.ToInt32(value);
    }
    public ItemAvailability GetAvailability(SmokingItem item) => _availability[Index(item)];
    public void SetAvailability(SmokingItem item, ItemAvailability availability)
    {
        var i = Index(item);
        Index(availability);
        var previous = _availability[i];
        if (previous == availability) return;
        _availability[i] = availability;
        var change = new ItemAvailabilityChangedEvent(item, previous, availability);
        Queue(() => Emit(ItemAvailabilityChanged, change));
        _runtime.AvailabilityChanged(item);
    }
    internal bool HasVisibleItems => _availability.Any(a => a != ItemAvailability.Hidden);
    public ItemSettings GetSettings(SmokingItem item) { Index(item); return _runtime.GetSettings(item); }
    SessionSettings ISessionsApi.GetSettings() { CheckThread(); return _runtime.GetSessionSettings(); }
    public EffectSettings GetSettings() { CheckThread(); return _runtime.GetEffectSettings(); }
    public SmokingState GetState() { CheckThread(); return _current?.State ?? SmokingState.Idle; }
    public SmokingSessionInfo? GetCurrent() { CheckThread(); return _current; }
    public ApiResult ValidatePosition()
    {
        CheckThread();
        return _runtime.IsReady ? _runtime.ValidatePosition() : new(ResultCode.NotReady, "No gameplay controller/player.");
    }
    public ApiResult CanStart(SmokingItem item)
    {
        if (GetAvailability(item) != ItemAvailability.Available) return new(ResultCode.ItemDisabled, "Smoking item is disabled or hidden.");
        if (!IsReady) return new(ResultCode.NotReady, "No gameplay controller/player.");
        if (_current != null) return new(ResultCode.Busy, "A smoking sequence is already active.");
        return _runtime.CanStart(item);
    }
    public ApiResult TryStart(SmokingItem item)
    {
        var validation = CanStart(item);
        return validation.Success ? _runtime.TryStart(item) : validation;
    }
    public ApiResult Stop()
    {
        CheckThread();
        return _runtime.IsReady ? _runtime.Stop() : new(ResultCode.NotReady, "No gameplay controller/player.");
    }
    public bool IsAutomaticEffectsEnabled(SmokingItem item) => _automatic[Index(item)];
    public void SetAutomaticEffectsEnabled(SmokingItem item, bool enabled) => _automatic[Index(item)] = enabled;
    public bool IsEnabled(EffectType effect) => _enabled[Index(effect)];
    public void SetEnabled(EffectType effect, bool enabled)
    {
        _enabled[Index(effect)] = enabled;
        _runtime.RefreshEffects();
    }
    public EffectStatus GetState(EffectType effect)
    {
        var i = Index(effect);
        return new(_enabled[i], _remaining[i], _strength[i]);
    }
    public ApiResult StartEffect(EffectType effect, float durationSeconds, float strength = 1f)
    {
        var i = Index(effect);
        if (!float.IsFinite(durationSeconds) || durationSeconds <= 0f || durationSeconds > 1800f ||
            !float.IsFinite(strength) || strength < 0f || strength > 1f)
            return new(ResultCode.InvalidArgument, "Duration must be > 0 and <= 1800 seconds; strength must be 0–1.");
        if (!IsReady) return new(ResultCode.NotReady, "No gameplay controller/player.");
        _runtime.RefreshEffects(); // Release stale scene/player context before installing this activation.
        _remaining[i] = durationSeconds;
        _strength[i] = strength;
        _runtime.RefreshEffects();
        return new(ResultCode.Success);
    }
    public void StopEffect(EffectType effect)
    {
        var i = Index(effect);
        _remaining[i] = _strength[i] = 0;
        _runtime.RefreshEffects();
    }
    internal float EffectStrength(EffectType effect, float automaticStrength)
    {
        var i = (int)effect;
        return _enabled[i] ? Math.Max(automaticStrength, _remaining[i] > 0 ? _strength[i] : 0f) : 0f;
    }
    internal bool HasDirectEffect(EffectType effect) => _remaining[(int)effect] > 0;
    internal void TickEffects(float realDelta)
    {
        if (!float.IsFinite(realDelta) || realDelta <= 0) return;
        for (var i = 0; i < _remaining.Length; i++)
        {
            _remaining[i] = Math.Max(0f, _remaining[i] - realDelta);
            if (_remaining[i] == 0) _strength[i] = 0;
        }
    }
    internal void ClearDirectEffects()
    {
        Array.Clear(_remaining, 0, _remaining.Length);
        Array.Clear(_strength, 0, _strength.Length);
    }
    internal Guid Accept(SmokingItem item, float duration)
    {
        if (_current != null) throw new InvalidOperationException("A session is already accepted.");
        _started = false;
        _current = new(Guid.NewGuid(), item, SmokingState.Approaching, duration, 0f);
        return _current.SessionId;
    }
    internal void Started()
    {
        if (_current == null || _started) return;
        _started = true;
        var snapshot = _current = _current with { State = SmokingState.Smoking };
        Queue(() => Emit(SmokingStarted, snapshot));
    }
    internal void Consume(float total)
    {
        if (_current == null || !_started || _current.State != SmokingState.Smoking || !float.IsFinite(total)) return;
        total = Math.Clamp(total, _current.ConsumedFraction, 1f);
        var delta = total - _current.ConsumedFraction;
        if (delta <= 0) return;
        _current = _current with { ConsumedFraction = total };
        var change = new ConsumptionChangedEvent(_current, delta);
        Queue(() => Emit(ConsumptionChanged, change));
    }
    internal void Finishing()
    {
        if (_current != null && _started) _current = _current with { State = SmokingState.Finishing };
    }
    internal void End(SmokingEndReason reason, string message = "", ResultCode failure = ResultCode.ApproachCancelled)
    {
        var snapshot = _current;
        if (snapshot == null) return;
        _current = null;
        if (_started)
        {
            var ended = new SmokingEndedEvent(snapshot, reason, message);
            Queue(() => Emit(SmokingEnded, ended));
        }
        else
        {
            var failed = new SmokingStartFailedEvent(snapshot, reason, new(failure, message, snapshot.SessionId));
            Queue(() => Emit(SmokingStartFailed, failed));
        }
        _started = false;
    }
    private void Queue(Action action) => _notifications.Enqueue(action);
    internal void DispatchEvents()
    {
        if (_dispatching) return;
        _dispatching = true;
        try
        {
            // Reentrant API calls are safe: mutations have completed, and newly queued events wait until next dispatch.
            var count = _notifications.Count;
            while (count-- > 0) _notifications.Dequeue()();
        }
        finally { _dispatching = false; }
    }
    private void Emit<T>(Action<T>? handlers, T data)
    {
        if (handlers == null) return;
        foreach (Action<T> handler in handlers.GetInvocationList())
            try { handler(data); } catch (Exception e) { _log(e); }
    }
}
