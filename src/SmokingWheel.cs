using HarmonyLib;
#if CIGARETTE_API
using NivalisMods.Cigarette.Api;
#endif
using System.Reflection;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
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
            !RailingTarget.TryCandidate(character, out var hit, out _)) return;
        _character = character; _rail = hit.collider;
        _scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle;
        _wheel = wheel;
        try
        {
            EnsureIcons();
            wheel.Clear();
            foreach (var kind in new[] { SmokeKind.Cigarette, SmokeKind.Joint, SmokeKind.Mask })
            {
#if CIGARETTE_API
                var availability = Plugin.Api.Items.GetAvailability((SmokingItem)kind);
                if (availability == ItemAvailability.Hidden) continue;
#endif
                var selected = kind;
                var label = kind == SmokeKind.Mask ? "Inhalant mask" : kind.ToString();
                wheel.AddAction(_icons[(int)kind], label, false, (Il2CppSystem.Action)(() =>
                {
#if CIGARETTE_API
                    if (Plugin.Api.Items.GetAvailability((SmokingItem)selected) != ItemAvailability.Available) return;
#endif
                    _choice = selected;
                    _choiceFrame = Time.frameCount;
                    _deadline = Time.unscaledTime + 2f;
                    // Native ClickExecuted closes the wheel after this callback.
                }),
#if CIGARETTE_API
                    availability == ItemAvailability.Available
#else
                    true
#endif
                );
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
        if (!_choice.HasValue || Time.frameCount <= _choiceFrame || Time.frameCount <= _closedFrame) return null;
        if (Time.unscaledTime > _deadline) { Close(); return null; }
        // Wait for the native window to release its gameplay locks. The controller
        // revalidates hands, movement and the railing before starting the session.
        if (!SmokingController.GameplayAvailable()) return null;
        var choice = _choice;
        _choice = null;
        if (!RailingTarget.TryCandidate(_character, out var hit, out _) || hit.collider != _rail) return null;
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

    internal bool OwnsIcon(Sprite sprite) => sprite != null && _icons.Any(icon => icon != null && icon == sprite);

    private bool IconsValid()
    {
        if (_icons.Count != 3 || _textures.Count != 3) return false;
        try
        {
            return Enumerable.Range(0, 3).All(i => _icons[i] != null && _textures[i] != null &&
                _icons[i].texture == _textures[i]);
        }
        catch { return false; } // A stale IL2CPP wrapper may throw from Sprite.texture.
    }

    private void EnsureIcons()
    {
        // Managed list entries can outlive their native Unity assets after cleanup.
        // Check both objects and the binding before reusing a cached icon set.
        if (IconsValid()) return;
        DestroyIcons();
        foreach (var kind in new[] { SmokeKind.Cigarette, SmokeKind.Joint, SmokeKind.Mask })
        {
            var name = kind.ToString().ToLowerInvariant() + ".png";
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Cigarette.Icons." + name)
                ?? throw new InvalidDataException("Missing smoking icon: " + name);
            using var bytes = new MemoryStream();
            stream.CopyTo(bytes);
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
                {
                    name = "Cigarette.Wheel." + kind, filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    // This managed cache is not a native scene asset reference.
                    hideFlags = HideFlags.DontUnloadUnusedAsset
                };
            _textures.Add(texture);
            if (!ImageConversion.LoadImage(texture, new Il2CppStructArray<byte>(bytes.ToArray()), false))
                throw new InvalidDataException("Cannot decode smoking icon: " + name);
            var sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), texture.width);
            _icons.Add(sprite);
            sprite.name = "Cigarette.Wheel." + kind;
            sprite.hideFlags = HideFlags.DontUnloadUnusedAsset;
        }
    }

    internal void Dispose()
    {
        try { Close(); }
        finally { DestroyIcons(); }
    }

    private void DestroyIcons()
    {
        // Protected assets are explicitly destroyed when replaced or disposed.
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

// Native wheel items can retain an override/material from a previous action.
// Match Photo Companion's workaround, scoped strictly to our three icons.
[HarmonyPatch(typeof(RadialMenuItemUI), nameof(RadialMenuItemUI.Init))]
internal static class SmokingWheelIconDisplay
{
    [HarmonyPostfix]
    private static void Postfix(RadialMenuItemUI __instance, Sprite __1)
    {
        if (SmokingController.Instance?.OwnsWheelIcon(__1) != true || __instance.icon == null) return;
        try
        {
            var image = __instance.icon;
            image.overrideSprite = null;
            image.material = null;
            image.type = UnityEngine.UI.Image.Type.Simple;
            image.preserveAspect = true;
            image.SetAllDirty();
        }
        catch (Exception e) { Plugin.Logger.LogWarning("Restoring smoking icon display: " + e.Message); }
    }
}
