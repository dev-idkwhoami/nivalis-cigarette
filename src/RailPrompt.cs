using HarmonyLib;
using Nivalis;
using UnityEngine;

namespace NivalisMods.Cigarette;

[HarmonyPatch(typeof(CrosshairUI), nameof(CrosshairUI.LateUpdate))]
internal static class RailPrompt
{
    private static CrosshairUI? _owned;
    private static bool _labelActive, _labelEnabled;
    private static Color _labelColor;
    private static float _labelAlpha;

    [HarmonyPrefix]
    private static bool Prefix(CrosshairUI __instance)
    {
        try
        {
            if (SmokingController.Instance?.CanSmokeAtRail() == true)
            {
                if (_owned != __instance)
                {
                    Release();
                    var label = __instance.interactionTMP;
                    _labelActive = label.gameObject.activeSelf;
                    _labelEnabled = label.enabled;
                    _labelColor = label.color;
                    _labelAlpha = label.canvasRenderer.GetAlpha();
                    _owned = __instance;
                }
                if (!__instance._currentlyFocused) __instance.GoToCrosshairState(true);
                var text = __instance.interactionTMP;
                text.gameObject.SetActive(true);
                text.enabled = true;
                var color = text.color;
                color.a = 1f;
                text.color = color;
                text.canvasRenderer.SetAlpha(1f);
                text.text = "Smoke cigarette";
                return false;
            }
        }
        catch (Exception e) { Plugin.Logger.LogWarning("Railing prompt: " + e.Message); }
        Release();
        return true;
    }

    internal static void Release()
    {
        var owned = _owned;
        _owned = null;
        if (owned == null) return;
        owned.GoToCrosshairState(false);
        var label = owned.interactionTMP;
        if (label == null) return;
        label.text = "";
        label.color = _labelColor;
        label.canvasRenderer.SetAlpha(_labelAlpha);
        label.enabled = _labelEnabled;
        label.gameObject.SetActive(_labelActive);
    }
}
