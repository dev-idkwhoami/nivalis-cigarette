using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using Object = UnityEngine.Object;

namespace NivalisMods.Cigarette;

// World-space puffs rendered as a single dynamic mesh. No paused native emitter or custom erosion shader.
internal sealed class ExhaleSmoke
{
    private const int Capacity = 320;
    private const float PuffsPerSecond = 96f;
    private float _emissionEnd, _nextPuff, _spreadRotation;
    private int _emissionIndex;
    private GameObject? _object;
    private Mesh? _mesh;
    private MeshRenderer _renderer = null!;
    private Material? _material;
    private Texture2D? _texture;
    private readonly List<Puff> _puffs = new();
    private readonly Il2CppStructArray<Vector3> _vertices = new(Capacity * 4);
    private readonly Il2CppStructArray<Color> _colors = new(Capacity * 4);
    private sealed record Puff(Vector3 Origin, Vector3 Velocity, Vector3 Drift, float Phase,
        float Born, float Lifetime, float Size);
    private int _lastFrame = -1;
    private int _previousCount;

    internal ExhaleSmoke()
    {
        try
        {
            _object = new GameObject("Cigarette.Exhale");
            _renderer = _object.AddComponent<MeshRenderer>();
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _renderer.enabled = false;
            MakeMaterial();
            _mesh = new Mesh { name = "Cigarette.SmokeBillboards" };
            _mesh.MarkDynamic();
            var uv = new Il2CppStructArray<Vector2>(Capacity * 4);
            var triangles = new Il2CppStructArray<int>(Capacity * 6);
            for (var i = 0; i < Capacity; i++)
            {
                var v = i * 4;
                uv[v] = new Vector2(0, 0); uv[v + 1] = new Vector2(1, 0);
                uv[v + 2] = new Vector2(1, 1); uv[v + 3] = new Vector2(0, 1);
                var t = i * 6;
                triangles[t] = v; triangles[t + 1] = v + 2; triangles[t + 2] = v + 1;
                triangles[t + 3] = v; triangles[t + 4] = v + 3; triangles[t + 5] = v + 2;
            }
            _mesh.vertices = _vertices;
            _mesh.colors = _colors;
            _mesh.uv = uv;
            _mesh.triangles = triangles;
            _object.AddComponent<MeshFilter>().sharedMesh = _mesh;
        }
        catch { Dispose(); throw; }
    }

    internal void Begin(float duration)
    {
        _nextPuff = Time.time;
        _emissionEnd = _nextPuff + duration;
        _spreadRotation = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
        _emissionIndex = 0;
    }

    internal void StopEmission() => _emissionEnd = 0f;

    private void Emit(Transform eye, Vector3 mouth, float now)
    {
        // Even time spacing, independent of frame rate. Discard a long hitch's backlog.
        _nextPuff = Mathf.Max(_nextPuff, now - 0.1f);
        while (_nextPuff <= now && _nextPuff < _emissionEnd && _puffs.Count < Capacity)
        {
            var born = _nextPuff;
            _nextPuff += 1f / PuffsPerSecond;
            var i = _emissionIndex++;
            // Interleave inner and outer samples throughout the breath, rather than emitting rings.
            var radius = Mathf.Sqrt(Mathf.Repeat(0.5f + i * 0.618034f, 1f));
            var angle = _spreadRotation + i * 2.399963f + UnityEngine.Random.Range(-0.3f, 0.3f);
            var radial = eye.right * Mathf.Cos(angle) + eye.up * Mathf.Sin(angle);
            var tangent = eye.right * -Mathf.Sin(angle) + eye.up * Mathf.Cos(angle);
            var speed = UnityEngine.Random.Range(0.55f, 0.85f);
            var origin = mouth + eye.forward * UnityEngine.Random.Range(0.12f, 0.22f)
                + radial * (radius * 0.055f);
            var velocity = eye.forward * speed + radial * (radius * speed * UnityEngine.Random.Range(0.5f, 0.95f));
            _puffs.Add(new Puff(origin, velocity, (tangent + radial * UnityEngine.Random.Range(-0.6f, 0.6f)) * UnityEngine.Random.Range(0.05f, 0.10f),
                UnityEngine.Random.Range(0f, Mathf.PI * 2f), born,
                UnityEngine.Random.Range(2.0f, 2.8f), UnityEngine.Random.Range(0.09f, 0.14f)));
        }
    }

    internal void Tick(Transform eye, Vector3 mouth)
    {
        if (_lastFrame == Time.frameCount) return;
        _lastFrame = Time.frameCount;
        if (_object == null || _mesh == null) return;
        var now = Time.time;
        _puffs.RemoveAll(p => now - p.Born >= p.Lifetime);
        Emit(eye, mouth, now);
        var camera = eye.GetComponent<Camera>();
        if (camera != null && (camera.cullingMask & (1 << _object.layer)) == 0)
            for (var layer = 0; layer < 32; layer++)
                if ((camera.cullingMask & (1 << layer)) != 0) { _object.layer = layer; break; }
        _object.transform.position = mouth;
        for (var i = 0; i < _puffs.Count; i++)
        {
            var puff = _puffs[i];
            var age = now - puff.Born;
            var fraction = age / puff.Lifetime;
            var alpha = 0.12f * Mathf.Clamp01(age / 0.22f) * (1f - fraction) * (1f - fraction);
            var travel = age / (1f + age * 0.35f);
            var swirl = puff.Drift * ((Mathf.Sin(age * 2.1f + puff.Phase) - Mathf.Sin(puff.Phase)) + 0.35f * (Mathf.Sin(age * 4.7f + puff.Phase * 2f) - Mathf.Sin(puff.Phase * 2f))) * age;
            var position = puff.Origin + puff.Velocity * travel + swirl + Vector3.up * (0.035f * age * age);
            var halfSize = puff.Size * Mathf.Lerp(0.65f, 2.2f, fraction) * 0.5f;
            var right = eye.right * halfSize;
            var up = eye.up * halfSize;
            var center = position - mouth;
            var v = i * 4;
            _vertices[v] = center - right - up;
            _vertices[v + 1] = center + right - up;
            _vertices[v + 2] = center + right + up;
            _vertices[v + 3] = center - right + up;
            for (var j = 0; j < 4; j++) _colors[v + j] = new Color(0.46f, 0.48f, 0.50f, alpha);
        }
        for (var i = _puffs.Count * 4; i < _previousCount * 4; i++) _colors[i] = Color.clear;
        _previousCount = _puffs.Count;
        _mesh.vertices = _vertices;
        _mesh.colors = _colors;
        _mesh.RecalculateBounds();
        _renderer.enabled = _puffs.Count > 0;
    }

    private void MakeMaterial()
    {
        // Confirmed in sharedassets0; supports vertex color/alpha and has no erosion threshold.
        var shader = Shader.Find("Mobile/Particles/Alpha Blended");
        if (shader == null) throw new InvalidOperationException("Mobile/Particles/Alpha Blended shader not loaded.");
        _material = new Material(shader);
        _texture = new Texture2D(32, 32, TextureFormat.RGBA32, false);
        var pixels = new Color[32 * 32];
        for (var y = 0; y < 32; y++)
        for (var x = 0; x < 32; x++)
        {
            var dx = (x - 15.5f) / 15.5f;
            var dy = (y - 15.5f) / 15.5f;
            pixels[y * 32 + x] = new Color(1f, 1f, 1f, Mathf.Pow(Mathf.Clamp01(1f - dx * dx - dy * dy), 2f));
        }
        _texture.SetPixels(new Il2CppStructArray<Color>(pixels));
        _texture.Apply();
        _material.mainTexture = _texture;
        if (_material.HasProperty("_TintColor")) _material.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f));
        _material.renderQueue = 3000;
        _renderer.sharedMaterial = _material;
    }

    internal void Dispose()
    {
        if (_object != null) Object.Destroy(_object);
        if (_mesh != null) Object.Destroy(_mesh);
        if (_material != null) Object.Destroy(_material);
        if (_texture != null) Object.Destroy(_texture);
        _object = null;
        _mesh = null;
        _material = null;
        _texture = null;
    }
}
