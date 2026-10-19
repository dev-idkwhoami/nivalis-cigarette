using System.Runtime.CompilerServices;
using UnityEngine;

[assembly: InternalsVisibleTo("Nivalis.Cigarette.RailDev")]

namespace NivalisMods.Cigarette;

// Boat geometry lives in an isolated physics scene and is queried explicitly.
internal static class RailSurface
{
    internal static readonly BoatRailSurface Boat = new();

    internal static bool Raycast(Vector3 origin, Vector3 direction, out RaycastHit hit,
        float distance, int mask, QueryTriggerInteraction triggers)
    {
        var found = Physics.Raycast(origin, direction, out hit, distance, mask, triggers);
        if (Boat.Raycast(origin, direction, distance, out var extra) &&
            (!found || extra.distance < hit.distance))
        {
            hit = extra;
            found = true;
        }
        return found;
    }
}

// Converts cached world-space poses by the motion since their last evaluation.
internal sealed class MovingRailFrame
{
    private readonly Transform _anchor;
    private Vector3 _previousPosition;
    private Quaternion _previousRotation;
    internal MovingRailFrame(Transform anchor)
    {
        _anchor = anchor;
        _previousPosition = anchor.position;
        _previousRotation = anchor.rotation;
    }

    internal (Vector3 Before, Vector3 After, Quaternion Rotation) Advance()
    {
        if (_anchor == null || !_anchor.gameObject.activeInHierarchy)
            throw new InvalidOperationException("The moving railing is no longer available.");
        var before = _previousPosition;
        var after = _anchor.position;
        var rotation = _anchor.rotation * Quaternion.Inverse(_previousRotation);
        _previousPosition = after;
        _previousRotation = _anchor.rotation;
        return (before, after, rotation);
    }
}
