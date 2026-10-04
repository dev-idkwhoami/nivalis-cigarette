using Il2CppInterop.Runtime.Attributes;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;
using HarmonyLib;

namespace NivalisMods.Cigarette;

// Render-only offsets never write the controller's look angles or accumulate across frames.
public sealed class HighCameraEffect : MonoBehaviour
{
    internal float Strength;
    internal static HighCameraEffect? Active;
    private Quaternion _rotation;
    private bool _offsetApplied, _tiltFailed, _warpFailed, _imageFailed;
    private ScreenWarp? _warp;
    private bool _reportedRender;
    private ScreenRgbShift? _rgb;

    public HighCameraEffect(IntPtr pointer) : base(pointer) { }
    public void OnEnable() => Active = this;

    public void LateUpdate()
    {
        Active = this;
        if (Strength <= 0f) { _warp?.Prepare(0f); _rgb?.Prepare(0f); }
    }

    [HideFromIl2Cpp]
    internal void BeginRender()
    {
        RestoreRotation();
        if (Strength <= 0f) { _warp?.Prepare(0f); _rgb?.Prepare(0f); return; }
        var camera = GetComponent<Camera>();
        if (!_tiltFailed)
        {
            try
            {
                var amount = CannabisEffects.Finite(Plugin.CameraDriftDegrees.Value, 2.5f, 0f, 5f) * Strength;
                _rotation = transform.localRotation;
                transform.localRotation = _rotation * Quaternion.Euler(0f, 0f, HighMotion.Roll(Time.time) * amount);
                _offsetApplied = true;
            }
            catch (Exception e)
            {
                RestoreRotation(); _tiltFailed = true;
                Plugin.Logger.LogWarning("Cannabis tilt stopped: " + e);
            }
        }
        if (!_warpFailed)
        {
            try
            {
                var wobble = CannabisEffects.Finite(Plugin.WobbleStrength.Value, 0f, 0f, 10f) * Strength;
                if (wobble > 0f) _warp ??= new ScreenWarp(camera);
                _warp?.Prepare(wobble);
            }
            catch (Exception e)
            {
                _warp?.Dispose(); _warp = null; _warpFailed = true;
                Plugin.Logger.LogWarning("Cannabis waves stopped; other effects remain active: " + e);
            }
        }
        if (!_imageFailed)
        {
            try
            {
                var pixels = RgbOffsets.AnimatedAmount(Time.time, Plugin.RgbShiftMinPixels.Value, Plugin.RgbShiftMaxPixels.Value) * Strength;
                var blur = Plugin.SoftFocus.Value ? HighMotion.Blur(Time.time) * Strength *
                    CannabisEffects.Finite(Plugin.BlurRadiusPixels.Value, 4f, 0f, 8f) : 0f;
                if (pixels > 0f || blur > 0f) _rgb ??= new ScreenRgbShift(camera);
                _rgb?.Prepare(pixels, blur);
            }
            catch (Exception e)
            {
                _rgb?.Dispose(); _rgb = null; _imageFailed = true;
                Plugin.Logger.LogWarning("Cannabis image effects stopped; tilt remains active: " + e);
            }
        }
        if (!_reportedRender)
        {
            Plugin.Logger.LogInfo($"Cannabis render hook active on {camera.name}: roll limit {Plugin.CameraDriftDegrees.Value} degrees; RGB separation {Plugin.RgbShiftMinPixels.Value}–{Plugin.RgbShiftMaxPixels.Value} pixels; blur peak {Plugin.BlurRadiusPixels.Value} pixels; imageFailed={_imageFailed}; {camera.pixelWidth}x{camera.pixelHeight}.");
            _reportedRender = true;
        }
    }

    public void OnPostRender() => RestoreRotation();
    [HideFromIl2Cpp]
    internal void EndRender() => RestoreRotation();

    [HideFromIl2Cpp]
    private void RestoreRotation()
    {
        if (!_offsetApplied) return;
        transform.localRotation = _rotation;
        _offsetApplied = false;
    }

    [HideFromIl2Cpp]
    internal void Release()
    {
        Strength = 0f;
        RestoreRotation();
        _warp?.Dispose(); _warp = null;
        _rgb?.Dispose(); _rgb = null;
    }

    public void OnDisable() { Release(); if (Active == this) Active = null; }
    public void OnDestroy() { Release(); if (Active == this) Active = null; }
}

// Use the game's actual PPv2 render entry point, before it builds its command buffers.
[HarmonyPatch(typeof(PostProcessLayer), nameof(PostProcessLayer.OnPreCull))]
internal static class HighRenderBegin
{
    [HarmonyPrefix, HarmonyPriority(Priority.Last)]
    private static void Prefix(PostProcessLayer __instance)
    {
        var effect = HighCameraEffect.Active;
        if (effect != null && effect.gameObject == __instance.gameObject) effect.BeginRender();
    }
}

[HarmonyPatch(typeof(PostProcessLayer), nameof(PostProcessLayer.OnPostRender))]
internal static class HighRenderEnd
{
    [HarmonyPostfix]
    private static void Postfix(PostProcessLayer __instance)
    {
        var effect = HighCameraEffect.Active;
        if (effect != null && effect.gameObject == __instance.gameObject) effect.EndRender();
    }
}
