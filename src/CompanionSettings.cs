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
                "Cigarettes, joints and inhalant masks.",
                Icon: ModIcon.FromResource(typeof(Plugin).Assembly, "Cigarette.Icons.cigarette.png")), config);
        var smoking = mod.AddCategory("General", "General");
        smoking.BindToggle("AllowSprintingWhileSmoking", "Allow sprinting while smoking", Plugin.AllowSprinting,
            "Allow sprinting during free smoking. Off by default; walking remains available.");
        smoking.BindToggle("EnableCigarette", "Cigarette", Plugin.EnabledItems[0], "Include cigarettes in the smoking selection.");
        smoking.BindToggle("EnableCannabis", "Cannabis", Plugin.EnabledItems[1], "Include cannabis in the smoking selection.");
        smoking.BindToggle("EnableInhalantMask", "Inhalant mask", Plugin.EnabledItems[2], "Include the inhalant mask in the smoking selection.");
        smoking.Text("Selection", "When only one item is available, smoking starts with it immediately. With no available items, smoking cannot start.");
        var hotkey = mod.Controls.AddInput("StandingSmoke", "Start / stop smoking", "<Keyboard>/x");
        Plugin.StandingSmokePressed = () => CompanionInput.WasPressed(hotkey);
        mod.Controls.AddParagraph("SmokingControls", "With empty hands, left-click a railing to smoke there, or use Start / stop smoking anywhere on foot. Walking and stopping keep the same session. Press the hotkey again or interact to end free smoking. Movement or Escape still ends railing smoking.");
    }
}
