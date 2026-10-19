using Nivalis;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace NivalisMods.Cigarette;

// Only a short, player-selected adjustment. Native Move retains collision,
// ground validation and the boat's local navigation coordinates.
internal sealed class RailApproach : IDisposable
{
    private readonly PlayerCharacter _character;
    private readonly PlayerHandsAnimator _hands;
    private readonly Collider _rail;
    private readonly Vector3 _localPoint;
    private readonly float _travel, _standOff, _initialYaw;
    private readonly Vector3 _localFacing;
    private readonly int _scene;
    private float _elapsed, _moved;
    private OverrideableBool.OverrideLock? _movement, _interaction;
    internal SmokeKind Kind { get; }
    internal RaycastHit Top { get; private set; }

    internal RailApproach(PlayerCharacter character, RaycastHit top, Vector3 facing, SmokeKind kind)
    {
        _character = character;
        _hands = character.Controller.HandsAnimator;
        _rail = top.collider;
        _localPoint = _rail.transform.InverseTransformPoint(top.point);
        _localFacing = _rail.transform.InverseTransformDirection(facing);
        _initialYaw = character.Controller.Rotation.eulerAngles.y;
        // A shared destination lets native movement approach or back away.
        _standOff = RailApproachMotion.StandOff(RailingTarget.MaximumDistance);
        _travel = Vector3.ProjectOnPlane(top.point - facing * _standOff - character.transform.position, Vector3.up).magnitude;
        _scene = SceneManager.GetActiveScene().handle;
        Kind = kind;
        Top = top;
    }

    internal void Begin(SmokingController owner)
    {
        _movement = _character.Controller.DisableMovement(owner);
        _interaction = _character.Interaction.DisableInteractions(owner);
        _character.Controller.StopMovement();
        RailPrompt.Release();
    }

    // -1 cancelled, 0 moving, 1 ready. A blocked path never teleports the player.
    internal int Tick()
    {
        if (!SmokingController.GameplayAvailable() || CancelPressed() ||
            PlayerManager._instance?.LocalPlayer?.Character != _character || _rail == null ||
            SceneManager.GetActiveScene().handle != _scene || _character.DrivenBoat != null || _hands == null ||
            _character.Controller.HandsAnimator != _hands || _character.ObjectHolder.IsHoldingObject ||
            !_character.CanEquipItem || _hands.IsSitting || _hands.Snapped || _hands.sleeping ||
            _hands.placingItem || _hands.IsPlayingMealAnimation || _hands.IsHoldingTray ||
            _hands.Animator.GetBool(PlayerHandsAnimator.Param.Fishing)) return -1;
        // Keep the chosen contact while translating/turning. The camera ray is
        // expected to move off a thin top as we align; it must not cancel setup.
        // A query also refreshes the isolated boat proxy transforms.
        RailSurface.Raycast(_character.Controller.Camera.transform.position,
            _character.Controller.Camera.transform.forward, out _, 1.5f,
            _hands.environmentLayer, QueryTriggerInteraction.Ignore);
        var point = _rail.transform.TransformPoint(_localPoint);
        if (!RailingTarget.TryTop(_character, _rail, point,
            RailingTarget.MaximumDistance + RailApproachMotion.Allowance, out var top, out _)) return -1;
        Top = top;
        var facing = Vector3.ProjectOnPlane(_rail.transform.TransformDirection(_localFacing), Vector3.up).normalized;
        _elapsed += Mathf.Min(Time.unscaledDeltaTime, 0.05f);
        var progress = RailApproachMotion.Progress(_elapsed);
        var yaw = Mathf.Atan2(facing.x, facing.z) * Mathf.Rad2Deg;
        var desiredYaw = Mathf.LerpAngle(_initialYaw, yaw, progress);
        _character.Controller.RotateCamera(Mathf.DeltaAngle(_character.Controller.Rotation.eulerAngles.y, desiredYaw));

        var offset = Vector3.ProjectOnPlane(top.point - facing * _standOff - _character.transform.position, Vector3.up);
        var remaining = offset.magnitude;
        var maximum = RailingTarget.MaximumDistance;
        var contactDistance = Vector3.ProjectOnPlane(top.point - _character.transform.position, Vector3.up).magnitude;
        if (RailApproachMotion.PositionReady(remaining, contactDistance, maximum))
        {
            // Already close enough: finish turning without issuing tiny Move calls.
            return progress >= 1f ? 1 : 0;
        }
        // Compare the scheduled position against ACTUAL remaining distance. Small
        // native movement thresholds or blocked frames no longer consume progress.
        var step = RailApproachMotion.Step(_travel, remaining, _elapsed, Time.unscaledDeltaTime);
        if (step > 0f)
        {
            var before = _character.transform.position;
            _character.Controller.Move(offset.normalized * step);
            _moved += Vector3.ProjectOnPlane(_character.transform.position - before, Vector3.up).magnitude;
        }
        if (_elapsed < 2f && _moved <= RailApproachMotion.MovementBudget(_travel, maximum)) return 0;
        Plugin.Logger.LogInfo($"Smoking approach cancelled: remaining={remaining:F3} m, planned={_travel:F3} m, moved={_moved:F3} m.");
        return -1;
    }

    private static bool CancelPressed()
    {
        var k = Keyboard.current;
        return Gamepad.current?.leftStick.ReadValue().sqrMagnitude > 0.1f ||
            Gamepad.current?.buttonEast.wasPressedThisFrame == true ||
            (k != null && (k.escapeKey.isPressed || k.wKey.isPressed || k.aKey.isPressed ||
                k.sKey.isPressed || k.dKey.isPressed || k.upArrowKey.isPressed ||
                k.downArrowKey.isPressed || k.leftArrowKey.isPressed || k.rightArrowKey.isPressed || k.spaceKey.isPressed));
    }

    public void Dispose()
    {
        try { _movement?.Release(); }
        finally { _movement = null; try { _interaction?.Release(); } finally { _interaction = null; } }
    }
}
