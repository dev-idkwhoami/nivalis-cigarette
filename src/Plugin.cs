using BepInEx;
using System.IO;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;

namespace NivalisMods.Cigarette;

[BepInPlugin(Id, "Cigarette", Version)]
public sealed class Plugin : BasePlugin
{
    public const string Version = "1.1.0";
    public const string Id = "local.nivalis.cigarette";
    internal static ManualLogSource Logger = null!;
    internal const float DefaultInhaleVolume = 0.01f;
    internal const float DefaultMaskInhaleVolume = 0.125f;
    internal const float DefaultExhaleVolume = 0.025f;
    internal static ConfigEntry<float> InhaleVolume = null!;
    internal static ConfigEntry<float> ExhaleVolume = null!;
    internal static ConfigEntry<float> RailDistance = null!;
    internal static ConfigEntry<float> SessionDuration = null!;
    internal static ConfigEntry<float> CannabisSessionDuration = null!;
    internal static ConfigEntry<float> MaskSessionDuration = null!;
    internal static ConfigEntry<float> MaskInhaleVolume = null!;
    internal static ConfigEntry<float> MaskWorldSpeed = null!;
    internal static ConfigEntry<float> MaskDurationMultiplier = null!;
    internal static ConfigEntry<float> LookDownLimit = null!;
    internal static ConfigEntry<float> HighDurationMultiplier = null!;
    internal static ConfigEntry<float> ApparitionHeight = null!;
    internal static ConfigEntry<float> CameraDriftDegrees = null!;
    internal static ConfigEntry<float> WobbleStrength = null!;
    internal static ConfigEntry<bool> SoftFocus = null!;
    internal static ConfigEntry<float> RgbShiftMinPixels = null!;
    internal static ConfigEntry<float> RgbShiftMaxPixels = null!;
    internal static ConfigEntry<float> BlurRadiusPixels = null!;

    internal static ConfigEntry<float> MouthHeight = null!;
    internal static ConfigEntry<float> MouthDistance = null!;

    public override void Load()
    {
        Logger = Log;
        var settings = new ConfigFile(Path.Combine(Paths.ConfigPath, "cigarette.cfg"), false,
            new BepInPlugin(Id, "Cigarette", Version)) { SaveOnConfigSet = false };
        InhaleVolume = settings.Bind("Audio", "InhaleVolume", DefaultInhaleVolume, "Inhale sound volume (0–1); 0 mutes inhaling.");
        ExhaleVolume = settings.Bind("Audio", "ExhaleVolume", DefaultExhaleVolume, "Exhale sound volume (0–1); 0 mutes exhaling.");
        try { BreathAudio.LoadRecordings(); }
        catch (Exception e) { Log.LogWarning("Could not load breath recordings: " + e.Message); }
        RailDistance = settings.Bind("Smoking", "MaximumRailDistanceMetres", 0.45f, "Maximum horizontal player-to-railing activation distance (0.2–0.65 metres).");
        SessionDuration = settings.Bind("Smoking", "SessionDurationSeconds", 60f, "Cigarette smoking duration in seconds (20–600). Paper burns only during draws.");
        CannabisSessionDuration = settings.Bind("Cannabis", "SessionDurationSeconds", 60f, "Cannabis smoking duration in seconds (20–600), independent of cigarettes. After-effect time still depends on consumed draws and HighDurationMultiplier.");
        MaskSessionDuration = settings.Bind("Mask", "SessionDurationSeconds", 60f, "Inhalant-mask session duration in seconds (20–600), independent of cigarettes and cannabis.");
        MaskInhaleVolume = settings.Bind("Mask", "InhaleVolume", DefaultMaskInhaleVolume, "Mask bubbling/inhale volume (0–1); 0 mutes it. Exhale uses Audio.ExhaleVolume without smoke.");
        MaskWorldSpeed = settings.Bind("Mask", "WorldSpeed", 0.5f, "World speed after finishing the mask (0.1–1); 0.5 is half speed, 1 disables slow motion.");
        MaskDurationMultiplier = settings.Bind("Mask", "AfterEffectDurationMultiplier", 3f, "After-effect seconds per equivalent session second consumed (0–10), like cannabis. A full 60-second session earns 180 seconds at 3; partial inhalations earn proportionally less.");
        LookDownLimit = settings.Bind("Smoking", "LookDownDegrees", 25f, "Maximum downward look angle while smoking (0 to 45 degrees). Normal look limits return on cancellation.");
        HighDurationMultiplier = settings.Bind("Cannabis", "HighDurationMultiplier", 3f, "After-effect seconds per equivalent second smoked (0–10). A complete 60-second blunt earns 180 seconds at 3; partial draws earn proportionally less.");
        ApparitionHeight = settings.Bind("Cannabis", "ApparitionHeightMetres", 2.1f, "World singer apparition height (0.5–3.75 metres).");
        CameraDriftDegrees = settings.Bind("Cannabis", "CameraDriftDegrees", 2.5f, "Occasional left/right camera roll after smoking (0–5 degrees); 0 disables it.");
        WobbleStrength = settings.Bind("Cannabis", "WobbleStrength", 0f, "Experimental full-screen waves (0–10); disabled by default in favour of RGB separation.");
        SoftFocus = settings.Bind("Cannabis", "SoftFocus", true, "Enable a slow whole-world image blur cycle during the high, independent of zoom.");
        BlurRadiusPixels = settings.Bind("Cannabis", "BlurRadiusPixels", 4f, "Peak radius of the slow image blur cycle (0–8 pixels).");
        RgbShiftMinPixels = settings.Bind("Cannabis", "RgbShiftMinPixels", 5f, "Minimum animated RGB offset (0–32 pixels). Smoothly drifts toward a new random amount every half-second.");
        RgbShiftMaxPixels = settings.Bind("Cannabis", "RgbShiftMaxPixels", 15f, "Maximum animated RGB offset (0–32 pixels). Equal limits give a fixed amount; both 0 disable RGB. Reversed limits are sorted.");
        MouthHeight = settings.Bind("CustomDraw", "MouthHeightBelowEyes", -0.075f, "Mouth target vertical offset from camera in metres; negative is below eyes.");
        MouthDistance = settings.Bind("CustomDraw", "MouthDistanceFromEyes", 0.035f, "Filter endpoint distance forward of camera in metres.");
        settings.Save();
        settings.SaveOnConfigSet = true;
        new Harmony(Id).PatchAll(typeof(Plugin).Assembly);
        ClassInjector.RegisterTypeInIl2Cpp<HighCameraEffect>();
        AddComponent<SmokingController>();
        Log.LogInfo($"Cigarette {Version}: Left-click a nearby railing to choose cigarette, joint or inhalant mask. Movement and Escape cancel.");
    }
}
