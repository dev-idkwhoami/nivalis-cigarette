using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Nivalis.Cigarette")]
[assembly: InternalsVisibleTo("SessionTests")]

namespace NivalisMods.Cigarette.Api;

/// <summary>Ship this assembly with optional integrations; it has no dependency on the mod or Unity.</summary>
public static class CigaretteApi
{
    public const string PluginId = "local.nivalis.cigarette";
    public const int ContractVersion = 2;
    private static ICigaretteApi? _instance;

    /// <summary>Safe on any thread. False when Cigarette has not registered an implementation.</summary>
    public static bool TryGetInstance([NotNullWhen(true)] out ICigaretteApi? api)
    {
        api = Volatile.Read(ref _instance);
        return api != null;
    }

    internal static void Register(ICigaretteApi instance) => Volatile.Write(ref _instance, instance);
}

public enum SmokingItem { Cigarette, Joint, InhalantMask }
public enum ItemAvailability { Available, Disabled, Hidden }
public enum EffectType { CameraDistortion, Apparitions, InhalantSlowMotion }
public enum SmokingState { Idle, Approaching, Smoking, Finishing }
public enum ResultCode
{
    Success, NotReady, GameplayUnavailable, Busy, ItemDisabled, InvalidPosition,
    PlayerNotReady, AssetsUnavailable, ApproachCancelled, Failed, InvalidArgument
}
public enum SmokingEndReason
{
    Completed, UserCancelled, ApiStopped, AvailabilityChanged, GameplayUnavailable,
    SceneChanged, ControllerDisabled, Failed
}

public sealed record ApiResult(ResultCode Code, string Message = "", Guid? SessionId = null)
{
    public bool Success => Code == ResultCode.Success;
}

/// <summary>Effective settings after the same bounds/defaults used by the smoking controller.</summary>
public sealed record ItemSettings(float SessionDurationSeconds, float AfterEffectDurationMultiplier,
    float InhaleVolume, float ExhaleVolume);

public sealed record SessionSettings(float MaximumRailDistanceMetres, float LookDownDegrees);

public sealed record EffectSettings(float MaskWorldSpeed,
    float ApparitionHeightMetres, float CameraDriftDegrees, float WobbleStrength,
    bool SoftFocus, float BlurRadiusPixels, float RgbShiftMinPixels, float RgbShiftMaxPixels);

/// <summary>Duration is captured at acceptance. ConsumedFraction is monotonic and between zero and one.</summary>
public sealed record SmokingSessionInfo(Guid SessionId, SmokingItem Item, SmokingState State,
    float SessionDurationSeconds, float ConsumedFraction)
{
    public float ConsumedSeconds => ConsumedFraction * SessionDurationSeconds;
}

public sealed record ConsumptionChangedEvent(SmokingSessionInfo Session, float DeltaFraction)
{
    public float DeltaSeconds => DeltaFraction * Session.SessionDurationSeconds;
}
public sealed record SmokingEndedEvent(SmokingSessionInfo Session, SmokingEndReason Reason, string Message);
public sealed record SmokingStartFailedEvent(SmokingSessionInfo Session, SmokingEndReason Reason, ApiResult Failure);
public sealed record ItemAvailabilityChangedEvent(SmokingItem Item, ItemAvailability Previous, ItemAvailability Current);
public sealed record EffectStatus(bool Enabled, float DirectRemainingSeconds, float DirectStrength);

/// <summary>All members except CigaretteApi.TryGetInstance must be used on Unity's main thread.</summary>
public interface ICigaretteApi
{
    bool IsReady { get; }
    IItemsApi Items { get; }
    ISessionsApi Sessions { get; }
    IEffectsApi Effects { get; }
    ISmokingEvents Events { get; }
}
public interface IItemsApi
{
    /// <summary>Immutable snapshot of user-enabled items, before API restrictions.</summary>
    IReadOnlyList<SmokingItem> GetUserEnabledItems();
    /// <summary>Read-only Mod Companion preference. API restrictions do not change it.</summary>
    bool IsEnabledByUser(SmokingItem item);
    /// <summary>Effective availability: user-disabled items are always Hidden.</summary>
    ItemAvailability GetAvailability(SmokingItem item);
    void SetAvailability(SmokingItem item, ItemAvailability availability);
    ItemSettings GetSettings(SmokingItem item);
}
public interface ISessionsApi
{
    SessionSettings GetSettings();
    ApiResult ValidatePosition();
    ApiResult CanStart(SmokingItem item);
    /// <summary>Success means approach accepted; observe SmokingStarted or SmokingStartFailed for the outcome.</summary>
    ApiResult TryStart(SmokingItem item);
    ApiResult Stop();
    SmokingState GetState();
    SmokingSessionInfo? GetCurrent();
}
public interface IEffectsApi
{
    bool IsAutomaticEffectsEnabled(SmokingItem item);
    void SetAutomaticEffectsEnabled(SmokingItem item, bool enabled);
    bool IsEnabled(EffectType effect);
    void SetEnabled(EffectType effect, bool enabled);
    EffectStatus GetState(EffectType effect);
    EffectSettings GetSettings();
    /// <summary>Replace the direct activation for this effect. Duration is 0–1800 seconds, exclusive of zero; strength is 0–1.</summary>
    ApiResult StartEffect(EffectType effect, float durationSeconds, float strength = 1f);
    /// <summary>Stop direct activation. Earned automatic effects are controlled separately.</summary>
    void StopEffect(EffectType effect);
}
public interface ISmokingEvents
{
    event Action<SmokingSessionInfo>? SmokingStarted;
    event Action<ConsumptionChangedEvent>? ConsumptionChanged;
    event Action<SmokingEndedEvent>? SmokingEnded;
    event Action<SmokingStartFailedEvent>? SmokingStartFailed;
    event Action<ItemAvailabilityChangedEvent>? ItemAvailabilityChanged;
}
