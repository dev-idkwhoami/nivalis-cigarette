using NivalisMods.Cigarette;
// Time ownership must restore non-default speed and preserve an external pause.
var slow = new MaskSlowMotion();
var slowTime = slow.Apply(0.8f, 0.02f, 1f, 0.35f);
Near(slowTime.Scale, 0.28f, "Slow relative to previous world speed");
Near(slowTime.Step, 0.007f, "Physics cadence remains smooth");
slowTime = slow.Apply(slowTime.Scale, slowTime.Step, 0f, 0.35f);
Near(slowTime.Scale, 0.8f, "Restore previous speed after effect");
Near(slowTime.Step, 0.02f, "Restore physics step after effect");
slowTime = slow.Apply(1f, 0.02f, 1f, 0.35f);
slowTime = slow.Apply(0f, slowTime.Step, 1f, 0.35f);
Near(slowTime.Scale, 0f, "Never unpause the world");
Near(slowTime.Step, 0.02f, "Pause releases owned physics step");
slowTime = slow.Apply(1f, 0.02f, 1f, 0.35f);
Near(slowTime.Scale, 1f, "Do not resume interrupted effect");
slow.Apply(1f, 0.02f, 0f, 0.35f);
slowTime = slow.Apply(1f, 0.02f, 1f, 0.35f);
slowTime = slow.Release(slowTime.Scale, slowTime.Step);
Near(slowTime.Scale, 1f, "Cancellation restores speed");
Near(slowTime.Step, 0.02f, "Cancellation restores physics");
slowTime = slow.Apply(1f, 0.02f, 1f, 1f);
Near(slowTime.Scale, 1f, "Configured off does not slow the world");
slow.Release(slowTime.Scale, slowTime.Step);
slowTime = slow.Apply(1f, 0.02f, 1f, float.NaN);
Near(slowTime.Scale, 0.5f, "Invalid speed uses default");
slowTime = slow.Apply(0.6f, 0.012f, 1f, 0.35f);
Near(slowTime.Scale, 0.6f, "External speed takes priority");
Near(slowTime.Step, 0.012f, "External physics step takes priority");
Console.WriteLine("PASS: mask slow-motion restore, cancellation, pause and external time ownership.");
var maskEarned = new HighSession();
maskEarned.BeginSmoke();
maskEarned.Credit(0f, 60f, 3f);
Near(maskEarned.Remaining, 0f, "No inhaling earns no slow motion");
maskEarned.Credit(0.25f, 60f, 3f);
Near(maskEarned.Remaining, 45f, "Quarter of mask earns quarter of after-effect");
maskEarned.Tick(10f, true);
Near(maskEarned.Remaining, 45f, "Effect timer waits while smoking and lowering");
maskEarned.Credit(0.25f, 60f, 3f);
Near(maskEarned.Remaining, 45f, "Repeated credit cannot add unconsumed time");
maskEarned.Tick(10f, false);
Near(maskEarned.Remaining, 35f, "After-effect counts down after smoking");
maskEarned.BeginSmoke();
maskEarned.Credit(0.5f, 60f, 3f);
Near(maskEarned.Remaining, 125f, "Further mask draws add earned time");
maskEarned.Tick(200f, false);
Near(maskEarned.Remaining, 0f, "After-effect expires");
var movementTime = new MaskSlowMotion();
if (movementTime.OwnsSlowMotion(0.5f)) throw new Exception("Unowned slow motion must not bypass movement gate");
var movementScale = movementTime.Apply(1f, 0.02f, 1f, 0.5f);
if (!movementTime.OwnsSlowMotion(movementScale.Scale)) throw new Exception("Mask-owned slow motion must allow native movement");
if (movementTime.OwnsSlowMotion(0f) || movementTime.OwnsSlowMotion(0.25f) || movementTime.OwnsSlowMotion(1f))
    throw new Exception("Pause, external time and normal speed must not bypass movement gate");
movementTime.Release(movementScale.Scale, movementScale.Step);
if (movementTime.OwnsSlowMotion(0.5f)) throw new Exception("Expired effect must not bypass movement gate");
Console.WriteLine("PASS: movement bypass is limited to active mask-owned slow motion.");
// A hollow shape must use its outermost shell, not accept pads in the cavity.
var shellVertices = new System.Numerics.Vector3[]
{
    new(-0.2f, -0.2f, 0f), new(0.2f, -0.2f, 0f), new(0.2f, 0.2f, 0f), new(-0.2f, 0.2f, 0f),
    new(-0.2f, -0.2f, -0.04f), new(0.2f, -0.2f, -0.04f), new(0.2f, 0.2f, -0.04f), new(-0.2f, 0.2f, -0.04f)
};
var shell = new GripSurface(shellVertices, new[] { 0, 1, 2, 0, 2, 3, 4, 5, 6, 4, 6, 7 });
Near(shell.Depth(0f, 0f), 0f, "Contact uses outside of shell");
if (shell.Clearance(new(0f, 0f, -0.02f), 0.007f) >= 0f ||
    shell.Clearance(new(0f, 0f, 0.004f), 0.007f) >= 0f ||
    shell.Clearance(new(0f, 0f, 0.01f), 0.007f) <= 0f)
    throw new Exception("Finger clearance must account for both interior and skin thickness");
// The stem is a finite solid, not an infinite shadow covering free space.
var stemVertices = new List<System.Numerics.Vector3>();
var stemTriangles = new List<int>();
void AddPlane(float halfWidth, float z)
{
    var first = stemVertices.Count;
    stemVertices.AddRange(new System.Numerics.Vector3[] { new(-halfWidth, -0.08f, z), new(halfWidth, -0.08f, z),
        new(halfWidth, 0.08f, z), new(-halfWidth, 0.08f, z) });
    stemTriangles.AddRange(new[] { first, first+1, first+2, first, first+2, first+3 });
}
AddPlane(0.08f, -0.02f); AddPlane(0.005f, 0.04f); AddPlane(0.005f, 0.06f);
var bowlAndStem = new GripSurface(stemVertices.ToArray(), stemTriangles.ToArray(), -0.012f);
if (bowlAndStem.Clearance(new(0f, 0f, 0f), 0.007f) <= 0f)
    throw new Exception("Empty space between stem and bowl must remain available to the hand");
if (bowlAndStem.Clearance(new(0f, 0f, 0.05f), 0.007f) >= 0f ||
    bowlAndStem.Clearance(new(0f, 0f, -0.03f), 0.007f) >= 0f)
    throw new Exception("Real stem and bowl penetration must still be rejected");
var fingerFixture = new FingerFit
{
    Root = new(0f, -0.08f, 0.04f), FirstLink = new(0f, 0.04f, 0f),
    SecondLink = new(0f, 0.03f, 0f), TipLink = new(0f, 0.02f, 0f),
    RootRotation = FingerFit.Turn(System.Numerics.Vector3.UnitX, -65f),
    MiddleRotation = FingerFit.Turn(System.Numerics.Vector3.UnitX, -20f),
    TipRotation = System.Numerics.Quaternion.Identity,
    RootCurl = -System.Numerics.Vector3.UnitX, MiddleCurl = -System.Numerics.Vector3.UnitX,
    TipCurl = -System.Numerics.Vector3.UnitX, Splay = -System.Numerics.Vector3.UnitZ
};
if (fingerFixture.Penetration(shell, new float[4]) < 0.01f) throw new Exception("Invalid intersecting finger fixture");
var fittedAngles = fingerFixture.Solve(shell);
if (fingerFixture.Penetration(shell, fittedAngles) > 0.001f) throw new Exception("Finger fitter failed to clear a reachable shell");
var fittedPoints = fingerFixture.Points(fittedAngles);
Near(System.Numerics.Vector3.Distance(fittedPoints[0], fittedPoints[1]), 0.04f, "Fitting preserves proximal length");
Near(System.Numerics.Vector3.Distance(fittedPoints[1], fittedPoints[2]), 0.03f, "Fitting preserves middle length");
Near(System.Numerics.Vector3.Distance(fittedPoints[2], fittedPoints[3]), 0.02f, "Fitting preserves fingertip length");
Console.WriteLine("PASS: exterior shell/cavity exclusion, fingertip thickness, intersecting-pose fitting and preserved bone lengths.");
var previousRgb = RgbOffsets.AnimatedAmount(0f, 5f, 10f);
var lowestRgb = previousRgb;
var highestRgb = previousRgb;
for (var frame = 0; frame <= 36000; frame++)
{
    var time = frame / 60f;
    var amount = RgbOffsets.AnimatedAmount(time, 5f, 10f);
    if (amount < 5f || amount > 10f || Math.Abs(amount - previousRgb) > 0.251f)
        throw new Exception("Animated RGB must stay inside its limits without sudden jumps");
    Near(RgbOffsets.AnimatedAmount(time, 10f, 5f), amount, "Reversed RGB limits");
    Near(RgbOffsets.AnimatedAmount(time, 7f, 7f), 7f, "Fixed RGB amount");
    Near(RgbOffsets.AnimatedAmount(time, 0f, 0f), 0f, "Disabled RGB amount");
    Near(RgbOffsets.Pixels(0, time, amount).X, amount, "Rendered offset follows animated range");
    lowestRgb = Math.Min(lowestRgb, amount);
    highestRgb = Math.Max(highestRgb, amount);
    previousRgb = amount;
}
if (highestRgb - lowestRgb < 4f) throw new Exception("RGB motion must explore its configured range");
// Each half-second is one complete transition, with continuous motion between targets.
for (var step = 0; step < 20; step++)
{
    var start = step * 0.5f;
    var from = RgbOffsets.AnimatedAmount(start, 5f, 10f);
    var to = RgbOffsets.AnimatedAmount(start + 0.5f, 5f, 10f);
    Near(RgbOffsets.AnimatedAmount(start + 0.25f, 5f, 10f), (from + to) * 0.5f,
        "RGB reaches transition midpoint after a quarter-second");
}
var invalidRgb = RgbOffsets.AnimatedAmount(float.NaN, float.NaN, float.PositiveInfinity);
if (!float.IsFinite(invalidRgb) || invalidRgb < 5f || invalidRgb > 15f)
    throw new Exception("Invalid RGB config must fall back to safe defaults");
Near(RgbOffsets.AnimatedAmount(1f, -10f, -5f), 0f, "Negative RGB limits clamp to zero");
Near(RgbOffsets.AnimatedAmount(1f, 40f, 50f), 32f, "Excessive RGB limits clamp to maximum");
Console.WriteLine("PASS: smooth random RGB range, boundaries, equal/reversed/invalid limits, and actual channel offsets.");
Near(HighMotion.Blur(0f), 0f, "Blur starts clear");
Near(HighMotion.Blur(6f), 1f, "Blur reaches peak halfway through cycle");
Near(HighMotion.Blur(12f), 0f, "Blur returns to clear after twelve seconds");
for (var frame = 0; frame <= 720; frame++)
{
    var blur = HighMotion.Blur(frame / 60f);
    if (blur < 0f || blur > 1f) throw new Exception("Blur cycle exceeds strength limits");
    if (frame > 0 && Math.Abs(blur - HighMotion.Blur((frame - 1) / 60f)) > 0.0045f)
        throw new Exception("Blur cycle has a sudden jump");
}
Console.WriteLine("PASS: twelve-second clear-soft-clear blur cycle with smooth bounded motion.");
for (var frame = 0; frame <= 600; frame++)
{
    var time = frame / 60f;
    var red = RgbOffsets.Pixels(0, time, 3f);
    var green = RgbOffsets.Pixels(1, time, 3f);
    var blue = RgbOffsets.Pixels(2, time, 3f);
    if (red.X < 2.09f || red.X > 3.001f || green.X != 0f || Math.Abs(red.X + blue.X) > 0.00001f)
        throw new Exception("RGB must separate channels in bounded opposite directions across the entire scene");
    for (var channel = 0; channel < 3; channel++)
        if (RgbOffsets.Pixels(channel, time, 0f) != System.Numerics.Vector2.Zero)
            throw new Exception("Zero RGB strength must leave channel positions unchanged");
}
// A white vertical edge produces separate coloured edges, not a colour cast on
// either constant region. This checks the actual source-channel sampling rule.
static float Edge(float x) => x >= 8f ? 1f : 0f;
var rOffset = RgbOffsets.Pixels(0, 0f, 3f).X;
var bOffset = RgbOffsets.Pixels(2, 0f, 3f).X;
if (Edge(7f + rOffset) != 1f || Edge(7f) != 0f || Edge(7f + bOffset) != 0f ||
    Edge(15f + rOffset) != 1f || Edge(15f + bOffset) != 1f || Edge(rOffset) != 0f)
    throw new Exception("Channel separation must create an edge fringe and preserve flat white/black regions");
Console.WriteLine("PASS: RGB channel pixel offsets, opposite fringes, no shift at zero, and preserved flat colours.");
for (var step = 0; step <= 100; step++)
{
    var shape = JointShape.Burn(step / 100f);
    if (shape.Length < 0f || shape.Radius < JointShape.BaseRadius || shape.Radius > JointShape.EndRadius)
        throw new Exception("Invalid joint cone dimensions");
    if (step > 3 && shape.Radius >= JointShape.Burn((step - 1) / 100f).Radius)
        throw new Exception("Burn must remove the wide end of the cone");
}
Near(JointShape.Burn(0f).Length, 0.064f, "Full paper length");
Near(JointShape.Burn(1f).Length, 0f, "Spent joint paper length");
Near(JointShape.Burn(JointShape.TwistBurnFraction).Length, JointShape.PaperLength, "Twisted closure burns before paper body");
Near(HighMotion.Roll(4f), 1f, "Camera reaches full right roll");
Near(HighMotion.Roll(7f), -1f, "Camera reaches full left roll");
Near(HighMotion.Roll(12f), 0f, "Camera rests between sways");
for (var step = 0; step <= 1400; step++)
{
    var t = step / 100f;
    if (Math.Abs(HighMotion.Roll(t)) > 1.00001f) throw new Exception("Roll exceeds configured angle");
    if (step > 0 && Math.Abs(HighMotion.Roll(t) - HighMotion.Roll(t - 0.01f)) > 0.011f)
        throw new Exception("Camera roll jumps");
    for (var y = 0; y <= 10; y++)
    for (var x = 0; x <= 10; x++)
    {
        var uv = HighMotion.Warp(x / 10f, y / 10f, t, 10f);
        if (uv.X < 0f || uv.X > 1f || uv.Y < 0f || uv.Y > 1f)
            throw new Exception("Screen waves expose texture borders");
        var unchanged = HighMotion.Warp(x / 10f, y / 10f, t, 0f);
        Near(unchanged.X, x / 10f, "Disabled horizontal distortion");
        Near(unchanged.Y, y / 10f, "Disabled vertical distortion");
    }
}
Console.WriteLine("PASS: tapered joint burn, full camera roll amplitude, smooth sway/rest transitions, bounded full-screen warp and zero distortion.");
static void Near(float actual, float expected, string reason)
{
    if (Math.Abs(actual - expected) > 0.002f) throw new Exception(reason + $": {actual} != {expected}");
}
foreach (var steps in new[] { 1, 30, 600, 3600 })
{
    var high = new HighSession();
    high.BeginSmoke();
    for (var i = 1; i <= steps; i++)
    {
        high.Credit((float)i / steps, 60f, 3f);
        high.Tick(60f / steps, true);
    }
    Near(high.Remaining, 180f, "Full blunt must earn 180 seconds independent of frame rate");
    high.Credit(1f, 60f, 3f);
    high.Credit(0.2f, 60f, 3f);
    Near(high.Remaining, 180f, "Duplicate or older draw samples must not earn more time");
    high.Tick(150f, false);
    Near(high.Strength, 1f, "Fade starts at final 30 seconds");
    high.Tick(15f, false);
    Near(high.Strength, 0.5f, "After-effect fade");
    high.Tick(100f, false);
    Near(high.Remaining, 0f, "After-effect must expire");
}
{
    var high = new HighSession();
    var plan = new SmokingSession(60f, () => 0.5f);
    high.BeginSmoke();
    high.Credit(plan.BurnProgress(3f), plan.Duration, 3f);
    Near(high.Remaining, 0f, "Raising an unsmoked blunt earns no time");
    high.Credit(plan.BurnProgress(plan.DrawStarts[0] + 2.65f), plan.Duration, 3f);
    Near(high.Remaining, 18f, "Half of first draw earns proportional after-effect time");
    high.Tick(0f, false);
    Near(high.Remaining, 18f, "Paused effects do not count down");
    high.BeginSmoke();
    high.Credit(0.5f, 60f, 3f);
    Near(high.Remaining, 108f, "Another blunt adds to existing time");
    high.Credit(float.NaN, 60f, 3f);
    high.Tick(float.PositiveInfinity, false);
    Near(high.Remaining, 108f, "Invalid timing must not poison state");
    high.Clear();
    Near(high.Remaining, 0f, "Scene/character cleanup clears effects");
    high.BeginSmoke(); high.Credit(1f, 600f, 10f);
    Near(high.Remaining, 1800f, "Repeated smoking has a duration cap");
    high.Clear(); high.BeginSmoke(); high.Credit(1f, 60f, 0f);
    Near(high.Remaining, 0f, "Zero multiplier disables high");
}
Console.WriteLine("PASS: proportional high duration, early cancellation, repeated draws, new sessions, pause, fade, expiry, duration cap and invalid timing.");
for (int duration = 20; duration <= 600; duration++)
foreach (float random in new[] {0f, 0.5f, 1f})
{
    var plan = new SmokingSession(duration, () => random);
    if (Math.Abs(plan.BurnProgress(duration) - 1f) > 0.00001f) throw new Exception("Incomplete burn");
    for (int i = 0; i < plan.DrawStarts.Length; i++)
    {
        var start = plan.DrawStarts[i];
        if (i > 0 && start - plan.DrawStarts[i-1] < 8.3f) throw new Exception("Overlapping draws/exhales");
        if (Math.Abs(plan.BurnProgress(start) - plan.BurnProgress(start + 1.5f)) > 0.00001f) throw new Exception("Burn while raising");
        if (Math.Abs(plan.BurnProgress(start + 3.8f) - plan.BurnProgress(start + 8.3f)) > 0.00001f) throw new Exception("Burn outside inhalation");
        if (Math.Abs(plan.BurnProgress(start + 2.65f) - (i + 0.5f)/plan.DrawStarts.Length) > 0.0001f) throw new Exception("Nonproportional burn");
    }
    if (Math.Abs(plan.DrawStarts[^1] + 8.3f - duration) > 0.0001f) throw new Exception("Wrong session endpoint");
}
foreach (float duration in new[] {float.NaN, float.PositiveInfinity, -100f, 10000f})
{
    var plan = new SmokingSession(duration, () => 0.5f);
    if (!float.IsFinite(plan.Duration) || plan.Duration < 20 || plan.Duration > 600) throw new Exception("Invalid duration");
}
Console.WriteLine("PASS: all durations 20–600, timing extremes, inhalation-only proportional burn, final exhale, invalid config.");

var names = new[] { "inhale_01.wav", "inhale_02.wav", "exhale_01.wav", "exhale_02.wav", "mask_inhale.wav" };
foreach (var name in names)
{
    using var stream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("Cigarette.Audio." + name)!;
    var recording = BreathRecording.Read(stream);
    if (recording.Rate != 48000 || recording.Samples.Length == 0) throw new Exception("Invalid recording");
    foreach (var duration in new[] {1f, 1.75f, 2.3f, 2.5f})
    {
        var output = recording.Excerpt(duration);
        if (output.Length != (int)Math.Round(duration * recording.Rate)) throw new Exception("Excerpt timing");
        if (output[0] != 0 || output[^1] != 0) throw new Exception("Missing fades");
        if (output.Any(x => !float.IsFinite(x) || Math.Abs(x) > 0.3501f)) throw new Exception("Invalid audio amplitude");
        if (output.Max(x => Math.Abs(x)) < 0.1f) throw new Exception("Recording too quiet");
    }
}
using (var invalid = new MemoryStream(new byte[32]))
{
    try { BreathRecording.Read(invalid); throw new Exception("Accepted invalid WAV"); }
    catch (InvalidDataException) { }
}
if (args.Length >= 1)
{
    var packaged = System.Reflection.Assembly.LoadFile(Path.GetFullPath(args[0]));
    if (args.Length >= 2 && packaged.GetName().Version != new Version(args[1] + ".0"))
        throw new Exception("Shipped assembly version does not match release version");
    foreach (var icon in new[] { "cigarette.png", "joint.png", "mask.png" })
    {
        using var stream = packaged.GetManifestResourceStream("Cigarette.Icons." + icon)
            ?? throw new Exception("Missing shipped icon: " + icon);
        var header = new byte[8];
        if (stream.Read(header, 0, 8) != 8 || !header.SequenceEqual(new byte[] {137,80,78,71,13,10,26,10}))
            throw new Exception("Invalid PNG icon: " + icon);
    }
    foreach (var name in names)
    {
        using var embedded = packaged.GetManifestResourceStream("Cigarette.Audio." + name) ?? throw new Exception("Missing shipped audio");
        var recording = BreathRecording.Read(embedded);
        if (recording.Samples.Length == 0) throw new Exception("Empty shipped audio");
    }
}
Console.WriteLine("PASS: five WAVs decoded, timing/fades/amplitude verified, invalid WAV rejected, shipped resources verified when DLL supplied.");

for (var step = 0; step <= 1000; step++)
{
    var phases = HandMotion.Exit(step / 1000f);
    if (phases.Retract > 0f && phases.Lift < 1f) throw new Exception("Retraction before clearance lift");
    if (phases.Lower > 0f && phases.Retract < 0.99999f) throw new Exception("Lowering before retraction");
    var previous = 0f;
    for (var finger = 0; finger < 4; finger++)
    {
        var lift = HandMotion.FingerLift(step / 1000f, finger);
        if (!float.IsFinite(lift) || lift < previous - 0.00001f || lift < 0f || lift > 1f)
            throw new Exception("Invalid finger tap order or amplitude");
        previous = lift;
    }
}
if (HandMotion.FingerLift(-1f, 0) != 0f || HandMotion.FingerLift(1f, 3) != 0f)
    throw new Exception("Finger tap does not return to rest");
Console.WriteLine("PASS: fingers land in order; hands lift and retract completely before lowering.");
