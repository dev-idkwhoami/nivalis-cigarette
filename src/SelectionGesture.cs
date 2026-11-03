namespace NivalisMods.Cigarette;

internal enum SelectionDecision { None, Tap, Hold }

// One decision per press. Cancellation never becomes a tap on release.
internal sealed class SelectionGesture
{
    internal bool Pending { get; private set; }
    private float _started;
    internal void Begin(float now) { _started = now; Pending = true; }
    internal void Cancel() => Pending = false;
    internal bool StopRequested(float now, bool pressed, bool held, bool valid, bool dynamic, float threshold)
    {
        if (!valid) { Cancel(); return false; }
        if (!dynamic) { Cancel(); return pressed; }
        if (pressed) Begin(now);
        return Tick(now, held, true, threshold) == SelectionDecision.Tap;
    }

    internal SelectionDecision Tick(float now, bool held, bool valid, float threshold)
    {
        if (!Pending) return SelectionDecision.None;
        if (!valid) { Cancel(); return SelectionDecision.None; }
        threshold = float.IsFinite(threshold) ? Math.Clamp(threshold, 0.1f, 2f) : 0.25f;
        if (now - _started >= threshold) { Cancel(); return SelectionDecision.Hold; }
        if (!held) { Cancel(); return SelectionDecision.Tap; }
        return SelectionDecision.None;
    }
}
