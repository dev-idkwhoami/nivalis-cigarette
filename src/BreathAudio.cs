using System.Reflection;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using Object = UnityEngine.Object;

namespace NivalisMods.Cigarette;

internal sealed class BreathAudio
{
    private static readonly Dictionary<string, BreathRecording> Recordings = new();
    private readonly GameObject _object;
    private readonly AudioSource _source;
    private AudioClip? _clip;
    private bool _failed;
    private readonly bool _mask;

    internal static void LoadRecordings()
    {
        foreach (var name in new[] { "inhale_01.wav", "inhale_02.wav", "exhale_01.wav", "exhale_02.wav", "mask_inhale.wav" })
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Cigarette.Audio." + name)
                ?? throw new InvalidDataException("Missing embedded recording: " + name);
            Recordings[name] = BreathRecording.Read(stream);
        }
    }

    internal BreathAudio(Transform eye, bool mask = false)
    {
        _mask = mask;
        _object = new GameObject("Cigarette.BreathAudio");
        try
        {
            _object.transform.SetParent(eye, false);
            _source = _object.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.loop = false;
            _source.spatialBlend = 0f;
            _source.pitch = 1f;
            _source.ignoreListenerPause = false;
        }
        catch { Object.Destroy(_object); throw; }
    }

    internal void Play(bool inhale, float duration, float elapsed = 0f)
    {
        if (_failed || elapsed >= duration) return;
        try
        {
            Stop();
            var maskInhale = _mask && inhale;
            var volume = maskInhale ? Plugin.MaskInhaleVolume.Value : inhale ? Plugin.InhaleVolume.Value : Plugin.ExhaleVolume.Value;
            var defaultVolume = maskInhale ? Plugin.DefaultMaskInhaleVolume : inhale ? Plugin.DefaultInhaleVolume : Plugin.DefaultExhaleVolume;
            _source.volume = float.IsFinite(volume) ? Mathf.Clamp01(volume) : defaultVolume;
            if (_source.volume == 0f) return;
            var name = maskInhale ? "mask_inhale.wav" : (inhale ? "inhale_" : "exhale_") + (UnityEngine.Random.Range(0, 2) + 1).ToString("00") + ".wav";
            if (!Recordings.TryGetValue(name, out var recording))
                throw new InvalidDataException("Bundled recording was not loaded: " + name);
            var data = recording.Excerpt(duration);
            _clip = AudioClip.Create("Cigarette." + name, data.Length, 1, recording.Rate, false);
            if (!_clip.SetData(new Il2CppStructArray<float>(data), 0)) throw new InvalidOperationException("AudioClip.SetData failed.");
            _source.clip = _clip;
            // A late frame joins the current breath rather than playing beyond its animation.
            _source.timeSamples = Math.Min(data.Length - 1, Math.Max(0, (int)(elapsed * recording.Rate)));
            _source.Play();
        }
        catch (Exception e)
        {
            _failed = true;
            Stop();
            Plugin.Logger.LogWarning("Breath audio disabled for this session: " + e.Message);
        }
    }

    internal void Stop()
    {
        if (_source != null) { _source.Stop(); _source.clip = null; }
        if (_clip != null) Object.Destroy(_clip);
        _clip = null;
    }

    internal void Dispose()
    {
        Stop();
        if (_object != null) Object.Destroy(_object);
    }
}
