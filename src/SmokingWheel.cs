using HarmonyLib;
using System.Reflection;
using Nivalis;
using Nivalis.UI;
using UnityEngine;
using Object = UnityEngine.Object;

namespace NivalisMods.Cigarette;

// The native wheel owns cursor, input, selection, controller navigation and close.
internal sealed class SmokingWheel
{
    private static RadialMenuUI? _openingWheel;
    private static int _openingFrame = -1;
    private RadialMenuUI? _wheel;
    private readonly List<Sprite> _icons = new();
    private readonly List<Texture2D> _textures = new();
    private SmokeKind? _choice;
    private int _choiceFrame, _scene, _closedFrame = -1;
    private float _deadline;
    private PlayerCharacter? _character;
    private Collider? _rail;
    internal bool Busy => _wheel != null || _choice.HasValue || Time.frameCount == _closedFrame;

    internal void Open()
    {
        var wheel = RadialMenuUI.instance;
        var character = PlayerManager._instance?.LocalPlayer?.Character;
        if (wheel == null || wheel.IsOpen || character == null ||
            !RailingTarget.TryTarget(character, out var hit, out _)) return;
        _character = character; _rail = hit.collider;
        _scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle;
        _wheel = wheel;
        try
        {
            EnsureIcons();
            wheel.Clear();
            foreach (var kind in new[] { SmokeKind.Cigarette, SmokeKind.Joint, SmokeKind.Mask })
            {
                var selected = kind;
                var label = kind == SmokeKind.Mask ? "Inhalant mask" : kind.ToString();
                wheel.AddAction(_icons[(int)kind], label, false, (Il2CppSystem.Action)(() =>
                {
                    _choice = selected;
                    _choiceFrame = Time.frameCount;
                    _deadline = Time.unscaledTime + 2f;
                    // Native ClickExecuted closes the wheel after this callback.
                }));
            }
            wheel.AddCloseAction();
            _openingWheel = wheel;
            _openingFrame = Time.frameCount;
            wheel.Open();
            RailPrompt.Release();
        }
        catch { Close(); throw; }
    }

    internal SmokeKind? Tick()
    {
        if (_wheel == null && !_choice.HasValue) return null;
        if (!Application.isFocused || _character == null || _rail == null ||
            PlayerManager._instance?.LocalPlayer?.Character != _character ||
            UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle != _scene)
        { Close(); return null; }
        if (_wheel != null)
        {
            if (_wheel.IsOpen) return null;
            _wheel = null;
            _closedFrame = Time.frameCount;
        }
        if (!_choice.HasValue || Time.frameCount <= _choiceFrame) return null;
        if (Time.unscaledTime > _deadline) { Close(); return null; }
        // Wait for the native window to release its gameplay locks. The controller
        // revalidates hands, movement and the railing before starting the session.
        if (!SmokingController.GameplayAvailable()) return null;
        var choice = _choice;
        _choice = null;
        if (!RailingTarget.TryTarget(_character, out var hit, out _) || hit.collider != _rail) return null;
        return choice;
    }

    internal void Close()
    {
        var wheel = _wheel;
        _wheel = null; _choice = null; _character = null; _rail = null;
        _closedFrame = Time.frameCount;
        if (wheel != null && wheel.IsOpen) wheel.Close();
    }

    internal static bool AllowClick(RadialMenuUI wheel) =>
        wheel != _openingWheel || Time.frameCount > _openingFrame;

    private void EnsureIcons()
    {
        if (_icons.Count == 3) return;
        foreach (var icon in _icons) if (icon != null) Object.Destroy(icon);
        foreach (var texture in _textures) if (texture != null) Object.Destroy(texture);
        _icons.Clear(); _textures.Clear();
        foreach (var kind in new[] { SmokeKind.Cigarette, SmokeKind.Joint, SmokeKind.Mask })
        {
            var name = kind.ToString().ToLowerInvariant() + ".png";
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Cigarette.Icons." + name)
                ?? throw new InvalidDataException("Missing smoking icon: " + name);
            using var bytes = new MemoryStream();
            stream.CopyTo(bytes);
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
                { name = "Cigarette.Wheel." + kind, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            _textures.Add(texture);
            if (!ImageConversion.LoadImage(texture, bytes.ToArray()))
                throw new InvalidDataException("Cannot decode smoking icon: " + name);
            var sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), texture.width);
            _icons.Add(sprite);
        }
    }

    internal void Dispose()
    {
        Close();
        foreach (var icon in _icons) if (icon != null) Object.Destroy(icon);
        foreach (var texture in _textures) if (texture != null) Object.Destroy(texture);
        _icons.Clear(); _textures.Clear();
    }
}

// The click that opens this menu must not select an item in that same frame.
[HarmonyPatch(typeof(RadialMenuUI), nameof(RadialMenuUI.ClickExecuted))]
internal static class SmokingWheelOpeningClick
{
    [HarmonyPrefix]
    private static bool Prefix(RadialMenuUI __instance) => SmokingWheel.AllowClick(__instance);
}
