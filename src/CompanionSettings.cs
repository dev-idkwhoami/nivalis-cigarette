using BepInEx.Configuration;
using NivalisMods.ModCompanion.Api;
using NivalisMods.ModCompanion;
using System.Runtime.CompilerServices;

namespace NivalisMods.Cigarette;

internal static class CompanionSettings
{
    // Keep the optional assembly out of Plugin.Load's JIT dependency graph.
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void Register(ConfigFile config)
    {
        var mod = SettingsRegistry.Register(Plugin.Id,
            new ModMetadata("Cigarette", "dev_idkwhoami", Plugin.Version,
                "Cigarettes, joints and inhalant masks."), config);
        var smoking = mod.AddCategory("Smoking", "Smoking");
        smoking.BindToggle("AllowSprintingWhileSmoking", "Allow sprinting while smoking", Plugin.AllowSprinting,
            "Reserved for walking smoking. No effect yet; movement still ends the session.");
        var hotkey = mod.Controls.AddInput("StandingSmoke", "Smoke while standing", "<Keyboard>/f8");
        Plugin.StandingSmokePressed = () => CompanionInput.WasPressed(hotkey);
        mod.Controls.AddParagraph("SmokingControls", "With empty hands, left-click a railing to smoke there, or use Smoke while standing anywhere on foot. Standing smoking holds the item in front of your chest. Movement or Escape ends smoking.");
    }
}
