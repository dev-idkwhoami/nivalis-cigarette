namespace NivalisMods.Cigarette;

internal static class RailApproachMotion
{
    internal const float Allowance = 0.30f;
    internal const float MaximumAngle = 35f;
    // Same comfortable destination from either side of it. Keep the existing
    // 12% reach buffer, independent of how close the player happened to start.
    internal static float StandOff(float maximum) => maximum * 0.88f;
    // Native boat navigation can correct placement by a few millimetres.
    // Placement tolerance must scale with usable reach, not with a tiny plan.
    internal static bool PositionReady(float remaining, float contactDistance, float maximum) =>
        remaining <= maximum * 0.05f && contactDistance <= maximum;
    internal static float MovementBudget(float travel, float maximum) =>
        Math.Max(travel * 1.5f, maximum * 0.15f);
    internal const float Duration = 0.65f;
    internal static float Progress(float elapsed)
    {
        var t = Math.Clamp(elapsed / Duration, 0f, 1f);
        return t * t * (3f - 2f * t);
    }
    internal static float Step(float travel, float remaining, float elapsed, float deltaTime)
    {
        var scheduled = travel * Progress(elapsed);
        var catchUp = Math.Max(0f, scheduled - (travel - remaining));
        var speed = Math.Max(0.15f, travel / Duration * 1.5f);
        return Math.Min(remaining, Math.Min(catchUp, speed * Math.Clamp(deltaTime, 0f, 0.05f)));
    }
}
