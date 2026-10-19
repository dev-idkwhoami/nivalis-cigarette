using Nivalis;
using NivalisMods.Cigarette.Api;

namespace NivalisMods.Cigarette;

internal sealed class GameApiRuntime : IApiRuntime
{
    public bool IsReady => SmokingController.Instance != null && SmokingController.Instance.isActiveAndEnabled &&
        PlayerManager._instance?.LocalPlayer?.Character?.Controller?.HandsAnimator != null;
    public ApiResult ValidatePosition() => Guard(() => SmokingController.Instance!.ValidatePosition());
    public ApiResult CanStart(SmokingItem item) => Guard(() => SmokingController.Instance!.ValidateStart((SmokeKind)item));
    public ApiResult TryStart(SmokingItem item) => Guard(() => SmokingController.Instance!.TryStart((SmokeKind)item));
    public ApiResult Stop() => Guard(() => SmokingController.Instance!.StopFromApi());
    public void AvailabilityChanged(SmokingItem item) => SmokingController.Instance?.AvailabilityChanged((SmokeKind)item);
    public void RefreshEffects() => SmokingController.Instance?.RefreshEffects();
    public ItemSettings GetSettings(SmokingItem item)
    {
        var duration = item switch
        {
            SmokingItem.Joint => Plugin.CannabisSessionDuration.Value,
            SmokingItem.InhalantMask => Plugin.MaskSessionDuration.Value,
            _ => Plugin.SessionDuration.Value
        };
        var multiplier = item switch
        {
            SmokingItem.Joint => Plugin.HighDurationMultiplier.Value,
            SmokingItem.InhalantMask => Plugin.MaskDurationMultiplier.Value,
            _ => 0f
        };
        return new(Clamp(duration, 60f, 20f, 600f), Clamp(multiplier, 3f, 0f, 10f),
            Clamp(item == SmokingItem.InhalantMask ? Plugin.MaskInhaleVolume.Value : Plugin.InhaleVolume.Value,
                item == SmokingItem.InhalantMask ? Plugin.DefaultMaskInhaleVolume : Plugin.DefaultInhaleVolume, 0f, 1f),
            Clamp(Plugin.ExhaleVolume.Value, Plugin.DefaultExhaleVolume, 0f, 1f));
    }
    public SessionSettings GetSessionSettings() => new(RailingTarget.MaximumDistance,
        Clamp(Plugin.LookDownLimit.Value, 25f, 0f, 45f));
    public EffectSettings GetEffectSettings()
    {
        var minimum = Clamp(Plugin.RgbShiftMinPixels.Value, 5f, 0f, 32f);
        var maximum = Clamp(Plugin.RgbShiftMaxPixels.Value, 15f, 0f, 32f);
        return new(
        Clamp(Plugin.MaskWorldSpeed.Value, 0.5f, 0.1f, 1f),
        Clamp(Plugin.ApparitionHeight.Value, 2.1f, 0.5f, 3.75f),
        Clamp(Plugin.CameraDriftDegrees.Value, 2.5f, 0f, 5f),
        Clamp(Plugin.WobbleStrength.Value, 0f, 0f, 10f), Plugin.SoftFocus.Value,
        Clamp(Plugin.BlurRadiusPixels.Value, 4f, 0f, 8f),
        Math.Min(minimum, maximum), Math.Max(minimum, maximum));
    }
    private static ApiResult Guard(Func<ApiResult> operation)
    {
        try { return operation(); }
        catch (Exception e)
        {
            Plugin.Logger.LogError("Cigarette API operation failed: " + e);
            return new(ResultCode.Failed, "Game state changed or an operation failed; see the Cigarette log.");
        }
    }
    private static float Clamp(float value, float fallback, float min, float max) =>
        float.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
}
