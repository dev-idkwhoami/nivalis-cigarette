namespace NivalisMods.Cigarette;

internal static class SmokingSelection
{
    // Availability is supplied by the same user/API checks used to build the wheel.
    internal static SmokeKind? Choose(SmokeKind remembered, IReadOnlyList<SmokeKind> available)
    {
        foreach (var item in available)
            if (item == remembered) return item;
        return available.Count == 0 ? null : available[0];
    }
}
