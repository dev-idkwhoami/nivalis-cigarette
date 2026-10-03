using UnityEngine;

namespace NivalisMods.Cigarette;

internal sealed class SupportHandPose
{
    private readonly List<(Transform Bone, Quaternion Resting, Quaternion Lifted, int Finger)> _joints = new();
    private readonly List<(int Joint, Vector3 Axis, Quaternion Neutral, float Curl)> _gripJoints = new();
    private readonly List<(int Joint, Quaternion Rotation)> _flatProximals = new();
    private readonly List<Vector3> _pads = new();
    private readonly Transform _hand;
    private readonly Vector3 _palm;
    private bool _canTap;
    private float _nextTap = Time.time + UnityEngine.Random.Range(4f, 9f);
    private float _tapStarted = float.NegativeInfinity;
    internal Vector3 ContactOffset { get; private set; }

    internal SupportHandPose(Animator animator, Quaternion rotation)
    {
        var hand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
        _hand = hand;
        Transform Bone(string finger, string joint) => animator.GetBoneTransform(
            Enum.Parse<HumanBodyBones>("Left" + finger + joint));
        var index = Bone("Index", "Proximal");
        var little = Bone("Little", "Proximal");
        var normal = Vector3.Cross(index.position - hand.position, little.position - hand.position).normalized;
        var middle = Bone("Middle", "Proximal");
        var fingerForward = Vector3.ProjectOnPlane(Bone("Middle", "Intermediate").position - middle.position, normal).normalized;
        _palm = hand.InverseTransformPoint(Vector3.Lerp(hand.position, middle.position, 0.65f) + normal * 0.012f);
        var heel = hand.InverseTransformPoint(Vector3.Lerp(hand.position, Bone("Thumb", "Proximal").position, 0.45f) + normal * 0.01f);
        var heelHeight = (rotation * heel).y;
        var lowest = heelHeight;
        // Left-to-right from the player's view: little, ring, middle, index. Thumb stays planted.
        var fingers = new[] { "Little", "Ring", "Middle", "Index", "Thumb" };
        for (var finger = 0; finger < fingers.Length; finger++)
        {
            var proximal = Bone(fingers[finger], "Proximal");
            var intermediate = Bone(fingers[finger], "Intermediate");
            var distal = Bone(fingers[finger], "Distal");
            if (finger < 4)
            {
                // Reduce splay around the palm normal without changing the finger's curl.
                var direction = Vector3.ProjectOnPlane(intermediate.position - proximal.position, normal).normalized;
                proximal.rotation = Quaternion.AngleAxis(Vector3.SignedAngle(direction, fingerForward, normal) * 0.8f, normal) * proximal.rotation;
            }
            var original = proximal.localRotation;
            var flat = original;
            if (finger < 4)
            {
                var direction = intermediate.position - proximal.position;
                proximal.rotation = Quaternion.FromToRotation(direction, Vector3.ProjectOnPlane(direction, normal)) * proximal.rotation;
                flat = proximal.localRotation;
                proximal.localRotation = original;
            }
            var axis = proximal.InverseTransformDirection(Vector3.Cross(intermediate.position - proximal.position, normal).normalized);
            Bend(intermediate, distal.position - intermediate.position, finger == 4 ? 12f : 32f, finger, 35f);
            Bend(distal, distal.position - intermediate.position, finger == 4 ? 5f : 10f, finger, 30f);
            // Arch each finger, then place its estimated pad on the same plane as the thumb heel.
            var best = original;
            var error = float.PositiveInfinity;
            for (var degrees = -35; degrees <= 35; degrees++)
            {
                proximal.localRotation = original * Quaternion.AngleAxis(degrees, axis);
                var difference = Mathf.Abs(PadHeight() - heelHeight);
                if (difference < error) { error = difference; best = proximal.localRotation; }
            }
            proximal.localRotation = best;
            _flatProximals.Add((_joints.Count, flat));
            _joints.Add((proximal, best, best * Quaternion.AngleAxis(-9f, axis), finger));
            lowest = Mathf.Min(lowest, PadHeight());
            if (finger < 4)
                _pads.Add(hand.InverseTransformPoint(distal.position + (distal.position - intermediate.position) * 0.25f));

            float PadHeight()
            {
                var pad = distal.position + (distal.position - intermediate.position) * 0.25f;
                return (rotation * hand.InverseTransformPoint(pad)).y - 0.006f;
            }
        }
        var transformedHeel = rotation * heel;
        ContactOffset = new Vector3(-transformedHeel.x, 0.003f - lowest, -transformedHeel.z);

        void Bend(Transform bone, Vector3 direction, float degrees, int finger, float gripCurl)
        {
            var axis = bone.InverseTransformDirection(Vector3.Cross(direction, normal).normalized);
            var neutral = bone.localRotation;
            var rest = neutral * Quaternion.AngleAxis(degrees, axis);
            bone.localRotation = rest;
            if (finger < 4) _gripJoints.Add((_joints.Count, axis, neutral, degrees + gripCurl));
            _joints.Add((bone, rest, rest * Quaternion.AngleAxis(-4f, axis), finger));
        }
    }

    internal void FitSurface(Quaternion rotation, Vector3 forward, float contactDepth, float nearEdge, float farEdge)
    {
        // Require both a useful top depth and support underneath every resting fingertip.
        float Depth(Vector3 local) => contactDepth + Vector3.Dot(ContactOffset + rotation * local, forward);
        _canTap = farEdge - nearEdge >= 0.16f &&
            _pads.All(pad => Depth(pad) >= nearEdge + 0.005f && Depth(pad) <= farEdge - 0.005f);
        if (_canTap) return;

        // A gripping hand rests on the palm, not the cupped pose's thumb heel.
        // Put that palm over the center of the measured top and start the fingers flat.
        ContactOffset = -(rotation * _palm) + Vector3.up * 0.003f +
            forward * ((nearEdge + farEdge) * 0.5f - contactDepth);
        foreach (var proximal in _flatProximals) SetGrip(proximal.Joint, proximal.Rotation);
        foreach (var grip in _gripJoints) SetGrip(grip.Joint, grip.Neutral);

        // Solve knuckles before tips, curling only after a joint clears the far edge.
        foreach (var grip in _gripJoints)
        {
            var joint = _joints[grip.Joint];
            var clearance = Depth(_hand.InverseTransformPoint(joint.Bone.position)) - farEdge;
            SetGrip(grip.Joint, grip.Neutral * Quaternion.AngleAxis(
                grip.Curl * Mathf.Clamp01((clearance - 0.005f) / 0.02f), grip.Axis));
        }

        void SetGrip(int index, Quaternion rotation)
        {
            var joint = _joints[index];
            _joints[index] = (joint.Bone, rotation, rotation, joint.Finger);
            joint.Bone.localRotation = rotation;
        }
    }

    internal void Apply(bool resting, bool exiting)
    {
        if (_canTap && !exiting && resting && Time.time >= _nextTap)
        {
            _tapStarted = Time.time;
            _nextTap = Time.time + UnityEngine.Random.Range(6f, 13f);
        }
        foreach (var joint in _joints)
        {
            var lift = _canTap && !exiting && joint.Finger < 4 ? HandMotion.FingerLift(Time.time - _tapStarted, joint.Finger) : 0f;
            joint.Bone.localRotation = Quaternion.Slerp(joint.Resting, joint.Lifted, lift);
        }
    }
}
