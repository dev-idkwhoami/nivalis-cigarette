using System.Numerics;
using NivalisMods.Cigarette;

internal static class WalkingSwayChecks
{
    internal static void Run()
    {
        Vector3? reference = null;
        foreach (var fps in new[] { 30, 60, 144, 360 })
        {
            var sway = new WalkingSway();
            for (var i = 0; i < fps; i++) sway.Update(new Vector3(0f, 0f, 0.3f), 2f, 1f / fps);
            Require(sway.Offset.Z < 0f && sway.Offset.Length() <= 0.025001f, "Counter-swing is bounded");
            if (reference.HasValue) Require(Vector3.Distance(reference.Value, sway.Offset) < 0.00001f, "Frame-rate-independent ramp");
            reference = sway.Offset;
            var moving = sway.Offset;
            sway.Update(Vector3.Zero, 0f, 0f);
            Require(sway.Offset == moving, "Pause does not advance sway");
            for (var i = 0; i < fps; i++) sway.Update(new Vector3(1f), 0f, 1f / fps);
            Require(sway.Offset.Length() < 0.00001f, "Stopping fades to still even with an offset native arm");
            for (var i = 0; i < fps; i++) sway.Update(new Vector3(0f, 0f, -0.3f), 2f, 1f / fps);
            Require(sway.Offset.Z > 0.024f, "Walking resumes without a new sway/session object");
        }
        Console.WriteLine("PASS: walking sway bounds, gait direction, pause, stop/resume and 30–360 FPS fades.");
    }
    private static void Require(bool value, string message)
    {
        if (!value) throw new Exception("Walking sway: " + message);
    }
}
