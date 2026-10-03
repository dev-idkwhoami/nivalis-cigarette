using BepInEx;
using System.IO;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;

namespace NivalisMods.Cigarette;

[BepInPlugin(Id, "Cigarette", Version)]
public sealed class Plugin : BasePlugin
{
    public const string Version = "1.0.0";
    public const string Id = "local.nivalis.cigarette";
    internal static ManualLogSource Logger = null!;
    internal const float DefaultInhaleVolume = 0.01f;
    internal const float DefaultExhaleVolume = 0.025f;
    internal static ConfigEntry<float> InhaleVolume = null!;
    internal static ConfigEntry<float> ExhaleVolume = null!;
    internal static ConfigEntry<float> RailDistance = null!;
    internal static ConfigEntry<float> SessionDuration = null!;
    internal static ConfigEntry<float> LookDownLimit = null!;

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
        SessionDuration = settings.Bind("Smoking", "SessionDurationSeconds", 60f, "Smoking session length in seconds (20–600). Paper burns only during draws.");
        LookDownLimit = settings.Bind("Smoking", "LookDownDegrees", 25f, "Maximum downward look angle while smoking (0 to 45 degrees). Normal look limits return on cancellation.");
        MouthHeight = settings.Bind("CustomDraw", "MouthHeightBelowEyes", -0.075f, "Mouth target vertical offset from camera in metres; negative is below eyes.");
        MouthDistance = settings.Bind("CustomDraw", "MouthDistanceFromEyes", 0.035f, "Filter endpoint distance forward of camera in metres.");
        settings.Save();
        settings.SaveOnConfigSet = true;
        new Harmony(Id).PatchAll(typeof(Plugin).Assembly);
        AddComponent<SmokingController>();
        Log.LogInfo($"Cigarette {Version}: Look at a nearby railing and left-click to smoke. Movement and Escape cancel.");
    }
}
