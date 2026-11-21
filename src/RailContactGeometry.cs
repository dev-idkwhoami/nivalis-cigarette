namespace NivalisMods.Cigarette;

internal static class RailContactGeometry
{
    internal const float ProbeHalfHeight = 0.08f;
    internal const float MinimumNormalY = 0.85f;

    // Project a hand's horizontal sample onto the verified top's tangent plane.
    // A fixed centre height misses a 22 cm side offset at about 20 degrees.
    // This only centres the short probe; physics must still find the same rail.
    internal static float ProbeHeight(float topY, float normalX, float normalY, float normalZ,
        float offsetX, float offsetZ) => topY - (normalX * offsetX + normalZ * offsetZ) / normalY;
}
