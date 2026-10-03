using Cinemachine;
using Nivalis;
using UnityEngine;

namespace NivalisMods.Cigarette;

internal sealed class StanceControl
{
    private readonly PlayerHandsAnimator _hands;
    private readonly CinemachinePOV _pov;
    private readonly AxisState _vertical;
    private readonly AxisState _horizontal;
    private readonly AxisState.Recentering _verticalRecentering;
    private readonly AxisState.Recentering _horizontalRecentering;
    private Vector3 _position;
    private Quaternion _rotation;
    private bool _placed;

    internal StanceControl(PlayerHandsAnimator hands, PlayerCharacterController controller)
    {
        _hands = hands;
        _pov = controller.FirstPersonPOV;
        // These getters box copies of the native AxisState, so the originals remain independent.
        _vertical = _pov.m_VerticalAxis;
        _horizontal = _pov.m_HorizontalAxis;
        _verticalRecentering = _pov.m_VerticalRecentering;
        _horizontalRecentering = _pov.m_HorizontalRecentering;
    }

    internal void Place(PlayerCharacter character)
    {
        // Rotation is the game's yaw-only body heading; camera pitch never participates.
        var heading = character.Controller.Rotation;

        // Let the game's own upright free-look placement retain its rig offsets and model axes.
        // Then freeze that world pose instead of tracking camera rotation every frame.
        var snap = _hands.snapRigRotationToCamera;
        var freeRotation = _hands.freelookCameraRotation;
        var offset = _hands.rigOffset;
        var targetOffset = _hands.targetRigOffset;
        try
        {
            _hands.snapRigRotationToCamera = false;
            _hands.freelookCameraRotation = heading;
            _hands.rigOffset = _hands.defaultRigOffset;
            _hands.targetRigOffset = _hands.defaultRigOffset;
            _hands.UpdateRig();
            _position = _hands.Rig.position;
            _rotation = _hands.Rig.rotation;
            _placed = true;
        }
        finally
        {
            _hands.snapRigRotationToCamera = snap;
            _hands.freelookCameraRotation = freeRotation;
            _hands.rigOffset = offset;
            _hands.targetRigOffset = targetOffset;
        }

        var horizontal = _pov.m_HorizontalAxis;
        var center = horizontal.Value + Mathf.DeltaAngle(horizontal.Value, heading.eulerAngles.y);
        horizontal.m_MinValue = center - 65f;
        horizontal.m_MaxValue = center + 65f;
        horizontal.m_Wrap = false;
        horizontal.Value = Mathf.Clamp(horizontal.Value, horizontal.m_MinValue, horizontal.m_MaxValue);
        _pov.m_HorizontalAxis = horizontal;
        var vertical = _pov.m_VerticalAxis;
        // Cinemachine POV positive pitch looks down. Keep upward movement, limit exposed shoulders.
        vertical.m_MinValue = Mathf.Max(_vertical.m_MinValue, -70f);
        vertical.m_MaxValue = Mathf.Min(_vertical.m_MaxValue, Mathf.Clamp(Plugin.LookDownLimit.Value, 0f, 45f));
        vertical.m_Wrap = false;
        vertical.Value = Mathf.Clamp(vertical.Value, vertical.m_MinValue, vertical.m_MaxValue);
        _pov.m_VerticalAxis = vertical;
        var vRecentering = _verticalRecentering;
        vRecentering.m_enabled = false;
        _pov.m_VerticalRecentering = vRecentering;
        var hRecentering = _horizontalRecentering;
        hRecentering.m_enabled = false;
        _pov.m_HorizontalRecentering = hRecentering;
    }

    internal void AlignCustomBody(Animator animator, Vector3 pivot, Quaternion heading)
    {
        // The native rig placement includes its own free-look blending and parent rotation.
        // Align the sampled skeleton, not an assumed model forward axis, before freezing it.
        var left = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        var right = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
        var across = Vector3.ProjectOnPlane(right.position - left.position, Vector3.up);
        if (across.sqrMagnitude < 0.0001f)
            throw new InvalidOperationException("Cannot establish the arm rig's body heading.");
        var correction = Quaternion.FromToRotation(across.normalized, heading * Vector3.right);
        _position = pivot + correction * (_position - pivot);
        _rotation = correction * _rotation;
        Apply();
    }

    internal void Apply()
    {
        if (_placed && _hands != null && _hands.Rig != null)
            _hands.Rig.SetPositionAndRotation(_position, _rotation);
    }

    internal void Restore()
    {
        _placed = false;
        if (_pov == null) return;
        // Preserve where the user is now looking, while restoring the previous input settings.
        var v = _pov.m_VerticalAxis.Value;
        var h = _pov.m_HorizontalAxis.Value;
        _vertical.Value = Mathf.Clamp(v, _vertical.m_MinValue, _vertical.m_MaxValue);
        _horizontal.Value = _horizontal.m_Wrap
            ? Mathf.Repeat(h - _horizontal.m_MinValue, _horizontal.m_MaxValue - _horizontal.m_MinValue) + _horizontal.m_MinValue
            : Mathf.Clamp(h, _horizontal.m_MinValue, _horizontal.m_MaxValue);
        _pov.m_VerticalAxis = _vertical;
        _pov.m_HorizontalAxis = _horizontal;
        _pov.m_VerticalRecentering = _verticalRecentering;
        _pov.m_HorizontalRecentering = _horizontalRecentering;
    }
}
