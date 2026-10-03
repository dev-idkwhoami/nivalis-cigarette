using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Attributes;
using Nivalis;
using Nivalis.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using Object = UnityEngine.Object;

namespace NivalisMods.Cigarette;

public sealed class SmokingController : MonoBehaviour
{
    private const string IdleClip = "IN1_X0185.FPV Idle_f 1_Fix";
    private const string IdleState = "Base Layer.Movement Tree";
    internal static SmokingController? Instance;
    private PlayerHandsAnimator? _hands;
    private bool _active, _exiting;
    private readonly List<DroppedCigarette> _drops = new();
    private StanceControl? _stance;
    private CustomDraw? _customDraw;
    private RuntimeAnimatorController? _originalController;
    private AnimatorOverrideController? _override;
    private OverrideableBool.OverrideLock? _movementLock;
    private OverrideableBool.OverrideLock? _interactionLock;
    private GameObject? _cigarette;
    private readonly List<Material> _materials = new();
    private readonly List<(Transform Bone, Vector3 Position, Quaternion Rotation, Vector3 Scale)> _bones = new();
    private readonly List<(Renderer Renderer, bool Enabled)> _renderers = new();
    private AnimatorStateInfo _originalState;
    private Vector3 _animatorPosition;
    private Quaternion _animatorRotation;
    private bool _applyRootMotion;
    private bool _fireEvents;
    private bool _rootMotion;
    private float _originalSpeed;
    private float _speedParameter;
    private float _started;
    private int _scene;
    private SmokingSession? _session;
    private SmokingHud? _hud;
    private Transform? _paper, _ember;

    public SmokingController(IntPtr pointer) : base(pointer) { }
    public void Awake() => Instance = this;

    public void Update()
    {
        try
        {
            for (var i = _drops.Count - 1; i >= 0; i--)
                if (!_drops[i].Tick()) { _drops[i].Dispose(); _drops.RemoveAt(i); }
            var keyboard = Keyboard.current;
            if (_active && (_hands == null ||
                PlayerManager._instance?.LocalPlayer?.Character?.Controller?.HandsAnimator != _hands ||
                UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle != _scene))
            {
                Stop(null);
                return;
            }
            if (_active && (!_exiting && (!GameplayAvailable() || Time.time - _started >= (_session?.Duration ?? 60f) ||
                Gamepad.current?.leftStick.ReadValue().sqrMagnitude > 0.1f))) BeginExit();
            if (_exiting)
            {
                if (!GameplayAvailable()) { _hud?.Restore(); _hud = null; }
                if (_customDraw?.ExitProgress >= 1f)
                {
                    // A stalled frame must not skip the release and destroy the held prop instead.
                    if (_cigarette != null && _hands != null) { Evaluate(_hands); DropCigarette(); }
                    Stop(null);
                }
                return;
            }
            if (_active && keyboard != null && (keyboard.escapeKey.wasPressedThisFrame ||
                keyboard.wKey.isPressed || keyboard.aKey.isPressed || keyboard.sKey.isPressed || keyboard.dKey.isPressed ||
                keyboard.upArrowKey.isPressed || keyboard.downArrowKey.isPressed || keyboard.leftArrowKey.isPressed ||
                keyboard.rightArrowKey.isPressed || keyboard.spaceKey.isPressed))
            {
                BeginExit();
                return;
            }
            if (!GameplayAvailable()) return;
            if (!_active && Mouse.current?.leftButton.wasPressedThisFrame == true && CanSmokeAtRail()) StartSmoking();
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError(e);
            Stop("Smoking failed; see the Cigarette log.");
        }
    }

    [HideFromIl2Cpp]
    private void StartSmoking()
    {
        Stop(null);
        var character = PlayerManager._instance.LocalPlayer.Character;
        var hands = character.Controller.HandsAnimator;
        if (hands == null || !character.Controller.CanMove || !character.CanEquipItem ||
            character.DrivenBoat != null || character.ObjectHolder.IsHoldingObject ||
            hands.IsSitting || hands.IsPlayingMealAnimation || hands.sleeping || hands.placingItem ||
            hands.IsHoldingTray || hands.Snapped || hands.Animator.GetBool(PlayerHandsAnimator.Param.Fishing))
        {
            Notice("Stand on foot with empty hands before smoking.");
            return;
        }
        if (!RailingTarget.TryTarget(character, out var railTop, out _)) return;
        var clipName = IdleClip;
        var clip = Resources.FindObjectsOfTypeAll<AnimationClip>().FirstOrDefault(c => c.name == clipName);
        if (clip == null)
        {
            Notice("Smoking clip is not loaded here. Try a populated street.");
            Plugin.Logger.LogWarning("Missing loaded clip: " + clipName);
            return;
        }
        var animator = hands.Animator;
        if (animator.IsInTransition(0) || animator.GetCurrentAnimatorStateInfo(0).fullPathHash != Animator.StringToHash(IdleState))
        {
            Notice("Wait until your hands are idle before smoking.");
            return;
        }
        if (animator.runtimeAnimatorController == null || !animator.HasState(0, Animator.StringToHash(IdleState)) ||
            !animator.runtimeAnimatorController.animationClips.Any(c => c.name == IdleClip))
            throw new InvalidOperationException("The player hands controller does not match the inspected build.");

        _originalController = animator.runtimeAnimatorController;
        _originalState = animator.GetCurrentAnimatorStateInfo(0);
        _originalSpeed = animator.speed;
        _speedParameter = animator.GetFloat(PlayerHandsAnimator.Param.Speed);
        _applyRootMotion = animator.applyRootMotion;
        _fireEvents = animator.fireEvents;
        _rootMotion = hands.rootMotion;
        _animatorPosition = animator.transform.localPosition;
        _animatorRotation = animator.transform.localRotation;
        foreach (var bone in hands.Rig.GetComponentsInChildren<Transform>(true))
            _bones.Add((bone, bone.localPosition, bone.localRotation, bone.localScale));
        foreach (var renderer in hands.renderers)
            if (renderer != null) _renderers.Add((renderer, renderer.enabled));
        _hands = hands; // Cleanup owns subsequent mutations, including partial startup failures.
        _active = true;
        _exiting = false;
        _stance = new StanceControl(hands, character.Controller);
        _stance.Place(character);
        _movementLock = character.Controller.DisableMovement(this);
        _interactionLock = character.Interaction.DisableInteractions(this);
        character.Controller.StopMovement();
        // Unity stripped both managed constructors; its native creation binding remains available.
        _override = new AnimatorOverrideController(IL2CPP.il2cpp_object_new(Il2CppClassPointerStore<AnimatorOverrideController>.NativeClassPtr));
        AnimatorOverrideController.Internal_Create(_override, _originalController);
        _override[IdleClip] = clip;
        animator.runtimeAnimatorController = _override;
        animator.applyRootMotion = false;
        animator.fireEvents = false; // NPC clip events must not invoke player interaction handlers.
        animator.speed = 1f;
        hands.rootMotion = false;
        animator.SetFloat(PlayerHandsAnimator.Param.Speed, 0f);
        foreach (var entry in _renderers) entry.Renderer.enabled = true;
        _session = new SmokingSession(Plugin.SessionDuration.Value, () => UnityEngine.Random.value);
        _started = Time.time;
        _scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle;
        Evaluate(hands);
        _stance.AlignCustomBody(animator, character.transform.position, character.Controller.Rotation);
        CreateCigarette(animator);
        if (_cigarette != null)
        {
            _customDraw = new CustomDraw(hands, character.Controller.Camera.transform, _cigarette.transform, character.Controller.Rotation, character, _session, railTop);
        }
        RailPrompt.Release();
        _hud = new SmokingHud();
        _hud.Hide();
    }

    [HideFromIl2Cpp]
    internal bool Owns(PlayerHandsAnimator hands) => _hands != null && _hands == hands;

    [HideFromIl2Cpp]
    internal void Evaluate(PlayerHandsAnimator hands)
    {
        // The game manually evaluates this disabled Animator. Replace only its active smoking update.
        var animator = hands.Animator;
        animator.Play(IdleState, 0, 0f);
        animator.Update(0f);
        animator.transform.localPosition = _animatorPosition;
        animator.transform.localRotation = _animatorRotation;
        _stance?.Apply();
        _customDraw?.Evaluate();
        if (_customDraw != null && _cigarette != null && _paper != null && _ember != null)
        {
            var burn = _customDraw.BurnProgress;
            var length = 0.064f * (1f - burn);
            _paper.localPosition = new Vector3(0f, 0f, length * 0.5f);
            _paper.localScale = new Vector3(0.007f, Mathf.Max(length, 0.00001f) * 0.5f, 0.007f);
            _ember.localPosition = new Vector3(0f, 0f, length + 0.001f);
            _paper.gameObject.SetActive(burn < 1f);
            _ember.gameObject.SetActive(burn < 1f);
        }
    }

    public void LateUpdate()
    {
        // Keep the world pose even if a camera-following ancestor moved after ManualUpdate.
        if (!_active) return;
        try
        {
            if (_hands != null) Evaluate(_hands);
            else _stance?.Apply();
            if (_exiting && _customDraw?.ExitProgress >= 0.28f && _cigarette != null) DropCigarette();
        }
        catch (Exception e) { Plugin.Logger.LogError(e); Stop("Custom draw failed; controls restored."); }
    }

    [HideFromIl2Cpp]
    private void CreateCigarette(Animator animator)
    {
        var left = false;
        var hand = animator.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
        if (hand == null) { Plugin.Logger.LogWarning("No humanoid hand bone; playing animation without cigarette."); return; }
        var index = animator.GetBoneTransform(left ? HumanBodyBones.LeftIndexIntermediate : HumanBodyBones.RightIndexIntermediate);
        var middle = animator.GetBoneTransform(left ? HumanBodyBones.LeftMiddleIntermediate : HumanBodyBones.RightMiddleIntermediate);
        _cigarette = new GameObject("Cigarette.HeldCigarette");
        _cigarette.transform.SetParent(hand, false);
        if (index != null && middle != null) _cigarette.transform.position = (index.position + middle.position) * 0.5f;
        else _cigarette.transform.localPosition = new Vector3(0f, 0.025f, 0.06f);
        var fingerTip = animator.GetBoneTransform(left ? HumanBodyBones.LeftIndexDistal : HumanBodyBones.RightIndexDistal);
        if (index != null && middle != null && fingerTip != null)
        {
            var normal = Vector3.Cross(fingerTip.position - index.position, middle.position - index.position).normalized;
            if (normal.sqrMagnitude > 0.1f) _cigarette.transform.rotation = Quaternion.LookRotation(left ? -normal : normal, hand.up);
        }
        var layer = _renderers.Count > 0 ? _renderers[0].Renderer.gameObject.layer : hand.gameObject.layer;
        Cylinder("Paper", new Vector3(0f, 0f, 0.032f), 0.064f, new Color(0.9f, 0.88f, 0.8f), layer);
        Cylinder("Filter", new Vector3(0f, 0f, -0.01f), 0.02f, new Color(0.64f, 0.36f, 0.13f), layer);
        Cylinder("Ember", new Vector3(0f, 0f, 0.065f), 0.003f, new Color(1f, 0.18f, 0.015f), layer, true);
    }

    [HideFromIl2Cpp]
    private void Cylinder(string name, Vector3 position, float length, Color color, int layer, bool glow = false)
    {
        var part = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        part.name = name;
        if (name == "Paper") _paper = part.transform;
        if (name == "Ember") _ember = part.transform;
        part.layer = layer;
        part.transform.SetParent(_cigarette!.transform, false);
        part.transform.localPosition = position;
        part.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        part.transform.localScale = new Vector3(0.007f, length / 2f, 0.007f);
        var collider = part.GetComponent<Collider>();
        collider.enabled = false;
        Object.Destroy(collider);
        var shader = Shader.Find("Standard") ?? Shader.Find("Unlit/Color");
        if (shader == null) throw new InvalidOperationException("No shader available for cigarette.");
        var material = new Material(shader);
        _materials.Add(material);
        material.color = color;
        if (glow && material.HasProperty("_EmissionColor"))
        {
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", color * 2f);
        }
        part.GetComponent<Renderer>().sharedMaterial = material;
    }

    [HideFromIl2Cpp]
    private void BeginExit()
    {
        if (!_active || _exiting) return;
        if (_customDraw == null) { Stop(null); return; }
        _exiting = true;
        _customDraw.BeginExit();
        // Menus/focus loss must never wait on the lowering animation to restore their UI.
        if (!GameplayAvailable()) { _hud?.Restore(); _hud = null; }
    }

    [HideFromIl2Cpp]
    private void DropCigarette()
    {
        if (_cigarette == null) return;
        var drop = new DroppedCigarette(_cigarette, _materials.ToArray(), _scene, PlayerManager._instance.LocalPlayer.Character.transform);
        _drops.Add(drop);
        _cigarette = null;
        _paper = _ember = null;
        _materials.Clear(); // Ownership moves to the falling prop until it despawns.
    }

    [HideFromIl2Cpp]
    internal void Stop(string? message)
    {
        var hands = _hands;
        _active = false;
        _exiting = false;
        _hands = null; // Always release Harmony interception, even if a Unity object has been destroyed.
        try { _customDraw?.Restore(); }
        catch (Exception e) { Plugin.Logger.LogError("Restoring hand IK: " + e); }
        _customDraw = null;
        try
        {
            if (hands != null && hands.Animator != null && _originalController != null)
            {
                var animator = hands.Animator;
                animator.runtimeAnimatorController = _originalController;
                animator.applyRootMotion = _applyRootMotion;
                animator.fireEvents = _fireEvents;
                animator.speed = _originalSpeed;
                animator.SetFloat(PlayerHandsAnimator.Param.Speed, _speedParameter);
                hands.rootMotion = _rootMotion;
                foreach (var entry in _bones)
                    if (entry.Bone != null)
                    {
                        entry.Bone.localPosition = entry.Position;
                        entry.Bone.localRotation = entry.Rotation;
                        entry.Bone.localScale = entry.Scale;
                    }
                animator.Play(_originalState.fullPathHash, 0, _originalState.normalizedTime);
                animator.Update(0f);
            }
        }
        catch (Exception e) { Plugin.Logger.LogError("Restoring hands: " + e); }
        finally
        {
            try { _stance?.Restore(); }
            catch (Exception e) { Plugin.Logger.LogError("Restoring look limits: " + e); }
            _stance = null;
            try { _hud?.Restore(); }
            catch (Exception e) { Plugin.Logger.LogError("Restoring HUD: " + e); }
            _hud = null;
            Release(ref _movementLock);
            Release(ref _interactionLock);
            foreach (var entry in _renderers)
                if (entry.Renderer != null) entry.Renderer.enabled = entry.Enabled;
            _renderers.Clear();
            _bones.Clear();
            if (_cigarette != null) Object.Destroy(_cigarette);
            _cigarette = null;
            _paper = _ember = null;
            _session = null;
            foreach (var material in _materials) if (material != null) Object.Destroy(material);
            _materials.Clear();
            if (_override != null) Object.Destroy(_override);
            _override = null;
            _originalController = null;
        }
        if (message != null) Notice(message);
    }

    [HideFromIl2Cpp]
    private static void Release(ref OverrideableBool.OverrideLock? token)
    {
        try { token?.Release(); }
        catch (Exception e) { Plugin.Logger.LogWarning("Releasing smoking lock: " + e.Message); }
        finally { token = null; }
    }

    [HideFromIl2Cpp]
    private void Notice(string text)
    {
        Plugin.Logger.LogWarning(text);
    }

    [HideFromIl2Cpp]
    private static bool GameplayAvailable()
    {
        if (!Application.isFocused || Time.timeScale == 0f || PlayerManager._instance?.LocalPlayer?.Character == null) return false;
        var ui = UIManager._instance;
        if (ui == null || !UIManager.IsVisible || ui._openPanels == null || Nivalis.PlayerInputManager._instance?.currentRebind != null) return false;
        foreach (var panel in ui._openPanels)
            if (panel != null && panel.gameObject.activeInHierarchy && panel.IsVisible &&
                (panel.requiresMouse || panel.TryCast<UIWindow>()?.IsOpen == true)) return false;
        return true;
    }

    [HideFromIl2Cpp]
    internal bool CanSmokeAtRail()
    {
        if (_active || !GameplayAvailable()) return false;
        var character = PlayerManager._instance.LocalPlayer.Character;
        var hands = character.Controller.HandsAnimator;
        if (hands == null || !character.Controller.CanMove || !character.CanEquipItem ||
            character.Interaction.CurrentFocus != null || !character.Interaction.InteractionActive ||
            character.DrivenBoat != null || character.ObjectHolder.IsHoldingObject || hands.Snapped ||
            hands.IsSitting || hands.IsPlayingMealAnimation || hands.sleeping || hands.placingItem || hands.IsHoldingTray ||
            hands.Animator.GetBool(PlayerHandsAnimator.Param.Fishing)) return false;
        if (hands.Animator.IsInTransition(0) || hands.Animator.GetCurrentAnimatorStateInfo(0).fullPathHash != Animator.StringToHash(IdleState)) return false;
        return RailingTarget.TryTarget(character, out _, out _);
    }

    public void OnDisable() { RailPrompt.Release(); Stop(null); foreach (var drop in _drops) drop.Dispose(); _drops.Clear(); }
    public void OnDestroy() { Stop(null); foreach (var drop in _drops) drop.Dispose(); _drops.Clear(); if (Instance == this) Instance = null; }
}

[HarmonyPatch(typeof(PlayerHandsAnimator), nameof(PlayerHandsAnimator.ManualUpdate))]
internal static class HandsUpdatePatch
{
    [HarmonyPrefix]
    private static bool Prefix(PlayerHandsAnimator __instance)
    {
        var smoking = SmokingController.Instance;
        if (smoking == null || !smoking.Owns(__instance)) return true;
        try { smoking.Evaluate(__instance); return false; }
        catch (Exception e)
        {
            Plugin.Logger.LogError(e);
            smoking.Stop("Animation failed; normal controls restored.");
            return true;
        }
    }
}

[HarmonyPatch(typeof(PlayerHandsAnimator), nameof(PlayerHandsAnimator.UpdateIK), new Type[] { })]
internal static class HandsIkPatch
{
    [HarmonyPrefix]
    private static bool Prefix(PlayerHandsAnimator __instance) => SmokingController.Instance?.Owns(__instance) != true;
}
