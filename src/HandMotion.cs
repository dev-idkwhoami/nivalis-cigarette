namespace NivalisMods.Cigarette;

// Time-only curves, shared by the pose controller and managed checks.
internal static class HandMotion
{
    internal static float Smooth(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    internal static float FingerLift(float elapsed, int finger)
    {
        var rise = Smooth(elapsed / 0.16f);
        var fall = Smooth((elapsed - (0.28f + finger * 0.11f)) / 0.14f);
        return rise * (1f - fall);
    }

    internal static (float Lift, float Retract, float Lower) Exit(float progress) =>
        (Smooth(progress / 0.18f), Smooth((progress - 0.18f) / 0.47f), Smooth((progress - 0.65f) / 0.35f));
}
