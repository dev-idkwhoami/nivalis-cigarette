using System.Reflection;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using BepInEx.Logging;
using HarmonyLib;
using Il2CppInterop.Runtime.Attributes;
using Nivalis;
using UnityEngine;
using UnityEngine.InputSystem;

namespace NivalisMods.Cigarette.RailDev;

[BepInPlugin(Id, "Cigarette Rail Development", "1.0.0")]
[BepInDependency(NivalisMods.Cigarette.Plugin.Id)]
public sealed class Plugin : BasePlugin
{
    internal const string Id = "local.nivalis.cigarette.raildev";
    internal static ManualLogSource Logger = null!;
    internal static string Folder = null!;
    internal static string RulesPath => System.IO.Path.Combine(Folder, "trial-rails.json");
    internal static List<RailRule> Rules = new();
    internal static readonly Type Target = typeof(NivalisMods.Cigarette.Plugin).Assembly.GetType("NivalisMods.Cigarette.RailingTarget", true)!;
    internal static readonly MethodInfo TryTarget = AccessTools.Method(Target, "TryTarget");
    internal static readonly MethodInfo Blocker = AccessTools.Method(Target, "IsReviewedPlayerBlocker");
    internal static readonly MethodInfo Gameplay = AccessTools.Method(typeof(SmokingController), "GameplayAvailable");

    public override void Load()
    {
        Logger = Log;
        Folder = System.IO.Path.Combine(Paths.ConfigPath, "Cigarette.RailDev");
        Directory.CreateDirectory(Folder);
        try { Rules = RailRules.Read(RulesPath); }
        catch (Exception e) { Log.LogError("Trial rules not loaded: " + e.Message); }
        var harmony = new Harmony(Id);
        harmony.Patch(AccessTools.Method(Target, "IsRecognizedRail"), postfix: new HarmonyMethod(typeof(Plugin), nameof(RailPostfix)));
        harmony.Patch(Blocker, postfix: new HarmonyMethod(typeof(Plugin), nameof(BlockerPostfix)));
        AddComponent<RailProbe>();
        Log.LogInfo("LOCAL DEVELOPMENT ONLY. 1 capture; 2 toggle trial rail; 3 toggle blocker bypass; 4 reload. Files: " + Folder);
    }

    private static void RailPostfix(Collider collider, ref bool __result)
    {
        if (__result || collider == null || Rules.Count == 0) return;
        var identity = Identity(collider, "rail");
        __result = Rules.Any(r => r.Matches(identity));
    }
    private static void BlockerPostfix(Collider collider, ref bool __result)
    {
        if (__result || collider == null || Rules.Count == 0) return;
        var identity = Identity(collider, "blocker");
        __result = Rules.Any(r => r.Matches(identity));
    }

    internal static RailRule Identity(Collider c, string kind)
    {
        var names = new List<string>();
        for (var t = c.transform; t != null; t = t.parent) names.Add(t.name);
        names.Reverse();
        var mesh = c.TryCast<MeshCollider>()?.sharedMesh ?? c.GetComponent<MeshFilter>()?.sharedMesh;
        var p = c.transform.position;
        return new(kind, c.gameObject.scene.name, string.Join("/", names), mesh?.name ?? "", c.GetIl2CppType().Name, p.x, p.y, p.z);
    }
}

public sealed class RailProbe : MonoBehaviour
{
    public RailProbe(IntPtr pointer) : base(pointer) { }
    public void Update()
    {
        var key = Keyboard.current;
        if (key == null) return;
        try
        {
            if (key.digit4Key.wasPressedThisFrame)
            {
                Plugin.Rules = RailRules.Read(Plugin.RulesPath);
                Notify($"Reloaded {Plugin.Rules.Count} local trial rules.");
                return;
            }
            if (!key.digit1Key.wasPressedThisFrame && !key.digit2Key.wasPressedThisFrame && !key.digit3Key.wasPressedThisFrame) return;
            if (Plugin.Gameplay.Invoke(null, null) is not true) return;
            var character = PlayerManager._instance?.LocalPlayer?.Character;
            if (character == null) return;
            var eye = character.Controller.Camera.transform;
            var environment = character.Controller.HandsAnimator.environmentLayer.value;
            var hits = Physics.RaycastAll(eye.position, eye.forward, 3f, ~0, QueryTriggerInteraction.Ignore)
                .Where(h => h.collider != null && !h.collider.transform.IsChildOf(character.transform))
                .OrderBy(h => h.distance).ToArray();
            var args = new object?[] { character, default(RaycastHit), null };
            var eligible = (bool)Plugin.TryTarget.Invoke(null, args)!;
            var reason = (string?)args[2];
            var physical = hits.FirstOrDefault(h => (environment & (1 << h.collider.gameObject.layer)) != 0 &&
                Plugin.Blocker.Invoke(null, new object[] { h.collider }) is not true);
            RaycastHit top = default;
            var hasTop = physical.collider != null && Physics.Raycast(
                new Vector3(physical.point.x, character.transform.position.y + 1.65f, physical.point.z),
                Vector3.down, out top, 1f, environment, QueryTriggerInteraction.Ignore);
            var report = new
            {
                utc = DateTime.UtcNow, eligible, reason, player = Vec(character.transform.position),
                eye = Vec(eye.position), direction = Vec(eye.forward), environmentMask = environment,
                downwardProbe = hasTop ? new
                {
                    identity = Plugin.Identity(top.collider, "rail"), point = Vec(top.point), normal = Vec(top.normal),
                    sameCollider = top.collider == physical.collider,
                    heightAbovePlayer = top.point.y - character.transform.position.y
                } : null,
                hits = hits.Select(h => new
                {
                    identity = Plugin.Identity(h.collider, "rail"), distance = h.distance,
                    point = Vec(h.point), normal = Vec(h.normal), layer = h.collider.gameObject.layer,
                    inEnvironment = (environment & (1 << h.collider.gameObject.layer)) != 0,
                    reviewedBlocker = Plugin.Blocker.Invoke(null, new object[] { h.collider }),
                    boundsCenter = Vec(h.collider.bounds.center), boundsSize = Vec(h.collider.bounds.size)
                }).ToArray()
            };
            var file = "capture-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + ".json";
            File.WriteAllText(System.IO.Path.Combine(Plugin.Folder, file), System.Text.Json.JsonSerializer.Serialize(report, RailRules.Json));
            if (key.digit1Key.wasPressedThisFrame) { Notify($"{reason}. Saved {file}"); return; }
            var kind = key.digit3Key.wasPressedThisFrame ? "blocker" : "rail";
            var target = hits.FirstOrDefault(h => (environment & (1 << h.collider.gameObject.layer)) != 0 &&
                (kind == "blocker" || Plugin.Blocker.Invoke(null, new object[] { h.collider }) is not true));
            if (target.collider == null) { Notify("No environment collider within 3 m; capture saved."); return; }
            var rule = Plugin.Identity(target.collider, kind);
            var updated = new List<RailRule>(Plugin.Rules);
            var removed = updated.RemoveAll(r => r.Matches(rule)) > 0;
            if (!removed) updated.Add(rule);
            RailRules.Write(Plugin.RulesPath, updated);
            Plugin.Rules = updated;
            Notify($"{(removed ? "Removed" : "Added")} local {kind}: {target.collider.name}. Capture saved. Reach/top checks still apply.");
        }
        catch (Exception e) { Plugin.Logger.LogError(e); Notify("Rail tool failed: " + e.Message); }
    }

    [HideFromIl2Cpp]
    private static float[] Vec(Vector3 v) => new[] { v.x, v.y, v.z };
    [HideFromIl2Cpp]
    private void Notify(string text)
    {
        Plugin.Logger.LogInfo(text);
        try { NotificationManager._instance?.CreateMessage("Rail development", text, this); }
        catch (Exception e) { Plugin.Logger.LogWarning("Notification: " + e.Message); }
    }
}
