using System.Numerics;

namespace NivalisMods.Cigarette;

internal static class HighMotion
{
    internal static float Roll(float time)
    {
        var phase = time % 14f;
        if (phase < 2f || phase >= 10f) return 0f;
        if (phase < 4f) return Smooth((phase - 2f) / 2f);
        if (phase < 7f) return 1f - 2f * Smooth((phase - 4f) / 3f);
        return -1f + Smooth((phase - 7f) / 3f);
    }

    private static float Smooth(float t) => t * t * (3f - 2f * t);

    // Twelve-second clear-to-soft-to-clear cycle, independent of zoom/focus depth.
    internal static float Blur(float time) => 0.5f - 0.5f * MathF.Cos(time * MathF.PI / 6f);

    internal static Vector2 Warp(float u, float v, float time, float strength)
    {
        var amplitude = Math.Clamp(strength, 0f, 10f) * 0.0035f;
        var border = amplitude * 1.8f;
        var x = border + u * (1f - 2f * border);
        var y = border + v * (1f - 2f * border);
        x += amplitude * (MathF.Sin(v * 10f + time * 1.1f) + 0.35f * MathF.Sin(u * 8f - time * 0.7f));
        y += amplitude * (0.7f * MathF.Sin(u * 9f - time * 0.85f) + 0.25f * MathF.Sin(v * 13f + time * 0.6f));
        return new Vector2(x, y);
    }
}
