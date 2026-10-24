using System.Numerics;

namespace NivalisMods.Cigarette;

// Follow the native free arm's gait, with frame-rate-independent start/stop fades.
internal sealed class WalkingSway
{
    internal Vector3 Offset { get; private set; }
    internal void Update(Vector3 freeArmSwing, float speed, float deltaTime)
    {
        if (!float.IsFinite(speed) || !float.IsFinite(deltaTime) || deltaTime <= 0f) return;
        var target = -freeArmSwing * 0.20f;
        var length = target.Length();
        if (!float.IsFinite(length)) return;
        if (length > 0.025f) target *= 0.025f / length;
        target *= Math.Clamp(speed / 0.8f, 0f, 1f);
        Offset = Vector3.Lerp(Offset, target, 1f - MathF.Exp(-8f * deltaTime));
    }
}
