using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace NivalisMods.Cigarette;

// Reconstruct the world image from R(x+dx,y+dy), G(x+dx,y+dy), B(x+dx,y+dy).
// Each source channel contributes exactly once; there is no coloured overlay.
internal sealed class ScreenRgbShift : IDisposable
{
    private readonly Camera _camera;
    private readonly CommandBuffer _commands;
    private readonly int _pass;
    private readonly Mesh[] _meshes = new Mesh[13];
    private readonly Material[] _materials = new Material[13];
    private readonly Il2CppStructArray<Vector2> _uv = new(4);
    private RenderTexture? _source, _target;

    internal ScreenRgbShift(Camera camera)
    {
        _camera = camera;
        _commands = new CommandBuffer { name = "Cigarette: independent RGB channels" };
        try
        {
            var shader = Shader.Find("Particles/Standard Unlit");
            if (shader == null || !shader.isSupported) throw new InvalidOperationException("RGB channel shader unavailable.");
            _pass = -1;
            for (var i = 0; i < shader.passCount; i++)
            {
                // This game's serialized shader stores LIGHTMODE/FORWARDBASE in capitals.
                var mode = shader.FindPassTagValue(i, new ShaderTagId("LIGHTMODE")).name;
                if (string.IsNullOrEmpty(mode)) mode = shader.FindPassTagValue(i, new ShaderTagId("LightMode")).name;
                if (string.Equals(mode, "ForwardBase", StringComparison.OrdinalIgnoreCase)) { _pass = i; break; }
            }
            if (_pass < 0) throw new InvalidOperationException("RGB shader ForwardBase pass missing.");
            var masks = new[] { Color.red, Color.green, Color.blue };
            var weights = new[] { 1f / 16f, 4f / 16f, 6f / 16f, 4f / 16f, 1f / 16f };
            for (var channel = 0; channel < _materials.Length; channel++)
            {
                var material = new Material(shader) { name = "Cigarette.RGB." + channel };
                _materials[channel] = material;
                // Opaque RGB samples with additive channel composition: source alpha does
                // not participate, and zero offsets reproduce the original RGB exactly.
                // Keep blur weights in linear vertex data, avoiding material colour-space conversion.
                var weight = channel < 3 ? 1f : weights[(channel - 3) % 5];
                material.SetColor("_Color", channel < 3 ? masks[channel] : Color.white);
                material.SetInt("_SrcBlend", (int)BlendMode.One);
                material.SetInt("_DstBlend", (int)BlendMode.One);
                material.SetInt("_BlendOp", (int)BlendOp.Add);
                material.SetInt("_ZWrite", 0);
                material.SetInt("_Cull", (int)CullMode.Off);
                material.SetShaderPassEnabled("Always", false); // No particle distortion GrabPass.
                var mesh = new Mesh { name = "Cigarette.RGBQuad." + channel };
                _meshes[channel] = mesh;
                mesh.vertices = new Il2CppStructArray<Vector3>(new[] {
                    new Vector3(-1,-1,0), new Vector3(1,-1,0), new Vector3(1,1,0), new Vector3(-1,1,0) });
                var vertexColour = new Color(weight, weight, weight, 1f);
                mesh.colors = new Il2CppStructArray<Color>(new[] { vertexColour, vertexColour, vertexColour, vertexColour });
                mesh.triangles = new Il2CppStructArray<int>(new[] { 0, 1, 2, 0, 2, 3 });
                mesh.MarkDynamic();
            }
            camera.AddCommandBuffer(CameraEvent.AfterEverything, _commands);
            Plugin.Logger.LogInfo($"Cannabis image compositor ready: shader {shader.name}, forward pass {_pass}/{shader.passCount}; RGB and zoom-independent blur.");
        }
        catch { Dispose(); throw; }
    }

    internal void Prepare(float pixels, float blurPixels = 0f)
    {
        _commands.Clear();
        if (pixels <= 0f && blurPixels <= 0f) return;
        var width = _camera.pixelWidth; var height = _camera.pixelHeight;
        if (width <= 0 || height <= 0) return;
        if (_source == null || _source.width != width || _source.height != height)
        {
            ReleaseTextures();
            _source = MakeTexture(width, height, "Cigarette.RGBSource");
            _target = MakeTexture(width, height, "Cigarette.RGBResult");
            for (var i = 0; i < _materials.Length; i++)
                _materials[i].mainTexture = i >= 3 && i < 8 ? _target : _source;
        }
        _commands.Blit(new RenderTargetIdentifier(BuiltinRenderTextureType.CameraTarget), new RenderTargetIdentifier(_source));
        _commands.SetRenderTarget(new RenderTargetIdentifier(_target));
        _commands.ClearRenderTarget(false, true, Color.black);
        _commands.SetViewport(new Rect(0, 0, width, height));
        _commands.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);
        var fog = Shader.GetGlobalVector("unity_FogParams");
        _commands.SetGlobalVector("unity_FogParams", new Vector4(0, 0, 0, 1));
        for (var channel = 0; channel < 3; channel++)
        {
            var offset = RgbOffsets.Pixels(channel, Time.time, pixels);
            SetOffset(channel, offset.X / width, offset.Y / height);
            _commands.DrawMesh(_meshes[channel], Matrix4x4.identity, _materials[channel], 0, _pass);
        }
        if (blurPixels > 0.001f)
        {
            // Separate meshes/materials for both axes: recorded commands must not share
            // UV data that is overwritten before the GPU executes the first blur pass.
            for (var axis = 0; axis < 2; axis++)
            {
                _commands.SetRenderTarget(new RenderTargetIdentifier(axis == 0 ? _source : _target));
                _commands.ClearRenderTarget(false, true, Color.black);
                for (var tap = 0; tap < 5; tap++)
                {
                    var index = 3 + axis * 5 + tap;
                    var offset = (tap - 2) * blurPixels * 0.5f;
                    SetOffset(index, axis == 0 ? offset / width : 0f, axis == 1 ? offset / height : 0f);
                    _commands.DrawMesh(_meshes[index], Matrix4x4.identity, _materials[index], 0, _pass);
                }
            }
        }
        _commands.SetGlobalVector("unity_FogParams", fog);
        _commands.Blit(new RenderTargetIdentifier(_target), new RenderTargetIdentifier(BuiltinRenderTextureType.CameraTarget));
        _commands.SetViewProjectionMatrices(_camera.worldToCameraMatrix, _camera.projectionMatrix);
    }

    private void SetOffset(int mesh, float x, float y)
    {
        for (var vertex = 0; vertex < 4; vertex++)
        {
            var u = (vertex == 1 || vertex == 2 ? 1f : 0f) + x;
            var v = (vertex >= 2 ? 1f : 0f) + y;
            _uv[vertex] = new Vector2(u, SystemInfo.graphicsUVStartsAtTop ? 1f - v : v);
        }
        _meshes[mesh].uv = _uv;
    }

    private static RenderTexture MakeTexture(int width, int height, string name)
    {
        var texture = new RenderTexture(width, height, 0, RenderTextureFormat.Default)
        { name = name, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        if (texture.Create()) return texture;
        Object.Destroy(texture);
        throw new InvalidOperationException("Could not allocate RGB channel render texture.");
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
        _commands.Release(); ReleaseTextures();
        foreach (var mesh in _meshes) if (mesh != null) Object.Destroy(mesh);
        foreach (var material in _materials) if (material != null) Object.Destroy(material);
    }
}
