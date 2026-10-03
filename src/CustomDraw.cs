using Nivalis;
using RootMotion.FinalIK;
using UnityEngine;

namespace NivalisMods.Cigarette;

// A procedural first-person animation: no NPC smoking clip is sampled.
internal sealed class CustomDraw
{
    private readonly ArmDriver _arm, _supportArm;
    private readonly SupportHandPose _supportPose;
    private readonly Vector3 _bodyOrigin;
    private float _railNearDepth;
    private Vector3 _exitRetract, _exitSupportRetract;
    private readonly Transform? _shoulder, _supportShoulder;
    private readonly Vector3 _supportShoulderPosition;
    private readonly Quaternion _supportShoulderRotation;
    private readonly Vector3 _restShoulderOffset, _restSupportShoulderOffset;
    private Vector3 _exitShoulderPosition, _exitSupportShoulderPosition;
    private readonly Vector3 _shoulderPosition;
    private readonly Quaternion _shoulderRotation;
    private readonly Vector3 _eyeAtStart;
    private readonly float _side;
    private readonly Vector3 _supportPosition;
    private readonly Quaternion _supportRotation;
    private readonly Transform _hand, _forearm, _eye, _cigarette;
    private readonly Quaternion _handFrame;
    private readonly Quaternion _bodyRotation;
    private readonly Vector3 _restPosition;
    private readonly Quaternion _restRotation;
    private readonly List<(Transform Bone, Quaternion Relaxed, Quaternion Draw)> _grip = new();
    private readonly float _start;
    private readonly SmokingSession _session;
    private int _drawIndex;
    private bool _exiting;
    private float _exitStarted, _exitBurn;
    private Vector3 _exitHand, _exitSupport;
    private Quaternion _exitHandRotation, _exitSupportRotation;
    private readonly Transform _supportHand;
    internal float BurnProgress => _exiting ? _exitBurn : _session.BurnProgress(Time.time - _start);
    internal float ExitProgress => _exiting ? Mathf.Clamp01((Time.unscaledTime - _exitStarted) / 0.85f) : 0f;
    private ExhaleSmoke? _smoke;
    private bool _smokeUnavailable;
    private bool _exhaled, _inhaled;
    private BreathAudio? _audio;
    private float _exhaleDelay = UnityEngine.Random.Range(1f, 2f);
#if CONTACT_DIAGNOSTICS
    private bool _contactReported;
#endif
    private float _exhaleDuration = UnityEngine.Random.Range(1f, 2.5f);

    internal CustomDraw(PlayerHandsAnimator hands, Transform eye, Transform cigarette, Quaternion bodyRotation, PlayerCharacter character, SmokingSession session, RaycastHit railTop)
    {
        _session = session;
        _bodyOrigin = character.transform.position;
        _railNearDepth = Vector3.Dot(railTop.point - _bodyOrigin, bodyRotation * Vector3.forward);
        var left = false;
        _arm = new ArmDriver((left ? hands.leftHandIK : hands.rightHandIK).solver, hands.Animator, left);
        _supportArm = new ArmDriver((left ? hands.rightHandIK : hands.leftHandIK).solver, hands.Animator, !left);
        _shoulder = hands.Animator.GetBoneTransform(left ? HumanBodyBones.LeftShoulder : HumanBodyBones.RightShoulder);
        if (_shoulder != null)
        {
            _shoulderPosition = _shoulder.position - bodyRotation * Vector3.forward * 0.08f;
            _shoulderRotation = _shoulder.rotation;
            _shoulder.SetPositionAndRotation(_shoulderPosition, _shoulderRotation);
        }
        _supportShoulder = hands.Animator.GetBoneTransform(left ? HumanBodyBones.RightShoulder : HumanBodyBones.LeftShoulder);
        if (_supportShoulder != null)
        {
            _supportShoulderPosition = _supportShoulder.position;
            _supportShoulderRotation = _supportShoulder.rotation;
        }
        _side = left ? -1f : 1f;
        _eyeAtStart = eye.position;
        _hand = hands.Animator.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
        _forearm = hands.Animator.GetBoneTransform(left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
        _supportHand = hands.Animator.GetBoneTransform(left ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand);
        _eye = eye;
        _cigarette = cigarette;
        _bodyRotation = bodyRotation;
        BuildGrip(hands.Animator, left);
        ApplyGrip(0f);
        CloseCigaretteGrip(hands.Animator, left);
        var index = hands.Animator.GetBoneTransform(left ? HumanBodyBones.LeftIndexIntermediate : HumanBodyBones.RightIndexIntermediate);
        var middle = hands.Animator.GetBoneTransform(left ? HumanBodyBones.LeftMiddleIntermediate : HumanBodyBones.RightMiddleIntermediate);
        var indexDistal = hands.Animator.GetBoneTransform(left ? HumanBodyBones.LeftIndexDistal : HumanBodyBones.RightIndexDistal);
        var middleDistal = hands.Animator.GetBoneTransform(left ? HumanBodyBones.LeftMiddleDistal : HumanBodyBones.RightMiddleDistal);
        // Distal bones start at the final knuckle. Move onto the fingertip pads.
        var gripPoint = (indexDistal.position + (indexDistal.position - index.position) * 0.25f +
            middleDistal.position + (middleDistal.position - middle.position) * 0.25f) * 0.5f;
        // Calibrate wrist orientation from actual hand bones, not the cigarette object's arbitrary roll.
        var handFrame = HandFrame(hands.Animator, left);
        _handFrame = handFrame;
        var desiredRest = Quaternion.LookRotation(bodyRotation * Vector3.forward, Vector3.down);
        _restRotation = Quaternion.LookRotation(bodyRotation * Vector3.forward, -_side * (bodyRotation * Vector3.right)) * Quaternion.Inverse(handFrame);
        _restPosition = eye.position + bodyRotation * new Vector3(_side * 0.24f, -0.36f, 0.34f);
        var supportFrame = HandFrame(hands.Animator, !left);
        _supportRotation = desiredRest * Quaternion.Inverse(supportFrame);
        _supportPosition = eye.position + bodyRotation * new Vector3(-_side * 0.24f, -0.46f, 0.28f);
        // Anchor both hands to the selected rail. A miss on one side must not discard the other contact.
        var wristOffset = Vector3.up * 0.045f;
        _supportPose = new SupportHandPose(hands.Animator, _supportRotation);
        var palmOffset = _supportPose.ContactOffset;
        if (TryRailTop(character, hands, bodyRotation, _side * 0.22f, railTop, _arm, _shoulder != null, true, wristOffset, out var restContact, out var rightNearEdge, out _))
        {
            _railNearDepth = Mathf.Min(_railNearDepth, rightNearEdge);
            _restPosition = restContact + wristOffset;
            if (_shoulder != null) _restShoulderOffset = _arm.ShoulderOffsetFor(_restPosition);
            _restRotation = Quaternion.AngleAxis(12f, bodyRotation * Vector3.right) * _restRotation;
        }
        if (TryRailTop(character, hands, bodyRotation, -_side * 0.22f, railTop, _supportArm, _supportShoulder != null, false, palmOffset, out var supportContact, out var leftNearEdge, out var leftFarEdge))
        {
            _supportPose.FitSurface(_supportRotation, bodyRotation * Vector3.forward,
                Vector3.Dot(supportContact - _bodyOrigin, bodyRotation * Vector3.forward), leftNearEdge, leftFarEdge);
            _railNearDepth = Mathf.Min(_railNearDepth, leftNearEdge);
            _supportPosition = supportContact + _supportPose.ContactOffset;
            if (_supportShoulder != null) _restSupportShoulderOffset = _supportArm.ShoulderOffsetFor(_supportPosition);
        }
        // Choose the cigarette roll so fingers run sideways at the mouth, instead of bending the wrist backwards.
        var frameWorld = _hand.rotation * handFrame;
        _cigarette.rotation = Quaternion.LookRotation(-(frameWorld * Vector3.up), frameWorld * Vector3.forward);
        // Hold near the filter's mouth end: about 3 mm projects past the inner finger surface.
        _cigarette.position = gripPoint + _cigarette.forward * 0.010f;
        try { _audio = new BreathAudio(eye); }
        catch (Exception e) { Plugin.Logger.LogWarning("Breath audio unavailable: " + e.Message); }
        _start = Time.time;
    }

    private void BuildGrip(Animator animator, bool left)
    {
        Transform Bone(string finger, string joint) => animator.GetBoneTransform(Enum.Parse<HumanBodyBones>((left ? "Left" : "Right") + finger + joint));
        var palmNormal = PalmNormal(animator, left);
        foreach (var (finger, first, second) in new[] { ("Index", 12f, 12f), ("Middle", 18f, 18f), ("Ring", 50f, 65f), ("Little", 60f, 70f), ("Thumb", 30f, 35f) })
        {
            var proximal = Bone(finger, "Proximal");
            var intermediate = Bone(finger, "Intermediate");
            var distal = Bone(finger, "Distal");
            var holdsCigarette = finger == "Index" || finger == "Middle";
            // Thumb opposition moves across the palm toward the folded ring finger as well as inward.
            var curlDirection = finger == "Thumb"
                ? (Bone("Ring", "Proximal").position - proximal.position + palmNormal * 0.035f).normalized
                : palmNormal;
            Add(proximal, intermediate, holdsCigarette ? first : 4f, first, curlDirection);
            Add(intermediate, distal, holdsCigarette ? second : 6f, second, curlDirection);
        }
        void Add(Transform bone, Transform child, float restAngle, float drawAngle, Vector3 curlDirection)
        {
            var axis = bone.InverseTransformDirection(Vector3.Cross(child.position - bone.position, curlDirection).normalized);
            _grip.Add((bone, bone.localRotation * Quaternion.AngleAxis(restAngle, axis),
                bone.localRotation * Quaternion.AngleAxis(drawAngle, axis)));
        }
    }

    private void CloseCigaretteGrip(Animator animator, bool left)
    {
        Transform Bone(string finger, string joint) => animator.GetBoneTransform(Enum.Parse<HumanBodyBones>((left ? "Left" : "Right") + finger + joint));
        var index = Bone("Index", "Proximal");
        var middle = Bone("Middle", "Proximal");
        Vector3 Pad(string finger)
        {
            var distal = Bone(finger, "Distal").position;
            return distal + (distal - Bone(finger, "Intermediate").position) * 0.25f;
        }
        // 7 mm cigarette plus approximately 7 mm from each pad surface to its bone axis.
        // Close lateral splay at the knuckles, retaining the previously calibrated finger curls.
        const float padCenterSpacing = 0.021f;
        for (var iteration = 0; iteration < 8; iteration++)
        {
            var a = Pad("Index");
            var b = Pad("Middle");
            var separation = b - a;
            if (separation.magnitude <= padCenterSpacing + 0.0002f) break;
            var center = (a + b) * 0.5f;
            var halfGap = separation.normalized * (padCenterSpacing * 0.5f);
            index.rotation = Quaternion.FromToRotation(a - index.position, center - halfGap - index.position) * index.rotation;
            middle.rotation = Quaternion.FromToRotation(b - middle.position, center + halfGap - middle.position) * middle.rotation;
        }
        // Both holding fingers retain this same grip at rest and at the mouth.
        for (var i = 0; i < _grip.Count; i++)
        {
            var bone = _grip[i].Bone;
            if (bone == index || bone == middle)
                _grip[i] = (bone, bone.localRotation, bone.localRotation);
        }
    }

    private void ApplyGrip(float drawBlend)
    {
        foreach (var entry in _grip)
            entry.Bone.localRotation = Quaternion.Slerp(entry.Relaxed, entry.Draw, drawBlend);
    }

    internal void BeginExit()
    {
        if (_exiting) return;
        _exitBurn = BurnProgress;
        _exitHand = _hand.position;
        _exitHandRotation = _hand.rotation;
        if (_shoulder != null) _exitShoulderPosition = _shoulder.position;
        if (_supportShoulder != null) _exitSupportShoulderPosition = _supportShoulder.position;
        _exitSupport = _supportHand.position;
        _exitSupportRotation = _supportHand.rotation;
        _exitRetract = RetractPoint(_exitHand);
        _exitSupportRetract = RetractPoint(_exitSupport);
        _exitStarted = Time.unscaledTime;
        _exiting = true;
        _smoke?.StopEmission();
        _audio?.Stop();
    }

    private void EvaluateExit()
    {
        var t = Smooth(ExitProgress);
        var phases = HandMotion.Exit(ExitProgress);
        if (_shoulder != null) _shoulder.SetPositionAndRotation(Vector3.Lerp(_exitShoulderPosition, _shoulderPosition, phases.Retract), _shoulderRotation);
        if (_supportShoulder != null) _supportShoulder.SetPositionAndRotation(Vector3.Lerp(_exitSupportShoulderPosition, _supportShoulderPosition, phases.Retract), _supportShoulderRotation);
        var lowered = _exitRetract;
        lowered.y = _eyeAtStart.y - 0.72f;
        var supportLowered = _exitSupportRetract;
        supportLowered.y = _eyeAtStart.y - 0.72f;
        var weight = 1f - Smooth(Mathf.Max(0f, (ExitProgress - 0.85f) / 0.15f));
        _arm.Solve(ExitPosition(_exitHand, _exitRetract, lowered, phases), Quaternion.Slerp(_exitHandRotation, _restRotation, t),
            _eyeAtStart + _bodyRotation * new Vector3(_side * 0.38f, -0.48f, 0.04f), weight);
        _supportArm.Solve(ExitPosition(_exitSupport, _exitSupportRetract, supportLowered, phases), Quaternion.Slerp(_exitSupportRotation, _supportRotation, t),
            _eyeAtStart + _bodyRotation * new Vector3(-_side * 0.38f, -0.48f, 0.04f), weight);
        ApplyGrip(0f);
        _supportPose.Apply(false, true);
        _smoke?.Tick(_eye, _eye.position);
    }

    private Vector3 RetractPoint(Vector3 start)
    {
        var local = Quaternion.Inverse(_bodyRotation) * (start - _bodyOrigin);
        // Leave room for the whole hand, not merely the wrist, before lowering.
        local.z = Mathf.Min(local.z, Mathf.Min(0.05f, _railNearDepth - 0.20f));
        var target = _bodyOrigin + _bodyRotation * local;
        target.y = start.y + 0.035f;
        return target;
    }

    private static Vector3 ExitPosition(Vector3 start, Vector3 retracted, Vector3 lowered,
        (float Lift, float Retract, float Lower) phases)
    {
        var lifted = start + Vector3.up * (0.035f * phases.Lift);
        return Vector3.Lerp(Vector3.Lerp(lifted, retracted, phases.Retract), lowered, phases.Lower);
    }

    internal void Evaluate()
    {
        if (_exiting) { EvaluateExit(); return; }
        var elapsed = Time.time - _start;
        var nextIndex = _drawIndex;
        while (nextIndex + 1 < _session.DrawStarts.Length && elapsed >= _session.DrawStarts[nextIndex + 1]) nextIndex++;
        if (nextIndex != _drawIndex)
        {
            _drawIndex = nextIndex;
            _exhaleDelay = UnityEngine.Random.Range(1f, 2f);
            _exhaleDuration = UnityEngine.Random.Range(1f, 2.5f);
            _exhaled = false;
            _inhaled = false;
        }
        var drawTime = elapsed - _session.DrawStarts[_drawIndex];
        var blend = drawTime < 0 ? 0f : drawTime < 1.5f ? Smooth(drawTime / 1.5f) :
            drawTime < 3.8f ? 1f : 1f - Smooth((drawTime - 3.8f) / 1.7f);
        if (!_inhaled && drawTime >= 1.5f)
        {
            _inhaled = true;
            if (drawTime < 3.8f) _audio?.Play(true, 2.3f, drawTime - 1.5f);
        }
        // Solve for the wrist from the cigarette's filter endpoint, not vice versa.
        var mouth = _eye.TransformPoint(new Vector3(0f, Mathf.Clamp(Plugin.MouthHeight.Value, -0.15f, -0.02f), Mathf.Clamp(Plugin.MouthDistance.Value, 0.01f, 0.15f)));
        if (!_exhaled && drawTime >= 3.8f + _exhaleDelay)
        {
            _exhaled = true;
            _audio?.Play(false, _exhaleDuration);
            if (!_smokeUnavailable)
            {
                try
                {
                    _smoke ??= new ExhaleSmoke();
                    _smoke.Begin(_exhaleDuration);
                }
                catch (Exception e)
                {
                    _smokeUnavailable = true;
                    Plugin.Logger.LogWarning("Exhale unavailable; hand animation continues: " + e.Message);
                }
            }
        }
        _smoke?.Tick(_eye, mouth);
        var drawRotation = Quaternion.LookRotation(_eye.forward, -_side * _eye.right) * Quaternion.Inverse(_cigarette.localRotation);
        // Roll around the cigarette's mouth-facing axis: fingers rise and wrist drops.
        // Mirror for the left-hand option. The existing pose blend eases this in and out.
        drawRotation = Quaternion.AngleAxis(-_side * 30f, _eye.forward) * drawRotation;
        var filterInHand = _cigarette.localPosition + _cigarette.localRotation * new Vector3(0f, 0f, -0.02f);
        var drawPosition = mouth - drawRotation * filterInHand;
        var position = Vector3.Lerp(_restPosition, drawPosition, blend);
        // A small outward arc keeps the raising hand away from the chest.
        position += _bodyRotation * new Vector3(0.045f * Mathf.Sin(blend * Mathf.PI), 0f, 0f);
        var weight = Smooth((Time.time - _start) / 0.6f);
        if (_shoulder != null)
        {
            // Absolute pose: repeated Animator/LateUpdate evaluation cannot accumulate shoulder rotation.
            _shoulder.SetPositionAndRotation(_shoulderPosition + _restShoulderOffset * (1f - blend) * weight, _shoulderRotation);
        }
        if (_supportShoulder != null)
            _supportShoulder.SetPositionAndRotation(_supportShoulderPosition + _restSupportShoulderOffset * weight, _supportShoulderRotation);
        var elbow = _eyeAtStart + _bodyRotation * Vector3.Lerp(
            new Vector3(_side * 0.38f, -0.42f, 0.12f), new Vector3(_side * 0.30f, -0.34f, 0.10f), blend);
        _arm.Solve(position, Quaternion.Slerp(_restRotation, drawRotation, blend), elbow, weight);
        // Follow the solved elbow-to-wrist direction rather than the body's forward axis.
        // Swing only: preserve the thumb-up roll, rail contact and exact mouth orientation.
        var forearmDirection = _hand.position - _forearm.position;
        if (forearmDirection.sqrMagnitude > 0.000001f)
        {
            var fingerDirection = _hand.rotation * (_handFrame * Vector3.forward);
            var correction = Quaternion.FromToRotation(fingerDirection, forearmDirection.normalized);
            _hand.rotation = Quaternion.Slerp(Quaternion.identity, correction, (1f - blend) * weight) * _hand.rotation;
        }
        _supportArm.Solve(_supportPosition, _supportRotation,
            _eyeAtStart + _bodyRotation * new Vector3(-_side * 0.38f, -0.42f, 0.1f), weight);
        ApplyGrip(blend);
        _supportPose.Apply(blend == 0f && weight >= 1f, false);
#if CONTACT_DIAGNOSTICS
        if (!_contactReported && blend == 0f && weight >= 1f)
        {
            _contactReported = true;
            Plugin.Logger.LogInfo($"[RailContact] settled: rightActual={_hand.position.ToString("F4")}, rightTarget={_restPosition.ToString("F4")}, rightError={Vector3.Distance(_hand.position, _restPosition):F4}; {_arm.DescribeReach(_restPosition)}; leftActual={_supportHand.position.ToString("F4")}, leftTarget={_supportPosition.ToString("F4")}, leftError={Vector3.Distance(_supportHand.position, _supportPosition):F4}");
        }
#endif
    }

    private static float Smooth(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * (3f - 2f * t);
    }

    private static Quaternion HandFrame(Animator animator, bool left)
    {
        var hand = animator.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
        var middle = animator.GetBoneTransform(left ? HumanBodyBones.LeftMiddleProximal : HumanBodyBones.RightMiddleProximal);
        return Quaternion.Inverse(hand.rotation) * Quaternion.LookRotation(middle.position - hand.position, PalmNormal(animator, left));
    }

    private static Vector3 PalmNormal(Animator animator, bool left)
    {
        var hand = animator.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
        var index = animator.GetBoneTransform(left ? HumanBodyBones.LeftIndexProximal : HumanBodyBones.RightIndexProximal);
        var little = animator.GetBoneTransform(left ? HumanBodyBones.LeftLittleProximal : HumanBodyBones.RightLittleProximal);
        // This rig's index-to-little winding points out of the BACK of the right hand.
        // Use the inward palm side consistently for both wrist alignment and finger curl.
        return Vector3.Cross(index.position - hand.position, little.position - hand.position).normalized * (left ? 1f : -1f);
    }

    private static Vector3 PalmPoint(Animator animator, bool left)
    {
        var hand = animator.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
        var middle = animator.GetBoneTransform(left ? HumanBodyBones.LeftMiddleProximal : HumanBodyBones.RightMiddleProximal);
        return hand.InverseTransformPoint(Vector3.Lerp(hand.position, middle.position, 0.65f));
    }

    private static bool TryRailTop(PlayerCharacter character, PlayerHandsAnimator hands, Quaternion body, float side,
        RaycastHit railTop, ArmDriver arm, bool canLean, bool wristContact, Vector3 wristOffset, out Vector3 contact, out float nearEdge, out float farEdge)
    {
        contact = default;
        nearEdge = 0f;
        farEdge = 0f;
        var forward = body * Vector3.forward;
        var railDepth = Vector3.Dot(railTop.point - character.transform.position, forward);
        var samples = new List<(float Depth, Vector3 Point)>();
        bool Probe(float depth, out Vector3 point)
        {
            point = default;
            var origin = character.transform.position + body * new Vector3(side, 0f, depth);
            origin.y = railTop.point.y + 0.08f;
            if (!Physics.Raycast(origin, Vector3.down, out var hit, 0.16f, hands.environmentLayer, QueryTriggerInteraction.Ignore)) return false;
            if (hit.collider != railTop.collider || hit.normal.y < 0.85f) return false;
            var height = hit.point.y - character.transform.position.y;
            if (height < 0.65f || height > 1.4f) return false;
            point = hit.point;
            return true;
        }
        // Measure each hand's actual top span once at startup. Seed with the aimed depth
        // so thin bars between grid samples are still represented.
        for (var sample = -1; sample <= 60; sample++)
        {
            var depth = sample < 0 ? railDepth : 0.15f + sample * 0.01f;
            if (depth >= 0.1f && depth <= 0.75f && Probe(depth, out var point)) samples.Add((depth, point));
        }
        if (samples.Count == 0) return false;
        samples.Sort((a, b) => a.Depth.CompareTo(b.Depth));
        var anchor = 0;
        for (var i = 1; i < samples.Count; i++)
            if (Mathf.Abs(samples[i].Depth - railDepth) < Mathf.Abs(samples[anchor].Depth - railDepth)) anchor = i;
        // Stay on one contiguous bar, even when a combined mesh also contains other ledges.
        var first = anchor;
        var last = anchor;
        bool Connected(int a, int b) => samples[b].Depth - samples[a].Depth <= 0.015f &&
            Mathf.Abs(samples[b].Point.y - samples[a].Point.y) <= 0.04f;
        while (first > 0 && Connected(first - 1, first)) first--;
        while (last + 1 < samples.Count && Connected(last, last + 1)) last++;
        var front = samples[first].Depth;
        nearEdge = front;
        var back = samples[last].Depth;
        farEdge = back;
        // Smoking wrist at the outer edge; support-hand heel near the inner edge
        // to leave room for the fingertips across the top.
        var preferred = wristContact ? back - Mathf.Min(0.015f, (back - front) * 0.5f)
            : front + Mathf.Min(0.015f, (back - front) * 0.5f);
        var bestScore = float.PositiveInfinity;
        var found = false;
        // Include an exact probe at the preferred point instead of quantizing it to the grid.
        for (var i = first - 1; i <= last; i++)
        {
            Vector3 point;
            float depth;
            if (i < first)
            {
                depth = preferred;
                if (!Probe(depth, out point)) continue;
            }
            else { depth = samples[i].Depth; point = samples[i].Point; }
            var target = point + wristOffset;
            var leanSquared = arm.ShoulderOffsetFor(target).sqrMagnitude;
            if (!arm.CanReach(target) && (!canLean || leanSquared <= 0f || leanSquared > 0.25f * 0.25f)) continue;
            var score = Mathf.Abs(depth - preferred);
            if (score >= bestScore) continue;
            contact = point;
            bestScore = score;
            found = true;
        }
#if CONTACT_DIAGNOSTICS
        Plugin.Logger.LogInfo($"[RailContact] side={side:F2}, selected={found}, scene={railTop.collider.gameObject.scene.name}, collider={railTop.collider.name}, span={front:F4}..{back:F4}, preferred={preferred:F4}, contact={contact.ToString("F4")}; reach: {arm.DescribeReach(contact + wristOffset)}");
#endif
        return found;
    }

    internal void Restore()
    {
        try { _arm.Restore(); }
        finally
        {
            try { _supportArm.Restore(); }
            finally
            {
                try { _smoke?.Dispose(); _smoke = null; }
                finally { _audio?.Dispose(); _audio = null; }
            }
        }
    }
}
