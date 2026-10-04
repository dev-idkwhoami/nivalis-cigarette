using System.Numerics;

namespace NivalisMods.Cigarette;

internal static class RgbOffsets
{
    // Hash successive half-second intervals into random targets, then ease between
    // them. Absolute time keeps motion independent of frame rate/render count.
    internal static float AnimatedAmount(float time, float minimum, float maximum)
    {
        minimum = float.IsFinite(minimum) ? Math.Clamp(minimum, 0f, 32f) : 5f;
        maximum = float.IsFinite(maximum) ? Math.Clamp(maximum, 0f, 32f) : 15f;
        if (minimum > maximum) (minimum, maximum) = (maximum, minimum);
        time = float.IsFinite(time) ? Math.Max(0f, time) : 0f;
        var interval = MathF.Floor(time / 0.5f);
        var phase = time / 0.5f - interval;
        var eased = phase * phase * (3f - 2f * phase);
        var from = Target((uint)interval);
        var to = Target(unchecked((uint)interval + 1));
        return minimum + (maximum - minimum) * (from + (to - from) * eased);
    }

    private static float Target(uint interval)
    {
        unchecked
        {
            var value = interval + 0x9e3779b9u;
            value = (value ^ (value >> 16)) * 0x85ebca6bu;
            value = (value ^ (value >> 13)) * 0xc2b2ae35u;
            value ^= value >> 16;
            return (value & 0xffffffu) / 16777215f;
        }
    }

    internal static Vector2 Pixels(int channel, float time, float amount)
    {
        amount = float.IsFinite(amount) ? Math.Clamp(amount, 0f, 32f) : 15f;
        var spread = amount;
        var drift = amount * 0.2f * MathF.Sin(time * 0.43f);
        return channel switch
        {
            0 => new Vector2(spread, drift),
            1 => new Vector2(0f, -drift * 0.5f),
            2 => new Vector2(-spread, -drift),
            _ => throw new ArgumentOutOfRangeException(nameof(channel))
        };
    }
}
