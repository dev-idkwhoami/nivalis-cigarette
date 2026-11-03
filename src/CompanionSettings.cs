using BepInEx.Configuration;
using NivalisMods.ModCompanion.Api;
using NivalisMods.ModCompanion;
using System.Runtime.CompilerServices;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

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
        smoking.BindChoice("SelectionMode", "Selection mode", Plugin.SelectionMode,
            new[] { new Choice<string>("Classic", "Classic"), new Choice<string>("Dynamic", "Dynamic") },
            "Classic opens the wheel. Dynamic: tap for the last used item, hold to choose. First use falls back to the first available item.");
        smoking.Slider("SelectionHoldSeconds", "Wheel hold duration (seconds)", 0.25f, 0.1f, 2f, 0.05f,
            "How long to hold the smoking control or railing click to open the wheel in Dynamic mode.");
        smoking.BindToggle("AllowSprintingWhileSmoking", "Allow sprinting while smoking", Plugin.AllowSprinting,
            "Allow sprinting during free smoking. Off by default; walking remains available.");
        smoking.BindToggle("EnableCigarette", "Cigarette", Plugin.EnabledItems[0], "Include cigarettes in the smoking selection.");
        smoking.BindToggle("EnableCannabis", "Cannabis", Plugin.EnabledItems[1], "Include cannabis in the smoking selection.");
        smoking.BindToggle("EnableInhalantMask", "Inhalant mask", Plugin.EnabledItems[2], "Include the inhalant mask in the smoking selection.");
        smoking.Text("Selection", "Classic skips the wheel with one available item. Dynamic holds always open the wheel. With no available items, smoking cannot start.");
        var hotkey = mod.Controls.AddInput("StandingSmoke", "Start / stop smoking", "<Keyboard>/x");
        Plugin.StandingSmokePressed = () => CompanionInput.WasPressed(hotkey);
        Plugin.StandingSmokeHeld = () => Held(hotkey.Keyboard?.Value) || Held(hotkey.Gamepad?.Value);
        mod.Controls.AddParagraph("SmokingControls", "With empty hands, left-click a railing to smoke there, or use Start / stop smoking anywhere on foot. Walking and stopping keep the same session. Tap the hotkey again or interact to end free smoking. In Dynamic mode, holding the hotkey during a session does nothing. Movement or Escape still ends railing smoking.");
    }

    private static bool Held(string? path) => !string.IsNullOrEmpty(path) &&
        InputSystem.FindControl(path)?.TryCast<ButtonControl>()?.isPressed == true;
}
