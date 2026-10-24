using NivalisMods.Cigarette;
using NivalisMods.Cigarette.Api;

internal static class ApiChecks
{
    internal static void Run()
    {
        Check(!CigaretteApi.TryGetInstance(out var absent) && absent == null, "Absent implementation must return false.");
        Check(!typeof(CigaretteApi).Assembly.GetReferencedAssemblies().Any(a =>
            a.Name!.StartsWith("Unity") || a.Name.StartsWith("BepInEx") || a.Name == "Nivalis.Cigarette"),
            "API library must load without the game/mod.");
        CheckUserPreferences();
        var runtime = new FakeRuntime();
        var errors = new List<Exception>();
        var service = new ApiService(runtime, errors.Add);
        runtime.Service = service;
        CigaretteApi.Register(service);
        Check(CigaretteApi.TryGetInstance(out var api) && ReferenceEquals(api, service), "Registered API identity.");
        Check(!api!.IsReady && api.Sessions.TryStart(SmokingItem.Joint).Code == ResultCode.NotReady, "Main menu readiness.");
        Check(api.Sessions.ValidatePosition().Code == ResultCode.NotReady, "Main menu position validation.");
        Check(api.Effects.StartEffect(EffectType.Apparitions, 10).Code == ResultCode.NotReady, "No direct effects before gameplay.");
        runtime.IsReady = true;
        var changes = new List<ItemAvailabilityChangedEvent>();
        api.Events.ItemAvailabilityChanged += changes.Add;
        foreach (var item in Enum.GetValues<SmokingItem>())
        {
            Check(api.Items.GetAvailability(item) == ItemAvailability.Available, "Default availability.");
            foreach (var state in new[] { ItemAvailability.Disabled, ItemAvailability.Hidden })
            {
                api.Items.SetAvailability(item, state);
                Check(api.Items.GetAvailability(item) == state, "Global availability getter.");
                Check(api.Sessions.TryStart(item).Code == ResultCode.ItemDisabled, "Disabled/hidden blocks API start.");
            }
            api.Items.SetAvailability(item, ItemAvailability.Available);
            api.Items.SetAvailability(item, ItemAvailability.Available);
        }
        service.DispatchEvents();
        Check(changes.Count == 9, "Availability emits once per actual change.");
        Throws<ArgumentOutOfRangeException>(() => api.Items.SetAvailability(SmokingItem.Joint, (ItemAvailability)42));
        Throws<ArgumentOutOfRangeException>(() => api.Items.GetAvailability((SmokingItem)42));
        foreach (var item in Enum.GetValues<SmokingItem>()) api.Items.SetAvailability(item, ItemAvailability.Hidden);
        Check(!service.HasVisibleItems, "All hidden removes the prompt/wheel entry opportunity.");
        api.Items.SetAvailability(SmokingItem.Joint, ItemAvailability.Disabled);
        Check(service.HasVisibleItems, "Disabled items remain visible.");
        api.Items.SetAvailability(SmokingItem.Joint, ItemAvailability.Available);
        runtime.Validation = new(ResultCode.InvalidPosition, "No railing.");
        Check(!api.Sessions.TryStart(SmokingItem.Joint).Success && runtime.StartCount == 0, "Rejected positions cannot start.");
        runtime.Validation = new(ResultCode.Success);
        var starts = new List<SmokingSessionInfo>();
        var consumption = new List<ConsumptionChangedEvent>();
        var ends = new List<SmokingEndedEvent>();
        var failures = new List<SmokingStartFailedEvent>();
        api.Events.SmokingStarted += _ => throw new InvalidOperationException("Expected subscriber failure.");
        api.Events.SmokingStarted += starts.Add;
        api.Events.ConsumptionChanged += consumption.Add;
        api.Events.SmokingEnded += ends.Add;
        api.Events.SmokingStartFailed += failures.Add;
        var accepted = api.Sessions.TryStart(SmokingItem.Joint);
        Check(accepted.Success && accepted.SessionId.HasValue && starts.Count == 0, "Accepted does not mean started.");
        Check(api.Sessions.GetState() == SmokingState.Approaching, "Approach state.");
        Check(api.Sessions.TryStart(SmokingItem.Joint).Code == ResultCode.Busy, "Reject overlapping sessions.");
        service.Consume(0.25f);
        Check(api.Sessions.GetCurrent()!.ConsumedFraction == 0, "No consumption during approach.");
        runtime.Duration = 120;
        Check(api.Items.GetSettings(SmokingItem.Joint).SessionDurationSeconds == 120 &&
            api.Sessions.GetCurrent()!.SessionDurationSeconds == 60, "Session duration is an acceptance-time snapshot.");
        service.Started(); service.Started();
        service.Consume(0.25f); service.Consume(0.25f); service.Consume(0.1f); service.Consume(float.NaN);
        service.Consume(0.5f);
        Check(api.Sessions.GetCurrent()!.ConsumedSeconds == 30, "Consumption uses fraction times captured seconds.");
        var heldSnapshot = api.Sessions.GetCurrent();
        service.Finishing(); service.Consume(1f);
        Check(api.Sessions.GetCurrent()!.ConsumedFraction == 0.5f, "No consumption during lowering.");
        service.End(SmokingEndReason.ApiStopped); service.End(SmokingEndReason.ApiStopped);
        service.DispatchEvents();
        Check(starts.Count == 1 && errors.Count == 1, "Faulty subscriber cannot break other listeners or duplicate start.");
        Check(consumption.Count == 2 && consumption.Sum(c => c.DeltaFraction) == 0.5f &&
            consumption.Sum(c => c.DeltaSeconds) == 30, "Monotonic, nonduplicated consumption deltas.");
        Check(ends.Count == 1 && ends[0].Session.SessionId == accepted.SessionId &&
            ends[0].Session.ConsumedFraction == 0.5f && ends[0].Reason == SmokingEndReason.ApiStopped, "Final totals and single terminal event.");
        Check(heldSnapshot!.State == SmokingState.Smoking && api.Sessions.GetState() == SmokingState.Idle, "Snapshots stay immutable.");
        accepted = api.Sessions.TryStart(SmokingItem.Joint);
        api.Items.SetAvailability(SmokingItem.Joint, ItemAvailability.Disabled);
        service.DispatchEvents();
        Check(failures.Count == 1 && failures[0].Session.SessionId == accepted.SessionId &&
            failures[0].Reason == SmokingEndReason.AvailabilityChanged && ends.Count == 1,
            "Disabling during approach cancels without a fake smoking/end event.");
        api.Items.SetAvailability(SmokingItem.Joint, ItemAvailability.Available);
        api.Sessions.TryStart(SmokingItem.Joint); service.Started();
        api.Items.SetAvailability(SmokingItem.Joint, ItemAvailability.Hidden);
        Check(api.Sessions.GetState() == SmokingState.Smoking, "Disabling leaves an actual session running.");
        service.Consume(10f); Check(api.Sessions.GetCurrent()!.ConsumedFraction == 1f, "Clamp consumption to 100 percent.");
        service.End(SmokingEndReason.Completed); service.DispatchEvents();
        Check(ends[^1].Reason == SmokingEndReason.Completed, "Natural completion reason.");
        foreach (var effect in Enum.GetValues<EffectType>())
        {
            Check(api.Effects.IsEnabled(effect), "Effects enabled initially.");
            Check(api.Effects.StartEffect(effect, 10, 0.7f).Success, "Direct effect activation.");
            service.TickEffects(2);
            Check(api.Effects.GetState(effect).DirectRemainingSeconds == 8, "Direct effect uses real seconds.");
            api.Effects.SetEnabled(effect, false);
            Check(service.EffectStrength(effect, 1f) == 0 && api.Effects.GetState(effect).DirectRemainingSeconds == 8,
                "Suppress both automatic/direct effects without discarding time.");
            service.TickEffects(2); api.Effects.SetEnabled(effect, true);
            Check(service.EffectStrength(effect, 0.2f) == 0.7f && api.Effects.GetState(effect).DirectRemainingSeconds == 6,
                "Reenable resumes remaining activation and takes maximum strength.");
            api.Effects.StartEffect(effect, 3, 0.3f);
            Check(api.Effects.GetState(effect).DirectRemainingSeconds == 3, "New direct activation replaces old one.");
            api.Effects.StopEffect(effect);
            Check(!service.HasDirectEffect(effect) && service.EffectStrength(effect, 0.5f) == 0.5f,
                "Stopping direct effect preserves automatic contribution.");
            api.Effects.StartEffect(effect, 1); service.TickEffects(2);
            Check(api.Effects.GetState(effect).DirectRemainingSeconds == 0 && api.Effects.GetState(effect).DirectStrength == 0,
                "Expiry clears direct state.");
        }
        Check(api.Effects.StartEffect(EffectType.Apparitions, float.NaN).Code == ResultCode.InvalidArgument, "Reject invalid duration.");
        Check(api.Effects.StartEffect(EffectType.Apparitions, 0).Code == ResultCode.InvalidArgument, "Reject zero duration.");
        Check(api.Effects.StartEffect(EffectType.Apparitions, 1801).Code == ResultCode.InvalidArgument, "Enforce duration cap.");
        Check(api.Effects.StartEffect(EffectType.Apparitions, 1, float.PositiveInfinity).Code == ResultCode.InvalidArgument, "Reject invalid strength.");
        api.Effects.StartEffect(EffectType.Apparitions, 10);
        service.TickEffects(float.NaN); service.TickEffects(-1);
        Check(api.Effects.GetState(EffectType.Apparitions).DirectRemainingSeconds == 10, "Ignore invalid clock deltas.");
        service.ClearDirectEffects(); Check(!service.HasDirectEffect(EffectType.Apparitions), "Scene cleanup clears direct effects.");
        api.Effects.SetAutomaticEffectsEnabled(SmokingItem.Joint, false);
        Check(!api.Effects.IsAutomaticEffectsEnabled(SmokingItem.Joint), "Automatic effects toggle getter.");
        var earned = new HighSession(); earned.BeginSmoke();
        earned.Credit(0.25f, 60f, api.Effects.IsAutomaticEffectsEnabled(SmokingItem.Joint) ? 3f : 0f);
        api.Effects.SetAutomaticEffectsEnabled(SmokingItem.Joint, true);
        earned.Credit(0.5f, 60f, api.Effects.IsAutomaticEffectsEnabled(SmokingItem.Joint) ? 3f : 0f);
        Check(earned.Remaining == 45f, "Disabled consumption cannot earn retroactive automatic effects.");
        // Event-triggered mutations queue followup events instead of recursing into a half-finished transition.
        var reentrant = 0;
        api.Events.ItemAvailabilityChanged += e =>
        {
            reentrant++;
            if (e.Current == ItemAvailability.Disabled) api.Items.SetAvailability(e.Item, ItemAvailability.Hidden);
        };
        service.DispatchEvents();
        api.Items.SetAvailability(SmokingItem.Joint, ItemAvailability.Disabled);
        service.DispatchEvents();
        Check(reentrant == 1 && api.Items.GetAvailability(SmokingItem.Joint) == ItemAvailability.Hidden, "Reentrant mutation completes safely.");
        service.DispatchEvents(); Check(reentrant == 2, "Followup event dispatched on next pass.");
        Exception? threadError = null;
        var thread = new Thread(() =>
        {
            Check(CigaretteApi.TryGetInstance(out _), "Discovery allowed off main thread.");
            try { api.Sessions.CanStart(SmokingItem.Joint); } catch (Exception e) { threadError = e; }
        });
        thread.Start(); thread.Join();
        Check(threadError is InvalidOperationException, "Game API rejects off-main-thread access.");
        Console.WriteLine("PASS: optional API discovery, global availability, validation, lifecycle, consumption, effects, exception isolation, reentrancy and thread contract.");
    }
    private static void CheckUserPreferences()
    {
        var runtime = new FakeRuntime { IsReady = true };
        var service = new ApiService(runtime, _ => { });
        runtime.Service = service;
        var snapshot = service.GetUserEnabledItems();
        Check(snapshot.Count == 3, "All user preferences default on.");
        var changes = new List<ItemAvailabilityChangedEvent>();
        service.ItemAvailabilityChanged += changes.Add;
        foreach (var item in Enum.GetValues<SmokingItem>())
        {
            service.SetUserEnabled(item, false);
            foreach (var restriction in Enum.GetValues<ItemAvailability>())
            {
                service.SetAvailability(item, restriction);
                Check(!service.IsEnabledByUser(item) && service.GetAvailability(item) == ItemAvailability.Hidden,
                    "API cannot override a disabled user preference.");
                Check(service.TryStart(item).Code == ResultCode.ItemDisabled, "User-disabled blocks start.");
            }
            service.SetAvailability(item, ItemAvailability.Disabled);
            service.SetUserEnabled(item, true);
            Check(service.GetAvailability(item) == ItemAvailability.Disabled, "Reenabling preserves API restriction.");
            service.SetUserEnabled(item, false);
        }
        service.DispatchEvents();
        Check(changes.Count == 9, "Only effective availability changes emit events.");
        Check(service.GetUserEnabledItems().Count == 0 && snapshot.Count == 3, "Preference snapshots stay unchanged.");
        Throws<NotSupportedException>(() => ((IList<SmokingItem>)snapshot)[0] = SmokingItem.Joint);
        Throws<ArgumentOutOfRangeException>(() => service.IsEnabledByUser((SmokingItem)42));
        service.SetAvailability(SmokingItem.Joint, ItemAvailability.Available);
        service.SetUserEnabled(SmokingItem.Joint, true);
        service.TryStart(SmokingItem.Joint);
        service.SetUserEnabled(SmokingItem.Joint, false);
        Check(service.GetState() == SmokingState.Idle, "User disable cancels pending approach.");
        service.SetUserEnabled(SmokingItem.Joint, true);
        service.TryStart(SmokingItem.Joint); service.Started();
        service.SetUserEnabled(SmokingItem.Joint, false);
        Check(service.GetState() == SmokingState.Smoking, "User disable allows active session to finish.");
    }

    private static void Check(bool value, string description) { if (!value) throw new Exception(description); }
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name);
    }
    private sealed class FakeRuntime : IApiRuntime
    {
        internal ApiService Service = null!;
        public bool IsReady { get; set; }
        internal ApiResult Validation = new(ResultCode.Success);
        internal float Duration = 60;
        internal int StartCount;
        public ApiResult ValidatePosition() => Validation;
        public ApiResult CanStart(SmokingItem item) => Validation;
        public ApiResult TryStart(SmokingItem item) { StartCount++; return new(ResultCode.Success, SessionId: Service.Accept(item, Duration)); }
        public ApiResult Stop() { Service.End(SmokingEndReason.ApiStopped); return new(ResultCode.Success); }
        public void AvailabilityChanged(SmokingItem item)
        {
            if (Service.GetCurrent() is { State: SmokingState.Approaching } current && current.Item == item &&
                Service.GetAvailability(item) != ItemAvailability.Available)
                Service.End(SmokingEndReason.AvailabilityChanged, "Disabled.", ResultCode.ItemDisabled);
        }
        public void RefreshEffects() { }
        public ItemSettings GetSettings(SmokingItem item) => new(Duration, 3f, 0.01f, 0.025f);
        public SessionSettings GetSessionSettings() => new(0.45f, 25f);
        public EffectSettings GetEffectSettings() => new(0.5f, 2.1f, 2.5f, 0f, true, 4f, 5f, 15f);
    }
}
