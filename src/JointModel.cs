using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using Object = UnityEngine.Object;

namespace NivalisMods.Cigarette;

// A tapered paper cone with a narrow rolled tip. Burn cuts back the wide end;
// it does not squash a complete cone down to a short, still-wide cylinder.
internal sealed class JointModel : IDisposable
{
    private const int Sides = 24;
    private readonly List<Mesh> _meshes = new();
    private readonly List<Material> _materials = new();
    private GameObject _paper = null!, _ember = null!;
    private GameObject _twist = null!;
    private Texture2D? _paperTexture;
    private Mesh _paperMesh = null!;
    private float _lastBurn = -1f;
    internal float Length { get; private set; } = JointShape.PaperLength;
    internal float Radius { get; private set; } = JointShape.EndRadius;

    internal JointModel(Transform parent, int layer)
    {
        try
        {
            var white = Color.white;
            (_paper, _paperMesh) = Part(parent, layer, "Paper", white);
            _paperTexture = MakePaperTexture();
            _paper.GetComponent<MeshRenderer>().sharedMaterial.mainTexture = _paperTexture;
            var (tip, tipMesh) = Part(parent, layer, "Filter", new Color(0.94f, 0.91f, 0.81f));
            Fill(tipMesh, -JointShape.TipLength, 0f, JointShape.MouthRadius, JointShape.BaseRadius);
            var (_, openingMesh) = Part(parent, layer, "TipOpening", new Color(0.20f, 0.19f, 0.15f));
            Fill(openingMesh, -JointShape.TipLength - 0.00005f, -JointShape.TipLength, 0.00165f, 0.00165f);
            (_twist, var twistMesh) = Part(parent, layer, "TwistedPaper", new Color(0.79f, 0.72f, 0.55f));
            MakeTwist(twistMesh);
            (_ember, var emberMesh) = Part(parent, layer, "Ember", new Color(0.45f, 0.10f, 0.025f), true);
            Fill(emberMesh, 0f, 1f, 1f, 0.92f);
            UpdateBurn(0f);
        }
        catch { Dispose(); throw; }
    }

    internal void UpdateBurn(float progress)
    {
        if (Mathf.Abs(progress - _lastBurn) < 0.0001f) return;
        _lastBurn = progress;
        var paper = JointShape.Burn(progress);
        var twist = Mathf.Clamp01(1f - progress / JointShape.TwistBurnFraction);
        Length = paper.Length + JointShape.TwistLength * twist;
        Radius = paper.Radius;
        _twist.SetActive(twist > 0f);
        _twist.transform.localPosition = new Vector3(0f, 0f, paper.Length);
        _twist.transform.localScale = new Vector3(1f, 1f, twist);
        _paper.SetActive(paper.Length > 0f); _ember.SetActive(paper.Length > 0f);
        if (paper.Length <= 0f) return;
        Fill(_paperMesh, 0f, paper.Length, JointShape.BaseRadius, Radius);
        _ember.transform.localPosition = new Vector3(0f, 0f, Length);
        var emberRadius = Mathf.Lerp(Radius, 0.0005f, twist);
        _ember.transform.localScale = new Vector3(emberRadius, emberRadius, 0.001f);
    }

    private (GameObject, Mesh) Part(Transform parent, int layer, string name, Color color, bool glow = false)
    {
        var part = new GameObject(name) { layer = layer };
        part.transform.SetParent(parent, false);
        var mesh = new Mesh { name = "Cigarette.Joint." + name };
        _meshes.Add(mesh);
        part.AddComponent<MeshFilter>().sharedMesh = mesh;
        var shader = Shader.Find("Standard") ?? Shader.Find("Unlit/Color");
        if (shader == null) throw new InvalidOperationException("No joint material shader available.");
        var material = new Material(shader) { color = color };
        _materials.Add(material);
        if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", 0.12f);
        if (glow && material.HasProperty("_EmissionColor"))
        { material.EnableKeyword("_EMISSION"); material.SetColor("_EmissionColor", new Color(0.9f, 0.12f, 0.015f)); }
        part.AddComponent<MeshRenderer>().sharedMaterial = material;
        return (part, mesh);
    }

    private static void Fill(Mesh mesh, float start, float end, float startRadius, float endRadius)
    {
        // Separate side/cap vertices keep cap normals flat and cone normals smooth.
        var vertices = new Vector3[Sides * 4 + 2];
        var uv = new Vector2[vertices.Length];
        var triangles = new List<int>(Sides * 12);
        for (var i = 0; i < Sides; i++)
        {
            var angle = i * Mathf.PI * 2f / Sides;
            var radial = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
            vertices[i] = vertices[Sides * 2 + i] = radial * startRadius + Vector3.forward * start;
            vertices[Sides + i] = vertices[Sides * 3 + i] = radial * endRadius + Vector3.forward * end;
            uv[i] = new Vector2((float)i / Sides, start / JointShape.PaperLength);
            uv[Sides + i] = new Vector2((float)i / Sides, end / JointShape.PaperLength);
            var next = (i + 1) % Sides;
            triangles.AddRange(new[] { i, next, Sides + next, i, Sides + next, Sides + i,
                Sides * 4, Sides * 2 + next, Sides * 2 + i,
                Sides * 4 + 1, Sides * 3 + i, Sides * 3 + next });
        }
        vertices[Sides * 4] = Vector3.forward * start;
        vertices[Sides * 4 + 1] = Vector3.forward * end;
        mesh.vertices = new Il2CppStructArray<Vector3>(vertices);
        mesh.uv = new Il2CppStructArray<Vector2>(uv);
        mesh.triangles = new Il2CppStructArray<int>(triangles.ToArray());
        mesh.RecalculateNormals(); mesh.RecalculateBounds();
    }

    private static Texture2D MakePaperTexture()
    {
        const int width = 128, height = 256;
        var texture = new Texture2D(width, height) { name = "Cigarette.RollingPaper", filterMode = FilterMode.Bilinear };
        var pixels = new Color[width * height];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            // Opaque shading suggests the contents through thin paper without transparent sorting.
            var flecks = Mathf.PerlinNoise(x * 0.23f + 17f, y * 0.19f + 5f);
            var fibre = Mathf.PerlinNoise(x * 0.8f, y * 0.12f + 37f);
            var colour = Color.Lerp(new Color(0.58f, 0.59f, 0.46f), new Color(0.91f, 0.88f, 0.75f),
                Mathf.Clamp01(0.35f + flecks * 0.65f + (fibre - 0.5f) * 0.3f));
            if (x < 3) colour = Color.Lerp(colour, new Color(0.94f, 0.91f, 0.82f), 0.4f);
            pixels[y * width + x] = colour;
        }
        texture.SetPixels(new Il2CppStructArray<Color>(pixels)); texture.Apply();
        return texture;
    }

    private static void MakeTwist(Mesh mesh)
    {
        const int rings = 12;
        var vertices = new Vector3[(rings + 1) * Sides];
        var triangles = new List<int>();
        for (var ring = 0; ring <= rings; ring++)
        {
            var t = (float)ring / rings;
            var radius = t < 0.12f ? Mathf.Lerp(JointShape.EndRadius, 0.0009f, Mathf.SmoothStep(0f, 1f, t / 0.12f))
                : Mathf.Lerp(0.0009f, 0.00012f, (t - 0.12f) / 0.88f);
            for (var i = 0; i < Sides; i++)
            {
                var angle = i * Mathf.PI * 2f / Sides + t * Mathf.PI * 3f;
                var crease = 1f + 0.23f * Mathf.Sin(i * Mathf.PI * 6f / Sides);
                vertices[ring * Sides + i] = new Vector3(Mathf.Cos(angle) * radius * crease + t * t * 0.0013f,
                    Mathf.Sin(angle) * radius * crease, t * JointShape.TwistLength);
                if (ring == rings) continue;
                var a = ring * Sides + i; var b = ring * Sides + (i + 1) % Sides;
                triangles.AddRange(new[] { a, b, b + Sides, a, b + Sides, a + Sides });
            }
        }
        mesh.vertices = new Il2CppStructArray<Vector3>(vertices);
        mesh.triangles = new Il2CppStructArray<int>(triangles.ToArray());
        mesh.RecalculateNormals(); mesh.RecalculateBounds();
    }

    public void Dispose()
    {
        foreach (var mesh in _meshes) if (mesh != null) Object.Destroy(mesh);
        foreach (var material in _materials) if (material != null) Object.Destroy(material);
        if (_paperTexture != null) Object.Destroy(_paperTexture);
        _paperTexture = null;
        _meshes.Clear(); _materials.Clear();
    }
}
