using HarmonyLib;
using Nivalis;

namespace NivalisMods.Cigarette;

// One native hide/show pair per session. No object discovery, graphic toggles or render hooks.
internal sealed class SmokingHud
{
    private static SmokingHud? _owner;
    private UIManager? _manager;
    private bool _started, _restoreOnExit, _issuingCall;

    internal void Hide()
    {
        if (_started) return;
        _started = true;
        _manager = UIManager._instance;
        if (_manager == null || !_manager.IsUiShown) return;
        _restoreOnExit = true;
        _owner = this;
        _issuingCall = true;
        try { _manager.HideUi(); }
        finally { _issuingCall = false; }
    }

    internal void Restore()
    {
        var restore = _restoreOnExit;
        _restoreOnExit = false;
        if (_owner == this) _owner = null;
        if (restore && _manager != null && _manager == UIManager._instance)
            _manager.ShowUi();
    }

    internal static void NativeVisibilityChanged(UIManager manager)
    {
        // A cutscene or another native UI owner takes precedence over our saved visible state.
        if (_owner != null && !_owner._issuingCall && _owner._manager == manager)
            _owner._restoreOnExit = false;
    }
}

[HarmonyPatch(typeof(UIManager), nameof(UIManager.HideUi))]
internal static class NativeUiHidePatch
{
    [HarmonyPostfix]
    private static void Postfix(UIManager __instance) => SmokingHud.NativeVisibilityChanged(__instance);
}

[HarmonyPatch(typeof(UIManager), nameof(UIManager.ShowUi))]
internal static class NativeUiShowPatch
{
    [HarmonyPostfix]
    private static void Postfix(UIManager __instance) => SmokingHud.NativeVisibilityChanged(__instance);
}
