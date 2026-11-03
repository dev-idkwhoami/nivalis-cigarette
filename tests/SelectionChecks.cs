using NivalisMods.Cigarette;

internal static class SelectionChecks
{
    internal static void Run()
    {
        var gesture = new SelectionGesture();
        void Expect(SelectionDecision expected, float time, bool held, bool valid = true, float threshold = .4f)
        {
            var actual = gesture.Tick(time, held, valid, threshold);
            if (actual != expected) throw new Exception($"Selection at {time}: expected {expected}, got {actual}");
        }
        gesture.Begin(0);
        Expect(SelectionDecision.None, .1f, true);
        Expect(SelectionDecision.Tap, .15f, false);
        Expect(SelectionDecision.None, .2f, false);
        gesture.Begin(0);
        Expect(SelectionDecision.Hold, .4f, true);
        Expect(SelectionDecision.None, .5f, false); // release after wheel cannot smoke
        gesture.Begin(0);
        Expect(SelectionDecision.None, .2f, true, false);
        Expect(SelectionDecision.None, .3f, false); // invalid target must not turn into tap
        gesture.Begin(0);
        gesture.Cancel();
        Expect(SelectionDecision.None, .2f, false);
        gesture.Begin(0);
        Expect(SelectionDecision.Hold, .5f, false); // release in the threshold frame
        gesture.Begin(0);
        Expect(SelectionDecision.None, .2f, true, threshold: float.NaN);
        Expect(SelectionDecision.Hold, .25f, true, threshold: float.NaN);
        foreach (var fps in new[] { 30, 60, 144, 360 })
        {
            gesture.Begin(0);
            for (var frame = 0; frame < (int)Math.Ceiling(.4 * fps); frame++)
                Expect(SelectionDecision.None, frame / (float)fps, true);
            Expect(SelectionDecision.Hold, (float)Math.Ceiling(.4 * fps) / fps, true);
        }
        var stop = new SelectionGesture();
        void Stop(bool expected, float now, bool pressed, bool held, bool valid = true, bool dynamic = true)
        {
            if (stop.StopRequested(now, pressed, held, valid, dynamic, .25f) != expected)
                throw new Exception($"Unexpected stop decision at {now}");
        }
        Stop(false, 0, true, true);
        Stop(true, .1f, false, false); // short tap stops on release
        Stop(false, .15f, false, false); // exactly once
        Stop(false, 1, true, true);
        Stop(false, 1.25f, false, true); // hold does not stop
        Stop(false, 2, false, true);
        Stop(false, 2.1f, false, false); // releasing the hold also does not stop
        Stop(false, 3, true, true);
        Stop(false, 3.05f, false, true, valid: false); // focus/menu/session loss
        Stop(false, 3.1f, false, false);
        Stop(true, 4, true, true, dynamic: false); // Classic stays immediate
        Stop(false, 5, true, true);
        Stop(false, 5.25f, false, false); // threshold-frame release is a hold
        Stop(false, 6, true, true);
        Stop(true, 6.1f, false, false); // tap still works after ignored holds
        foreach (var remembered in new[] { -1, 0, 1, 2, 99 })
        for (var mask = 0; mask < 8; mask++)
        {
            var available = Enum.GetValues<SmokeKind>().Where(kind => (mask & (1 << (int)kind)) != 0).ToArray();
            SmokeKind? expected = available.Length == 0 ? null :
                available.Contains((SmokeKind)remembered) ? (SmokeKind)remembered : available[0];
            if (SmokingSelection.Choose((SmokeKind)remembered, available) != expected)
                throw new Exception($"Wrong remembered-item fallback for item {remembered}, mask {mask}");
        }
        Console.WriteLine("PASS: selection tap/hold, single dispatch, cancellation, frame rates and all availability/remembered-item combinations.");
    }
}
