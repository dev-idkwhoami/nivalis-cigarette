namespace NivalisMods.Cigarette;

// Only newly consumed draws earn time. Holding, lowering and menus never do.
internal sealed class HighSession
{
    private double _creditedBurn, _remaining;
    internal float Remaining => (float)_remaining;
    internal float Strength => Math.Clamp(Remaining / 30f, 0f, 1f);

    internal void BeginSmoke() => _creditedBurn = 0f;

    internal void Credit(float burn, float duration, float multiplier)
    {
        if (!float.IsFinite(burn) || !float.IsFinite(duration) || duration <= 0f) return;
        burn = Math.Clamp(burn, 0f, 1f);
        var added = Math.Max(0f, burn - _creditedBurn);
        _creditedBurn = Math.Max(_creditedBurn, burn);
        multiplier = float.IsFinite(multiplier) ? Math.Clamp(multiplier, 0f, 10f) : 3f;
        _remaining = Math.Min(1800d, _remaining + added * duration * multiplier);
    }

    internal void Tick(float delta, bool smoking)
    {
        if (!smoking && float.IsFinite(delta) && delta > 0f)
            _remaining = Math.Max(0d, _remaining - delta);
    }

    internal void Clear() { _remaining = 0d; _creditedBurn = 0d; }
}
