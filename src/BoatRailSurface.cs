using Nivalis;
using Nivalis.Boat;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NivalisMods.Cigarette;

internal sealed class BoatRailSurface : IDisposable
{
    private Scene _scene;
    private GameObject? _root;
    private Transform? _model;
    private readonly List<BoxCollider> _boxes = new();
    private bool _failed;

    private bool Synchronize()
    {
        var boat = BoatController.Instance;
        var character = PlayerManager._instance?.LocalPlayer?.Character;
        if (boat == null || character == null || character.DrivenBoat != null ||
            !boat.gameObject.activeInHierarchy || Vector3.Distance(boat.transform.position, character.transform.position) > 20f)
        {
            return false;
        }
        var model = boat.transform.Find("Boat (New)/combined_combine_Boat");
        if (model == null || !model.gameObject.activeInHierarchy ||
            model.GetComponent<MeshFilter>()?.sharedMesh?.name != "combined_combine_Boat")
        {
            return false;
        }
        if (_model != model || _root == null)
        {
            ClearObjects();
            _model = model;
            // Move the empty root BEFORE adding colliders. No collider ever enters
            // the game's physics scene, so there are no contacts or trigger events.
            if (!_scene.IsValid() || !_scene.isLoaded)
            {
                var parameters = new CreateSceneParameters { localPhysicsMode = LocalPhysicsMode.Physics3D };
                _scene = SceneManager.CreateScene("Cigarette.BoatRails.QueryOnly", parameters);
            }
            _root = new GameObject("Cigarette.Boat.RailingTargets");
            SceneManager.MoveGameObjectToScene(_root, _scene);
            foreach (var definition in BoatRailLayout.Boxes)
            {
                var target = new GameObject("Cigarette.Boat.Railing." + definition.Name);
                SceneManager.MoveGameObjectToScene(target, _scene);
                target.transform.SetParent(_root.transform, false);
                var box = target.AddComponent<BoxCollider>();
                box.center = new Vector3((definition.MinX + definition.MaxX) / 2f,
                    (definition.MinY + definition.MaxY) / 2f, (definition.MinZ + definition.MaxZ) / 2f);
                box.size = new Vector3(definition.MaxX - definition.MinX,
                    definition.MaxY - definition.MinY, definition.MaxZ - definition.MinZ);
                _boxes.Add(box);
            }
        }
        // Cannot parent across physics scenes: copy the exact model transform instead.
        var changed = _root.transform.position != model.position ||
            _root.transform.rotation != model.rotation || _root.transform.localScale != model.lossyScale;
        _root.transform.SetPositionAndRotation(model.position, model.rotation);
        _root.transform.localScale = model.lossyScale;
        if (changed) Physics.SyncTransforms();
        return true;
    }

    public bool Raycast(Vector3 origin, Vector3 direction, float distance, out RaycastHit hit)
    {
        hit = default;
        if (_failed) return false;
        try
        {
            if (!Synchronize()) return false;
            var found = false;
            var ray = new Ray(origin, direction);
            foreach (var box in _boxes)
                if (box.Raycast(ray, out var candidate, distance) && (!found || candidate.distance < hit.distance))
                { hit = candidate; found = true; }
            if (!found) return false;
            // Native boat walls are outside the ordinary environment mask. They
            // still obstruct our targets; do not let smoking reach through a cabin.
            var boatLayer = LayerMask.NameToLayer("Boat");
            if (boatLayer >= 0 && Physics.Raycast(origin, direction, out var wall, hit.distance,
                1 << boatLayer, QueryTriggerInteraction.Ignore) && wall.distance < hit.distance)
                hit = wall;
            return true;
        }
        catch (Exception e)
        {
            _failed = true;
            Plugin.Logger.LogError("Boat railing query failed; normal rails remain available: " + e);
            ClearObjects();
            return false;
        }
    }

    public Transform? AnchorFor(Collider collider) =>
        !_failed && collider != null && _boxes.Any(b => b == collider) ? _model : null;

    private void ClearObjects()
    {
        foreach (var box in _boxes) if (box != null) box.enabled = false;
        if (_root != null) UnityEngine.Object.Destroy(_root);
        _root = null;
        _model = null;
        _boxes.Clear();
    }

    public void Dispose()
    {
        _failed = true;
        ClearObjects();
        if (_scene.IsValid() && _scene.isLoaded) SceneManager.UnloadSceneAsync(_scene);
    }

}
