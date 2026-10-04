using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Nivalis.Playables;
using UnityEngine;
using Object = UnityEngine.Object;

namespace NivalisMods.Cigarette;

// Borrow only native frame textures; never clone scene scripts, lights or colliders.
internal sealed class Apparition : IDisposable
{
    private readonly AnimatedSequence _sequence;
    private GameObject? _object;
    private Mesh? _mesh;
    private Material? _material;
    private readonly float _lifetime = UnityEngine.Random.Range(9f, 16f);
    private readonly float _phase;
    private float _age;
    private int _frame = -1;
    private readonly Il2CppStructArray<Color> _colors = new(4);
    internal Vector3 Position { get; }

    internal Apparition(AnimatedSequence sequence, Vector3 position, Quaternion rotation, float height, int layer)
    {
        _sequence = sequence;
        Position = position;
        _phase = UnityEngine.Random.Range(0f, 30f);
        try
        {
            var shader = Shader.Find("Mobile/Particles/Additive") ?? Shader.Find("Legacy Shaders/Particles/Additive");
            if (shader == null) throw new InvalidOperationException("No additive particle shader loaded for apparitions.");
            _material = new Material(shader) { name = "Cigarette.Apparition" };
            if (_material.HasProperty("_TintColor")) _material.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f));
            var texture = sequence.keyframes[0].texture;
            var width = height * texture.width / texture.height;
            _object = new GameObject("Cigarette.Apparition." + sequence.name) { layer = layer };
            _object.transform.SetPositionAndRotation(position, rotation);
            _mesh = new Mesh { name = "Cigarette.ApparitionQuad" };
            _mesh.vertices = new Il2CppStructArray<Vector3>(new[] {
                new Vector3(-width / 2f, 0f, 0f), new Vector3(width / 2f, 0f, 0f),
                new Vector3(width / 2f, height, 0f), new Vector3(-width / 2f, height, 0f) });
            _mesh.uv = new Il2CppStructArray<Vector2>(new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) });
            _mesh.triangles = new Il2CppStructArray<int>(new[] { 0, 2, 1, 0, 3, 2 });
            _mesh.colors = _colors;
            _object.AddComponent<MeshFilter>().sharedMesh = _mesh;
            var renderer = _object.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }
        catch { Dispose(); throw; }
    }

    internal bool Tick(float delta, float strength, bool visible, Vector3 cameraPosition)
    {
        if (_object == null || _sequence == null) return false;
        _object.SetActive(visible);
        if (!visible) return true;
        // The quad faces -Z. Turn only around the upright axis, preserving its ground anchor.
        var awayFromCamera = Vector3.ProjectOnPlane(Position - cameraPosition, Vector3.up);
        if (awayFromCamera.sqrMagnitude > 0.0001f)
            _object.transform.rotation = Quaternion.LookRotation(awayFromCamera.normalized);
        _age += delta;
        if (_age >= _lifetime) return false;
        var frames = _sequence.keyframes;
        if (frames == null || frames.Length == 0) return false;
        var index = (int)((_age + _phase) * Math.Max(1, _sequence.frameRate)) % frames.Length;
        if (index != _frame) { _material!.mainTexture = frames[index].texture; _frame = index; }
        var alpha = Mathf.SmoothStep(0f, 1f, Mathf.Min(_age / 1.5f, (_lifetime - _age) / 2f)) * strength * 0.75f;
        for (var i = 0; i < 4; i++) _colors[i] = new Color(1f, 1f, 1f, alpha);
        _mesh!.colors = _colors;
        return true;
    }

    public void Dispose()
    {
        if (_object != null) Object.Destroy(_object);
        if (_mesh != null) Object.Destroy(_mesh);
        if (_material != null) Object.Destroy(_material);
        _object = null; _mesh = null; _material = null;
    }
}
