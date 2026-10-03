using RootMotion.FinalIK;
using UnityEngine;

namespace NivalisMods.Cigarette;

internal sealed class ArmDriver
{
    private readonly IKSolverLimb _solver;
    private readonly Transform _upperArm;
    private readonly float _minReach, _maxReach;
    private readonly Transform? _target, _bendGoal;
    private readonly Vector3 _position, _bendNormal;
    private readonly Quaternion _rotation;
    private readonly float _positionWeight, _rotationWeight, _bendWeight, _maintainWeight;
    private readonly IKSolverLimb.BendModifier _bend;
    private GameObject? _elbow;

    internal ArmDriver(IKSolverLimb solver, Animator animator, bool left)
    {
        _solver = solver;
        _upperArm = animator.GetBoneTransform(left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
        var forearm = animator.GetBoneTransform(left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
        var hand = animator.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
        var upperLength = Vector3.Distance(_upperArm.position, forearm.position);
        var lowerLength = Vector3.Distance(forearm.position, hand.position);
        _minReach = Mathf.Abs(upperLength - lowerLength) + 0.025f;
        _maxReach = (upperLength + lowerLength) * 0.95f;
        _target = solver.target;
        _bendGoal = solver.bendGoal;
        _position = solver.IKPosition;
        _rotation = solver.IKRotation;
        _bendNormal = solver.bendNormal;
        _positionWeight = solver.IKPositionWeight;
        _rotationWeight = solver.IKRotationWeight;
        _bend = solver.bendModifier;
        _bendWeight = solver.bendModifierWeight;
        _maintainWeight = solver.maintainRotationWeight;
    }

#if CONTACT_DIAGNOSTICS
    internal string DescribeReach(Vector3 position) =>
        $"root={_upperArm.position.ToString("F4")}, target={position.ToString("F4")}, distance={Vector3.Distance(position, _upperArm.position):F4}, allowed={_minReach:F4}..{_maxReach:F4}";
#endif

    internal Vector3 ShoulderOffsetFor(Vector3 position)
    {
        if (CanReach(position)) return Vector3.zero;
        var delta = position - _upperArm.position;
        // Keep the same arm lengths; translate the shoulder only enough to retain elbow bend.
        return delta.normalized * Mathf.Max(0f, delta.magnitude - _maxReach * 0.98f);
    }

    internal bool CanReach(Vector3 position)
    {
        var distance = Vector3.Distance(position, _upperArm.position);
        return distance >= _minReach && distance <= _maxReach;
    }

    internal void Solve(Vector3 position, Quaternion rotation, Vector3 elbow, float weight)
    {
        _elbow ??= new GameObject("Cigarette.ElbowGoal");
        _elbow.transform.position = elbow;
        _solver.target = null;
        _solver.bendGoal = _elbow.transform;
        // Keep a little elbow bend at both limits instead of solving a singular folded/straight arm.
        var delta = position - _upperArm.position;
        var direction = delta.sqrMagnitude > 0.000001f ? delta.normalized : (_upperArm.position - elbow).normalized;
        _solver.IKPosition = _upperArm.position + direction * Mathf.Clamp(delta.magnitude, _minReach, _maxReach);
        _solver.IKRotation = rotation;
        _solver.IKPositionWeight = weight;
        _solver.IKRotationWeight = weight;
        _solver.maintainRotationWeight = 0f;
        _solver.bendModifier = IKSolverLimb.BendModifier.Goal;
        _solver.bendModifierWeight = 1f;
        _solver.Update();
    }

    internal void Restore()
    {
        try
        {
            _solver.target = _target;
            _solver.bendGoal = _bendGoal;
            _solver.bendNormal = _bendNormal;
            _solver.IKPosition = _position;
            _solver.IKRotation = _rotation;
            _solver.IKPositionWeight = _positionWeight;
            _solver.IKRotationWeight = _rotationWeight;
            _solver.bendModifier = _bend;
            _solver.bendModifierWeight = _bendWeight;
            _solver.maintainRotationWeight = _maintainWeight;
        }
        finally
        {
            if (_elbow != null) UnityEngine.Object.Destroy(_elbow);
            _elbow = null;
        }
    }
}
