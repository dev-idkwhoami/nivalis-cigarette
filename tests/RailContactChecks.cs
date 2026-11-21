using System.Numerics;
using NivalisMods.Cigarette;

internal static class RailContactChecks
{
    internal static void Run()
    {
        var oldMisses = 0;
        var probes = 0;
        foreach (var slope in new[] { 0f, 10f, 20f, 25f, 30f, 31f })
        foreach (var yaw in new[] { 0f, 37f, 90f, 173f })
        foreach (var sign in new[] { -1f, 1f })
        foreach (var side in new[] { -.22f, .22f })
        foreach (var depth in new[] { -.02f, 0f, .03f })
        {
            var angle = slope * MathF.PI / 180f;
            var rotation = Matrix4x4.CreateRotationY(yaw * MathF.PI / 180f);
            var normal = Vector3.TransformNormal(new Vector3(-sign * MathF.Sin(angle), MathF.Cos(angle), 0), rotation);
            var offset = Vector3.TransformNormal(new Vector3(side, 0, depth), rotation);
            // Independently constructed plane: y = 1.1 + side * signed slope.
            var surfaceY = 1.1f + side * sign * MathF.Tan(angle);
            var centre = RailContactGeometry.ProbeHeight(1.1f, normal.X, normal.Y, normal.Z, offset.X, offset.Z);
            var rayStart = centre + RailContactGeometry.ProbeHalfHeight;
            var distance = rayStart - surfaceY;
            if (normal.Y <= RailContactGeometry.MinimumNormalY || distance < 0 ||
                distance > RailContactGeometry.ProbeHalfHeight * 2f || MathF.Abs(centre - surfaceY) > .00001f)
                throw new Exception($"Slope probe missed {slope} degrees, yaw {yaw}, side {side}.");
            if (MathF.Abs(surfaceY - 1.1f) > .08f) oldMisses++;
            probes++;
        }
        if (oldMisses == 0) throw new Exception("Regression did not reproduce the old fixed-height miss.");
        // Raising the probe must not extend the accepted top-surface slope.
        if (MathF.Cos(40f * MathF.PI / 180f) > RailContactGeometry.MinimumNormalY)
            throw new Exception("40-degree rail unexpectedly accepted.");
        Console.WriteLine($"PASS: {probes} horizontal/sloped hand probes across both hands and world rotations; reproduced {oldMisses} old misses; steep-surface limit retained.");
    }
}
