using UnityEngine;
using NVector = System.Numerics.Vector3;
using NRotation = System.Numerics.Quaternion;

namespace NivalisMods.Cigarette;

internal static class MaskFingerPose
{
    private static int _cachedHand, _cachedMesh;
    private static Quaternion[]? _cachedRotations;
    private static Vector3 _cachedPosition;
    private static GripSurface? _surface;
    private static int _surfaceMesh;

    internal static void Fit(Animator animator, Transform prop)
    {
        var hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
        var meshFilter = prop.GetComponentInChildren<MeshFilter>();
        var mesh = meshFilter.sharedMesh;
        var bones = new List<Transform>();
        foreach (var finger in new[] { "Index", "Middle", "Ring", "Little", "Thumb" })
        foreach (var joint in new[] { "Proximal", "Intermediate", "Distal" })
            bones.Add(animator.GetBoneTransform(Enum.Parse<HumanBodyBones>("Right" + finger + joint)));
        if (_cachedRotations != null && _cachedHand == hand.GetInstanceID() && _cachedMesh == mesh.GetInstanceID())
        {
            for (var i = 0; i < bones.Count; i++) bones[i].localRotation = _cachedRotations[i];
            prop.localPosition = _cachedPosition;
            return;
        }
        if (_surface == null || _surfaceMesh != mesh.GetInstanceID())
        {
            if (!mesh.isReadable) throw new InvalidOperationException("Mask mesh is not readable; cannot safely fit fingers.");
            var vertices = mesh.vertices.Select(v => V(prop.InverseTransformPoint(meshFilter.transform.TransformPoint(v)))).ToArray();
            _surface = new GripSurface(vertices, mesh.triangles.ToArray(), -0.012f);
            _surfaceMesh = mesh.GetInstanceID();
        }
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var original = bones.Select(b => b.localRotation).ToArray();
        var originalPosition = prop.localPosition;
        var palm = -(prop.rotation * Vector3.forward);
        var worst = float.PositiveInfinity;
        var clearance = 0f;
        // If the shell already intersects a knuckle, curls alone cannot fix it.
        // Find the smallest extra palm clearance, while the face target stays fixed.
        for (var attempt = 0; attempt <= 12; attempt++)
        {
            clearance = attempt * 0.003f;
            prop.localPosition = originalPosition - prop.localRotation * Vector3.forward * clearance;
            for (var i = 0; i < bones.Count; i++) bones[i].localRotation = original[i];
            worst = 0f;
            for (var finger = 0; finger < 5; finger++)
            {
                var first = bones[finger * 3]; var middle = bones[finger * 3 + 1]; var tip = bones[finger * 3 + 2];
                var firstLink = first.InverseTransformPoint(middle.position);
                var secondLink = middle.InverseTransformPoint(tip.position);
                // Distal segment continues along the same anatomical bone axis,
                // but follows the distal rotation, including its own curl.
                var fit = new FingerFit
                {
                    Root = V(prop.InverseTransformPoint(first.position)),
                    RootRotation = Q(Quaternion.Inverse(prop.rotation) * first.rotation),
                    MiddleRotation = Q(middle.localRotation), TipRotation = Q(tip.localRotation),
                    FirstLink = V(firstLink), SecondLink = V(secondLink), TipLink = V(secondLink * 0.8f),
                    RootCurl = Axis(first, middle.position - first.position),
                    MiddleCurl = Axis(middle, tip.position - middle.position),
                    TipCurl = Axis(tip, tip.TransformDirection(secondLink)),
                    Splay = V(first.InverseTransformDirection(palm)), SplayLimit = finger == 4 ? 35f : 18f
                };
                var angles = fit.Solve(_surface);
                worst = Math.Max(worst, fit.Penetration(_surface, angles));
                first.localRotation = original[finger * 3] * U(FingerFit.Turn(fit.Splay, angles[3])) * U(FingerFit.Turn(fit.RootCurl, angles[0]));
                middle.localRotation = original[finger * 3 + 1] * U(FingerFit.Turn(fit.MiddleCurl, angles[1]));
                tip.localRotation = original[finger * 3 + 2] * U(FingerFit.Turn(fit.TipCurl, angles[2]));
            }
            if (worst <= 0.001f) break;
        }
        if (worst > 0.001f)
        {
            for (var i = 0; i < bones.Count; i++) bones[i].localRotation = original[i];
            prop.localPosition = originalPosition;
            Plugin.Logger.LogWarning($"Mask fit could not clear every pad ({worst * 1000f:F1} mm); using the previous grip. Animation continues.");
        }
        _cachedHand = hand.GetInstanceID(); _cachedMesh = mesh.GetInstanceID();
        _cachedRotations = bones.Select(b => b.localRotation).ToArray();
        _cachedPosition = prop.localPosition;
        if (worst <= 0.001f) Plugin.Logger.LogInfo($"Mask fingers fitted to native shell: {timer.ElapsedMilliseconds} ms; palm clearance {clearance * 1000f:F1} mm; maximum sampled overlap {worst * 1000f:F2} mm. Pose cached for this hand rig.");

        NVector Axis(Transform bone, Vector3 direction)
        {
            var axis = Vector3.Cross(direction, palm);
            if (axis.sqrMagnitude < 0.000001f) axis = Vector3.Cross(direction, prop.up);
            return V(bone.InverseTransformDirection(axis.normalized));
        }
    }

    private static NVector V(Vector3 v) => new(v.x, v.y, v.z);
    private static NRotation Q(Quaternion q) => new(q.x, q.y, q.z, q.w);
    private static Quaternion U(NRotation q) => new(q.X, q.Y, q.Z, q.W);
}
