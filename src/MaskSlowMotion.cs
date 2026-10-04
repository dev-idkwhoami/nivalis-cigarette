namespace NivalisMods.Cigarette;

// Own only the values we wrote. A pause, cutscene or another mod takes priority.
internal sealed class MaskSlowMotion
{
    private bool _owned, _interrupted;
    private float _baseScale, _baseStep, _lastScale, _lastStep;

    internal bool OwnsSlowMotion(float scale) =>
        _owned && scale > 0f && scale < 1f && Same(scale, _lastScale);

    internal (float Scale, float Step) Apply(float scale, float step, float strength, float speed)
    {
        if (_owned && (!Same(scale, _lastScale) || !Same(step, _lastStep)))
        {
            var restored = Release(scale, step);
            _interrupted = strength > 0f;
            return restored;
        }
        if (strength <= 0f)
        {
            _interrupted = false;
            return Release(scale, step);
        }
        if (_interrupted || scale <= 0f) return (scale, step);
        if (!_owned)
        {
            _baseScale = scale;
            _baseStep = step;
            _owned = true;
        }
        speed = float.IsFinite(speed) ? Math.Clamp(speed, 0.1f, 1f) : 0.5f;
        var multiplier = 1f + (speed - 1f) * Math.Clamp(strength, 0f, 1f);
        _lastScale = _baseScale * multiplier;
        _lastStep = _baseStep * multiplier;
        return (_lastScale, _lastStep);
    }

    internal (float Scale, float Step) Release(float scale, float step)
    {
        if (!_owned) return (scale, step);
        _owned = false;
        return (Same(scale, _lastScale) ? _baseScale : scale,
            Same(step, _lastStep) ? _baseStep : step);
    }

    private static bool Same(float a, float b) => Math.Abs(a - b) < 0.000001f;
}
