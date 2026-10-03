namespace NivalisMods.Cigarette;

// Preplanned draws make total burn independent of frame rate and random rest intervals.
internal sealed class SmokingSession
{
    internal readonly float Duration;
    internal readonly float[] DrawStarts;
    internal SmokingSession(float duration, Func<float> random)
    {
        Duration = float.IsFinite(duration) ? Math.Clamp(duration, 20f, 600f) : 60f;
        var count = Math.Max(2, (int)(Duration / 12f));
        DrawStarts = new float[count];
        var spacing = (Duration - 8.3f - 2f) / (count - 1);
        for (var i = 0; i < count; i++)
            DrawStarts[i] = 2f + i * spacing + (i == 0 || i == count - 1 ? 0f : (random() * 2f - 1f) * Math.Min(0.8f, spacing * 0.08f));
    }
    internal float BurnProgress(float elapsed)
    {
        var draws = 0f;
        foreach (var start in DrawStarts) draws += Math.Clamp((elapsed - start - 1.5f) / 2.3f, 0f, 1f);
        return draws / DrawStarts.Length;
    }
}
