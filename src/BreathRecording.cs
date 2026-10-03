using System.Text;

namespace NivalisMods.Cigarette;

// PCM decoding and excerpt preparation remain independent of Unity for validation.
internal sealed record BreathRecording(int Rate, float[] Samples)
{
    internal static BreathRecording Read(Stream stream)
    {
        using var reader = new BinaryReader(stream, Encoding.ASCII, true);
        if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "RIFF") throw new InvalidDataException("Expected RIFF WAV.");
        reader.ReadUInt32();
        if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "WAVE") throw new InvalidDataException("Expected WAVE.");
        int rate = 0, channels = 0, bits = 0, format = 0;
        byte[]? data = null;
        while (stream.Position + 8 <= stream.Length)
        {
            var tag = Encoding.ASCII.GetString(reader.ReadBytes(4));
            var size = reader.ReadUInt32();
            var end = stream.Position + size;
            if (end > stream.Length) throw new InvalidDataException("Truncated WAV chunk.");
            if (tag == "fmt ")
            {
                if (size < 16) throw new InvalidDataException("Invalid WAV format chunk.");
                format = reader.ReadUInt16(); channels = reader.ReadUInt16(); rate = reader.ReadInt32();
                reader.ReadUInt32(); reader.ReadUInt16(); bits = reader.ReadUInt16();
            }
            else if (tag == "data") data = reader.ReadBytes(checked((int)size));
            stream.Position = end + (size & 1);
        }
        if (format != 1 || channels != 1 || bits != 16 || rate < 8000 || rate > 192000 || data == null || data.Length == 0 || data.Length % 2 != 0)
            throw new InvalidDataException("Breath recording must be mono, 16-bit PCM WAV.");
        var samples = new float[data.Length / 2];
        for (var i = 0; i < samples.Length; i++)
            samples[i] = (short)(data[i * 2] | (data[i * 2 + 1] << 8)) / 32768f;
        return new BreathRecording(rate, samples);
    }

    internal float[] Excerpt(float duration)
    {
        if (!float.IsFinite(duration) || duration <= 0f || duration > 10f) throw new ArgumentOutOfRangeException(nameof(duration));
        var count = Math.Max(1, (int)Math.Round(duration * Rate));
        var window = Math.Min(count, Samples.Length);
        double energy = 0;
        for (var i = 0; i < window; i++) energy += Samples[i] * Samples[i];
        var bestEnergy = energy;
        var start = 0;
        for (var i = window; i < Samples.Length; i++)
        {
            energy += Samples[i] * Samples[i] - Samples[i - window] * Samples[i - window];
            if (energy > bestEnergy) { bestEnergy = energy; start = i - window + 1; }
        }
        var peak = 0f;
        for (var i = 0; i < window; i++) peak = Math.Max(peak, Math.Abs(Samples[start + i]));
        var gain = peak > 0.00001f ? Math.Min(256f, 0.35f / peak) : 1f;
        var output = new float[count];
        for (var i = 0; i < window; i++)
        {
            var fade = Math.Min(1f, Math.Min(i / (Rate * 0.08f), (window - 1 - i) / (Rate * 0.15f)));
            output[i] = Samples[start + i] * gain * fade;
        }
        return output;
    }
}
