using System.Numerics;
using NivalisMods.Cigarette;

internal static class BoatRailChecks
{
    internal static void Run()
    {
        // Capture rays reconstructed in model space from the recorded cabin
        // transforms (rigid-fit residual < 0.000024 m). Mesh scale is 0.24.
        Check(new(-25.10283353f, 9.48019311f, 10.86885811f),
            new(-0.0436297f, -0.913324f, 0.40488968f), "SideLong", 0.7445832f);
        Check(new(-28.3230077f, 9.70937612f, 3.45028538f),
            new(-0.1937727f, -0.98083643f, -0.02030059f), "Rear", 0.693541f);
        if (BoatRailLayout.Boxes.Length != 3) throw new Exception("Only the three measured rear-deck rails belong in boat support.");
        foreach (var b in BoatRailLayout.Boxes)
        {
            if (b.MinX >= b.MaxX || b.MinY >= b.MaxY || b.MinZ >= b.MaxZ)
                throw new Exception("Invalid boat rail box: " + b.Name);
            if (Math.Abs(b.MaxY - 6.875f) > 0.0001f || (b.MaxY - b.MinY) * 0.24f > 0.091f)
                throw new Exception("Boat rail target must remain on the measured narrow top bar.");
            if (Hit(new Vector3(0, 20, 0), -Vector3.UnitY, b) != null)
                throw new Exception("Boat floor/interior was incorrectly included.");
        }
        Console.WriteLine("PASS: both recorded boat aim rays hit their measured rail boxes; targets exclude the deck/interior.");
    }

    private static void Check(Vector3 eye, Vector3 direction, string expected, float distance)
    {
        var hits = BoatRailLayout.Boxes.Select(b => (b.Name, Distance: Hit(eye, direction, b)))
            .Where(h => h.Distance.HasValue).OrderBy(h => h.Distance).ToArray();
        if (hits.Length == 0 || hits[0].Name != expected ||
            Math.Abs(hits[0].Distance!.Value * 0.24f - distance) > 0.002f)
            throw new Exception("Recorded ray did not hit the expected boat rail: " + expected);
        var contact = eye + direction * hits[0].Distance!.Value;
        var topOrigin = new Vector3(contact.X, 15f, contact.Z);
        var box = BoatRailLayout.Boxes.Single(b => b.Name == expected);
        var top = Hit(topOrigin, -Vector3.UnitY, box);
        if (top == null || Math.Abs(15f - top.Value - box.MaxY) > 0.001f)
            throw new Exception("Aimed boat rail has no matching downward top contact.");
    }

    // Independent slab intersection validates the data used by Unity BoxCollider.
    private static float? Hit(Vector3 origin, Vector3 direction, BoatRailBox b)
    {
        var low = 0f;
        var high = float.PositiveInfinity;
        foreach (var (o, d, min, max) in new[]
        {
            (origin.X, direction.X, b.MinX, b.MaxX),
            (origin.Y, direction.Y, b.MinY, b.MaxY),
            (origin.Z, direction.Z, b.MinZ, b.MaxZ)
        })
        {
            if (Math.Abs(d) < 1e-7f) { if (o < min || o > max) return null; continue; }
            var a = (min - o) / d;
            var z = (max - o) / d;
            low = Math.Max(low, Math.Min(a, z));
            high = Math.Min(high, Math.Max(a, z));
            if (high < low) return null;
        }
        return low;
    }
}
