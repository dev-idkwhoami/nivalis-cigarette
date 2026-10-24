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
            "Allow sprinting during free smoking. Off by default; walking remains available.");
        var hotkey = mod.Controls.AddInput("StandingSmoke", "Start / stop smoking", "<Keyboard>/x");
        Plugin.StandingSmokePressed = () => CompanionInput.WasPressed(hotkey);
        mod.Controls.AddParagraph("SmokingControls", "With empty hands, left-click a railing to smoke there, or use Start / stop smoking anywhere on foot. Walking and stopping keep the same session. Press the hotkey again or interact to end free smoking. Movement or Escape still ends railing smoking.");
    }
}
