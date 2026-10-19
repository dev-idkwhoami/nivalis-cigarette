namespace NivalisMods.Cigarette;

internal static class RailApproachMotion
{
    internal const float Allowance = 0.30f;
    internal const float MaximumAngle = 35f;
    // Same comfortable destination from either side of it. Keep the existing
    // 12% reach buffer, independent of how close the player happened to start.
    internal static float StandOff(float maximum) => maximum * 0.88f;
    // Keep the clicked position along the rail, but use its player-facing lip
    // for depth. A far-edge click must not push the standing target into it.
    internal static float NearEdgeShift(float depth) => Math.Max(0f, depth - 0.02f);
    internal static bool CanSettle(float remaining, float contactDistance, float maximum) =>
        remaining <= maximum * 0.25f && contactDistance <= maximum;
    internal static bool ReachablePlacement(float lateral, float contactDistance, float maximum) =>
        lateral <= maximum * 0.05f && contactDistance >= StandOff(maximum) - maximum * 0.05f &&
        contactDistance <= maximum;
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
        if (deltaTime <= 0f) return 0f;
        var scheduled = travel * Progress(elapsed);
        var catchUp = Math.Max(0f, scheduled - (travel - remaining));
        var speed = Math.Max(0.15f, travel / Duration * 1.5f);
        // The schedule already advances with time and subtracts actual motion.
        // Capping catch-up by this frame's dt repeatedly discards the backlog:
        // at high FPS a native minimum step can then prevent ANY progress.
        // Allow up to one 50 ms movement chunk, still bounded by the schedule
        // and remaining distance. Native Move continues to enforce collision.
        return Math.Min(remaining, Math.Min(catchUp, speed * 0.05f));
    }
}
