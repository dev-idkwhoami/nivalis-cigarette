namespace NivalisMods.Cigarette;

internal static class JointShape
{
    internal const float PaperLength = 0.064f, TipLength = 0.020f;
    internal const float TwistLength = 0.009f, TwistBurnFraction = 0.03f;
    internal const float MouthRadius = 0.00242f, BaseRadius = 0.00308f, EndRadius = 0.005203f;
    internal static (float Length, float Radius) Burn(float progress)
    {
        var burn = float.IsFinite(progress) ? progress : 0f;
        var remaining = 1f - Math.Clamp((burn - TwistBurnFraction) / (1f - TwistBurnFraction), 0f, 1f);
        return (PaperLength * remaining, BaseRadius + (EndRadius - BaseRadius) * remaining);
    }
}
