using System.Numerics;

namespace NivalisMods.Cigarette;

// Kinematics and search are independent of Unity. No bone lengths or scales change.
internal sealed class FingerFit
{
    internal Vector3 Root, FirstLink, SecondLink, TipLink;
    internal Quaternion RootRotation, MiddleRotation, TipRotation;
    internal Vector3 RootCurl, MiddleCurl, TipCurl, Splay;
    internal float SplayLimit = 20f;
    internal const float PadRadius = 0.007f;

    internal static Quaternion Turn(Vector3 axis, float degrees) =>
        Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), degrees * MathF.PI / 180f);

    internal Vector3[] Points(float[] angles)
    {
        var first = RootRotation * Turn(Splay, angles[3]) * Turn(RootCurl, angles[0]);
        var middle = first * MiddleRotation * Turn(MiddleCurl, angles[1]);
        var tip = middle * TipRotation * Turn(TipCurl, angles[2]);
        var p1 = Root + Vector3.Transform(FirstLink, first);
        var p2 = p1 + Vector3.Transform(SecondLink, middle);
        var p3 = p2 + Vector3.Transform(TipLink, tip);
        return new[] { Root, p1, p2, p3 };
    }

    internal float Penetration(GripSurface surface, float[] angles)
    {
        var points = Points(angles);
        var worst = 0f;
        for (var segment = 0; segment < 3; segment++)
        for (var sample = 0; sample <= 5; sample++)
        {
            var point = Vector3.Lerp(points[segment], points[segment + 1], sample / 5f);
            worst = Math.Max(worst, -surface.Clearance(point, PadRadius));
        }
        return worst;
    }

    internal float[] Solve(GripSurface surface)
    {
        var preferredTip = Points(new float[4])[3];
        var depth = surface.Depth(preferredTip.X, preferredTip.Y);
        if (float.IsFinite(depth)) preferredTip.Z = depth + PadRadius + 0.002f;
        var min = new[] { -75f, -60f, -40f, -SplayLimit };
        var max = new[] { 45f, 65f, 65f, SplayLimit };
        var best = new float[4];
        var bestScore = float.PositiveInfinity;
        // Different starting curls escape local minima around the bowl lip/stem.
        foreach (var start in new[] { 0f, -25f, -50f, 25f })
        {
            var angles = new[] { start, 0f, 0f, 0f };
            var score = Score(angles);
            foreach (var step in new[] { 20f, 10f, 5f, 2f, 0.5f })
            for (var pass = 0; pass < 10; pass++)
            {
                var changed = false;
                for (var joint = 0; joint < 4; joint++)
                {
                    var original = angles[joint]; var selected = original;
                    foreach (var sign in new[] { -1f, 1f })
                    {
                        angles[joint] = Math.Clamp(original + sign * step, min[joint], max[joint]);
                        var candidate = Score(angles);
                        if (candidate < score) { score = candidate; selected = angles[joint]; changed = true; }
                    }
                    angles[joint] = selected;
                }
                if (!changed) break;
            }
            if (score < bestScore) { bestScore = score; best = (float[])angles.Clone(); }
        }
        return best;

        float Score(float[] angles)
        {
            var points = Points(angles);
            var collision = 0f;
            for (var segment = 0; segment < 3; segment++)
            for (var sample = 0; sample <= 5; sample++)
            {
                var point = Vector3.Lerp(points[segment], points[segment + 1], sample / 5f);
                var overlap = Math.Max(0f, -surface.Clearance(point, PadRadius));
                collision += overlap * overlap;
            }
            var pose = angles.Sum(a => a * a) * 0.000000002f;
            return collision * 2000f + Vector3.DistanceSquared(points[3], preferredTip) + pose;
        }
    }
}
