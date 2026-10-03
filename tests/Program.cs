using NivalisMods.Cigarette;
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

var names = new[] { "inhale_01.wav", "inhale_02.wav", "exhale_01.wav", "exhale_02.wav" };
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
    foreach (var name in names)
    {
        using var embedded = packaged.GetManifestResourceStream("Cigarette.Audio." + name) ?? throw new Exception("Missing shipped audio");
        var recording = BreathRecording.Read(embedded);
        if (recording.Samples.Length == 0) throw new Exception("Empty shipped audio");
    }
}
Console.WriteLine("PASS: four WAVs decoded, timing/fades/amplitude verified, invalid WAV rejected, shipped resources verified when DLL supplied.");

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
