using NivalisMods.Cigarette.RailDev;

var rail = new RailRule("rail", "1_Lowtown", "Building/Rail", "shared building mesh", "MeshCollider", 1, 2, 3);
if (!rail.Matches(rail with { X = 1.01f })) throw new Exception("Small position rounding must match");
foreach (var other in new[] { rail with { Scene = "2_Meridian_Market" }, rail with { Path = "Other/Rail" },
    rail with { Mesh = "other mesh" }, rail with { Collider = "BoxCollider" }, rail with { X = 2 },
    rail with { Y = 3 }, rail with { Kind = "blocker" } })
    if (rail.Matches(other)) throw new Exception("Trial rule leaked onto another object or rule kind");
var directory = Path.Combine(Path.GetTempPath(), "rail-dev-test-" + Guid.NewGuid());
Directory.CreateDirectory(directory);
try
{
    var path = Path.Combine(directory, "trial.json");
    if (RailRules.Read(path).Count != 0) throw new Exception("Missing rules must start empty");
    RailRules.Write(path, new() { rail });
    var loaded = RailRules.Read(path);
    if (loaded.Count != 1 || !loaded[0].Matches(rail)) throw new Exception("Saved rule did not round trip");
    RailRules.Write(path, new());
    if (RailRules.Read(path).Count != 0 || RailRules.Read(path + ".bak").Count != 1)
        throw new Exception("Toggle removal or backup failed");
    foreach (var invalid in new[] { "null", "[null]", "not json", "[{\"Kind\":\"rail\"}]" })
    {
        File.WriteAllText(path, invalid);
        try { RailRules.Read(path); throw new Exception("Invalid rules were accepted"); }
        catch (System.Text.Json.JsonException) { }
        catch (InvalidDataException) { }
    }
}
finally { Directory.Delete(directory, true); }
Console.WriteLine("PASS: rail trials match only the selected scene/object/position; JSON persistence, removal, backup and invalid input.");
