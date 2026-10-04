using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace NivalisMods.Cigarette;

// Warp the completed camera image on a UV grid. Uses a shipped shader, not a
// volume which may be blended away or an OnRenderImage callback skipped by PPv2.
internal sealed class ScreenWarp : IDisposable
{
    private const int Columns = 48, Rows = 32;
    private readonly Camera _camera;
    private readonly CommandBuffer _commands;
    private readonly Mesh _mesh;
    private readonly Material _material;
    private readonly Il2CppStructArray<Vector2> _uv = new((Columns + 1) * (Rows + 1));
    private RenderTexture? _source, _target;

    internal ScreenWarp(Camera camera)
    {
        _camera = camera;
        var shader = Shader.Find("Unlit/Texture");
        if (shader == null || !shader.isSupported) throw new InvalidOperationException("Unlit/Texture is unavailable for the screen warp.");
        _material = new Material(shader) { name = "Cigarette.ScreenWarp" };
        _mesh = new Mesh { name = "Cigarette.ScreenWarpGrid" };
        _commands = new CommandBuffer { name = "Cigarette: full-screen waves" };
        try
        {
            var vertices = new Vector3[_uv.Length];
            for (var y = 0; y <= Rows; y++)
            for (var x = 0; x <= Columns; x++)
                vertices[y * (Columns + 1) + x] = new Vector3(2f * x / Columns - 1f, 2f * y / Rows - 1f, 0f);
            var triangles = new List<int>();
            for (var y = 0; y < Rows; y++)
            for (var x = 0; x < Columns; x++)
            {
                var a = y * (Columns + 1) + x; var b = a + 1; var c = a + Columns + 1; var d = c + 1;
                // Both windings tolerate the platform's render-target Y inversion.
                triangles.AddRange(new[] { a, b, d, a, d, c, d, b, a, c, d, a });
            }
            _mesh.vertices = new Il2CppStructArray<Vector3>(vertices);
            _mesh.triangles = new Il2CppStructArray<int>(triangles.ToArray());
            _mesh.MarkDynamic();
            camera.AddCommandBuffer(CameraEvent.AfterEverything, _commands);
        }
        catch { Dispose(); throw; }
    }

    internal void Prepare(float strength)
    {
        _commands.Clear();
        if (strength <= 0f) return;
        var width = _camera.pixelWidth; var height = _camera.pixelHeight;
        if (width <= 0 || height <= 0) return;
        if (_source == null || _source.width != width || _source.height != height)
        {
            ReleaseTextures();
            _source = MakeTexture(width, height, "Cigarette.ScreenCopy");
            _target = MakeTexture(width, height, "Cigarette.WarpedScreen");
            _material.mainTexture = _source;
        }
        for (var y = 0; y <= Rows; y++)
        for (var x = 0; x <= Columns; x++)
        {
            var uv = HighMotion.Warp((float)x / Columns, (float)y / Rows, Time.time, strength);
            _uv[y * (Columns + 1) + x] = new Vector2(uv.X, SystemInfo.graphicsUVStartsAtTop ? 1f - uv.Y : uv.Y);
        }
        _mesh.uv = _uv;
        _commands.Blit(new RenderTargetIdentifier(BuiltinRenderTextureType.CameraTarget), new RenderTargetIdentifier(_source));
        _commands.SetRenderTarget(new RenderTargetIdentifier(_target));
        _commands.SetViewport(new Rect(0, 0, width, height));
        _commands.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);
        _commands.DrawMesh(_mesh, Matrix4x4.identity, _material, 0, 0);
        _commands.Blit(new RenderTargetIdentifier(_target), new RenderTargetIdentifier(BuiltinRenderTextureType.CameraTarget));
        _commands.SetViewProjectionMatrices(_camera.worldToCameraMatrix, _camera.projectionMatrix);
    }

    private static RenderTexture MakeTexture(int width, int height, string name)
    {
        var texture = new RenderTexture(width, height, 0, RenderTextureFormat.Default)
        { name = name, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        if (texture.Create()) return texture;
        Object.Destroy(texture);
        throw new InvalidOperationException("Could not allocate screen warp render texture.");
    }

    private void ReleaseTextures()
    {
        if (_source != null) { _source.Release(); Object.Destroy(_source); }
        if (_target != null) { _target.Release(); Object.Destroy(_target); }
        _source = null; _target = null;
    }

    public void Dispose()
    {
        if (_camera != null) _camera.RemoveCommandBuffer(CameraEvent.AfterEverything, _commands);
        _commands.Release();
        ReleaseTextures();
        Object.Destroy(_mesh); Object.Destroy(_material);
    }
}
