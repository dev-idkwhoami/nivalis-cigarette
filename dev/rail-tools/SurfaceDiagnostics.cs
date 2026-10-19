using Nivalis;
using UnityEngine;

namespace NivalisMods.Cigarette.RailDev;

// On-demand evidence only: never adds collision or changes the targeting mask.
internal static class SurfaceDiagnostics
{
    internal static object Capture(PlayerCharacter character)
    {
        var eye = character.Controller.Camera.transform;
        var ray = new Ray(eye.position, eye.forward);
        var environment = character.Controller.HandsAnimator.environmentLayer.value;
        var colliders = Resources.FindObjectsOfTypeAll<Collider>()
            .Where(c => c != null && c.gameObject.scene.IsValid() &&
                Near(c.transform.position, c.bounds, eye.position))
            .OrderBy(c => Distance(c.transform.position, c.bounds, eye.position)).ToArray();
        var renderers = Resources.FindObjectsOfTypeAll<MeshRenderer>()
            .Where(r => r != null && r.gameObject.scene.IsValid() &&
                Near(r.transform.position, r.bounds, eye.position))
            .OrderBy(r => r.bounds.IntersectRay(ray) ? 0 : 1)
            .ThenBy(r => Distance(r.transform.position, r.bounds, eye.position)).ToArray();
        return new
        {
            radius = 4f,
            // Unlike the original hits, retain triggers and player descendants.
            allRayHits = Physics.RaycastAll(ray, 3f, ~0, QueryTriggerInteraction.Collide)
                .Where(h => h.collider != null).OrderBy(h => h.distance)
                .Select(h => new
                {
                    identity = Plugin.Identity(h.collider, "rail"), h.distance,
                    point = Vec(h.point), normal = Vec(h.normal),
                    trigger = h.collider.isTrigger,
                    playerChild = h.collider.transform.IsChildOf(character.transform),
                    layer = h.collider.gameObject.layer,
                    layerName = LayerMask.LayerToName(h.collider.gameObject.layer)
                }).ToArray(),
            nearbyColliderCount = colliders.Length,
            nearbyColliders = colliders.Take(128).Select(c => new
            {
                identity = Plugin.Identity(c, "rail"), c.enabled,
                active = c.gameObject.activeInHierarchy, trigger = c.isTrigger,
                playerChild = c.transform.IsChildOf(character.transform),
                layer = c.gameObject.layer, layerName = LayerMask.LayerToName(c.gameObject.layer),
                inEnvironment = (environment & (1 << c.gameObject.layer)) != 0,
                boundsCenter = Vec(c.bounds.center), boundsSize = Vec(c.bounds.size),
                eyeInsideBounds = c.bounds.Contains(eye.position),
                localPosition = Vec(c.transform.localPosition),
                mesh = c.TryCast<MeshCollider>()?.sharedMesh?.name,
                convex = c.TryCast<MeshCollider>()?.convex,
                rigidbody = c.attachedRigidbody != null ? Path(c.attachedRigidbody.transform) : null
            }).ToArray(),
            nearbyRendererCount = renderers.Length,
            nearbyRenderers = renderers.Take(128).Select(r => new
            {
                scene = r.gameObject.scene.name, path = Path(r.transform), r.enabled,
                active = r.gameObject.activeInHierarchy,
                playerChild = r.transform.IsChildOf(character.transform),
                layer = r.gameObject.layer, layerName = LayerMask.LayerToName(r.gameObject.layer),
                mesh = r.GetComponent<MeshFilter>()?.sharedMesh?.name,
                worldPosition = Vec(r.transform.position), localPosition = Vec(r.transform.localPosition),
                boundsCenter = Vec(r.bounds.center), boundsSize = Vec(r.bounds.size),
                rayIntersectsBounds = r.bounds.IntersectRay(ray),
                collidersOnObject = r.GetComponents<Collider>().Select(c => Plugin.Identity(c, "rail")).ToArray()
            }).ToArray()
        };
    }

    private static float Distance(Vector3 position, Bounds bounds, Vector3 eye) =>
        Math.Min(Vector3.Distance(position, eye), Vector3.Distance(bounds.ClosestPoint(eye), eye));

    private static bool Near(Vector3 position, Bounds bounds, Vector3 eye) => Distance(position, bounds, eye) <= 4f;

    private static float[] Vec(Vector3 v) => new[] { v.x, v.y, v.z };

    private static string Path(Transform t)
    {
        var names = new List<string>();
        for (; t != null; t = t.parent) names.Add(t.name);
        names.Reverse();
        return string.Join("/", names);
    }
}
