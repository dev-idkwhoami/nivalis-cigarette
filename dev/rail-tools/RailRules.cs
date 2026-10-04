using System.Text.Json;

namespace NivalisMods.Cigarette.RailDev;

internal sealed record RailRule(string Kind, string Scene, string Path, string Mesh, string Collider,
    float X, float Y, float Z)
{
    internal bool Matches(RailRule other) => Kind == other.Kind && Scene == other.Scene &&
        Path == other.Path && Mesh == other.Mesh && Collider == other.Collider &&
        Math.Abs(X - other.X) <= 0.05f && Math.Abs(Y - other.Y) <= 0.05f && Math.Abs(Z - other.Z) <= 0.05f;
}

internal static class RailRules
{
    internal static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    internal static List<RailRule> Read(string path)
    {
        if (!File.Exists(path)) return new();
        var rules = JsonSerializer.Deserialize<List<RailRule>>(File.ReadAllText(path), Json)
            ?? throw new InvalidDataException("Rules must be a JSON array.");
        if (rules.Any(r => r == null || (r.Kind != "rail" && r.Kind != "blocker") ||
            string.IsNullOrWhiteSpace(r.Scene) || string.IsNullOrWhiteSpace(r.Path) ||
            r.Mesh == null || string.IsNullOrWhiteSpace(r.Collider) ||
            !float.IsFinite(r.X) || !float.IsFinite(r.Y) || !float.IsFinite(r.Z)))
            throw new InvalidDataException("Each rule needs kind rail/blocker, scene, path, mesh, collider and finite coordinates.");
        return rules;
    }

    internal static void Write(string path, List<RailRule> rules)
    {
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(rules, Json));
        if (File.Exists(path)) File.Copy(path, path + ".bak", true);
        File.Move(temporary, path, true);
    }
}
