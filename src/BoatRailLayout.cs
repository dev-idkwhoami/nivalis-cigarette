namespace NivalisMods.Cigarette;

internal sealed record BoatRailBox(string Name, float MinX, float MinY, float MinZ,
    float MaxX, float MaxY, float MaxZ);

internal static class BoatRailLayout
{
    // Mesh-local coordinates, measured from the top faces of combined_combine_Boat.
    // The model transform supplies the native 0.24 scale and boat motion.
    internal static readonly BoatRailBox[] Boxes =
    {
        new("Rear", -29f, 6.75f, -7.625f, -28.75f, 6.875f, 12f),
        new("SideLong", -28.625f, 6.5f, 12.125f, -13.25f, 6.875f, 12.375f),
        new("SideShort", -28.625f, 6.75f, -8f, -24.5f, 6.875f, -7.75f)
    };
}
