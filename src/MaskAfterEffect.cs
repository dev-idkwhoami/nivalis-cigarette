using Nivalis;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NivalisMods.Cigarette;

internal sealed class MaskAfterEffect
{
    private readonly HighSession _session = new();
    private readonly MaskSlowMotion _slow = new();
    private int _scene, _player;
    private float _fade;

    internal bool OwnsSlowMotion => _slow.OwnsSlowMotion(Time.timeScale);

    internal void BeginSmoke()
    {
        var player = PlayerManager._instance?.LocalPlayer?.Character;
        if (_scene != SceneManager.GetActiveScene().handle || _player != player?.GetInstanceID()) Clear();
        _scene = SceneManager.GetActiveScene().handle;
        _player = player?.GetInstanceID() ?? 0;
        _session.BeginSmoke();
        Suspend();
    }

    internal void Credit(float burn, float duration)
    {
        if (_scene == SceneManager.GetActiveScene().handle &&
            _player == PlayerManager._instance?.LocalPlayer?.Character?.GetInstanceID())
            _session.Credit(burn, duration, Plugin.MaskDurationMultiplier.Value);
    }

    internal void Tick(bool gameplay, bool holdingSmoke)
    {
        if (_session.Remaining <= 0f) { Suspend(); return; }
        if (_scene != SceneManager.GetActiveScene().handle ||
            _player != PlayerManager._instance?.LocalPlayer?.Character?.GetInstanceID())
        { Clear(); return; }
        if (!gameplay || holdingSmoke) { Suspend(); return; }
        // Real seconds keep the duration independent of the world speed selected.
        _session.Tick(Time.unscaledDeltaTime, false);
        _fade = Mathf.Min(1f, _fade + Time.unscaledDeltaTime / 0.4f);
        var strength = Mathf.Min(_fade, _session.Remaining / 0.4f);
        strength = strength * strength * (3f - 2f * strength);
        SetTime(_slow.Apply(Time.timeScale, Time.fixedDeltaTime, strength, Plugin.MaskWorldSpeed.Value));
    }

    internal void Suspend()
    {
        _fade = 0f;
        SetTime(_slow.Apply(Time.timeScale, Time.fixedDeltaTime, 0f, 1f));
    }

    internal void Clear() { _session.Clear(); Suspend(); }

    private static void SetTime((float Scale, float Step) value)
    {
        if (Time.timeScale != value.Scale) Time.timeScale = value.Scale;
        if (Time.fixedDeltaTime != value.Step) Time.fixedDeltaTime = value.Step;
    }
}
