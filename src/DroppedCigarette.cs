using UnityEngine;
using Object = UnityEngine.Object;

namespace NivalisMods.Cigarette;

// Short-lived physical prop, never an inventory item or a persistent scene object.
internal sealed class DroppedCigarette
{
    private readonly GameObject _object;
    private readonly Material[] _materials;
    private readonly float _started;
    private readonly int _scene;
    private readonly JointModel? _joint;
    internal DroppedCigarette(GameObject cigarette, Material[] materials, int scene, Transform player, JointModel? joint = null)
    {
        _object = cigarette;
        _materials = materials;
        _joint = joint;
        _scene = scene;
        _started = Time.time;
        cigarette.transform.SetParent(null, true);
        cigarette.layer = 0;
        foreach (var renderer in cigarette.GetComponentsInChildren<Renderer>(true)) renderer.gameObject.layer = 0;

        // The filter occupies -2 cm..0; paper/ember extend along local +Z.
        // Size the collider from the actual remaining geometry, including a spent butt.
        var paper = cigarette.transform.Find("Paper");
        var ember = cigarette.transform.Find("Ember");
        var end = paper != null && paper.gameObject.activeSelf ? paper.localScale.y * 2f : 0f;
        if (ember != null && ember.gameObject.activeSelf)
            end = Mathf.Max(end, ember.localPosition.z + ember.localScale.y);
        if (joint != null) end = joint.Length + (joint.Length > 0f ? 0.0015f : 0f);
        var collider = cigarette.AddComponent<CapsuleCollider>();
        collider.direction = 2;
        collider.radius = joint?.Radius ?? 0.0035f;
        collider.height = end + 0.02f;
        collider.center = new Vector3(0f, 0f, (end - 0.02f) * 0.5f);
        collider.contactOffset = 0.0005f;
        collider.isTrigger = false;
        // Release beside the hand without colliding with or pushing the player capsule.
        foreach (var playerCollider in player.GetComponentsInChildren<Collider>(true))
            if (playerCollider != null) Physics.IgnoreCollision(collider, playerCollider, true);

        var body = cigarette.AddComponent<Rigidbody>();
        body.mass = 0.002f;
        body.drag = 0.15f;
        body.angularDrag = 0.5f;
        body.useGravity = true;
        body.isKinematic = false;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.velocity = cigarette.transform.forward * 0.12f + Vector3.down * 0.1f;
        body.angularVelocity = (Vector3.right + Vector3.forward * UnityEngine.Random.Range(-1f, 1f)).normalized * 2.8f;
    }
    internal bool Tick()
    {
        if (_object == null || UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle != _scene) return false;
        var age = Time.time - _started;
        if (age >= 3f) return false;
        return true;
    }
    internal void Dispose()
    {
        if (_object != null) Object.Destroy(_object);
        foreach (var material in _materials) if (material != null) Object.Destroy(material);
        _joint?.Dispose();
    }
}
