using UnityEngine;

namespace NivalisMods.Cigarette;

internal static class MaskModel
{
    private const string AssetName = "Purple_vape_Purple_vape_0";
    // Native mesh is authored at 1/0.035 scale, with the face opening along +Y
    // and the canister along -Y. Normalize to opening -Z, canister +Z, nose +Y.
    internal static readonly Vector3 MouthPoint = new(0f, 0f, -0.070f);
    // Stem is offset toward the nose in the native mesh, not centered on the bowl.
    // Anchor at its junction with the rounded underside, rather than down the stem.
    internal static readonly Vector3 GripPoint = new(0f, 0.02625f, -0.00875f);
    private static Mesh? _mesh;
    private static Material? _material;

    internal static bool Available()
    {
        if (_mesh == null) _mesh = Resources.FindObjectsOfTypeAll<Mesh>().FirstOrDefault(m => m.name == AssetName);
        if (_material == null) _material = Resources.FindObjectsOfTypeAll<Material>().FirstOrDefault(m => m.name == AssetName);
        return _mesh != null && _material != null;
    }

    internal static void Create(Transform parent, int layer)
    {
        if (!Available()) throw new InvalidOperationException("City inhalant-mask assets are not loaded here. Try a populated street.");
        var model = new GameObject("Cigarette.InhalantMask") { layer = layer };
        model.transform.SetParent(parent, false);
        model.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
        model.transform.localScale = Vector3.one * 0.035f;
        model.AddComponent<MeshFilter>().sharedMesh = _mesh;
        model.AddComponent<MeshRenderer>().sharedMaterial = _material;
        // Borrow only native geometry/material. No NPC logic, colliders, or smoke
        // emitter is cloned. Destroying the prop must never destroy these assets.
    }
}
