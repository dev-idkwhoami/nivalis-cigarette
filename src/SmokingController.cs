using HarmonyLib;
#if CIGARETTE_API
using NivalisMods.Cigarette.Api;
#endif
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
    private bool _standing;
    private SmokingEndReason _endReason;
#if CIGARETTE_API
    private int _effectScene = -1, _effectPlayer;
#endif
    private RailApproach? _approach;
    private readonly List<DroppedCigarette> _drops = new();
    private StanceControl? _stance;
    private CustomDraw? _customDraw;
    private RuntimeAnimatorController? _originalController;
    private AnimatorOverrideController? _override;
    private OverrideableBool.OverrideLock? _movementLock;
    private OverrideableBool.OverrideLock? _sprintLock;
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
    private bool _blunt;
    private SmokeKind _kind;
    private readonly SmokingWheel _wheel = new();
    private JointModel? _joint;
    private readonly CannabisEffects _high = new();
    private readonly MaskAfterEffect _maskEffect = new();

    public SmokingController(IntPtr pointer) : base(pointer) { }
    public void Awake() => Instance = this;

    [HideFromIl2Cpp]
    internal bool OwnsWheelIcon(Sprite sprite) => _wheel.OwnsIcon(sprite);

    public void Update()
    {
        try
        {
#if CIGARETTE_API
            SynchronizeEffectContext();
#endif
            CreditAfterEffects();
#if CIGARETTE_API
            if (GameplayAvailable() && !_active) Plugin.Api.TickEffects(Time.unscaledDeltaTime);
#endif
            _maskEffect.Tick(GameplayAvailable(), _active);
            _high.Tick(GameplayAvailable(), _active && _blunt, _active);
            for (var i = _drops.Count - 1; i >= 0; i--)
                if (!_drops[i].Tick()) { _drops[i].Dispose(); _drops.RemoveAt(i); }
            var keyboard = Keyboard.current;
            if (_approach != null)
            {
                var approach = _approach;
                var result = approach.Tick();
                if (result != 0)
                {
                    _approach = null;
                    approach.Dispose();
                    if (result > 0) StartSmoking(approach.Kind, approach.Top);
#if CIGARETTE_API
                    else Plugin.Api.End(SmokingEndReason.UserCancelled, "Approach cancelled, obstructed, or gameplay changed.");
#endif
                }
                return;
            }
            var choice = _wheel.Tick();
            if (choice.HasValue)
            {
#if CIGARETTE_API
                var result = _wheel.Standing ? TryStart(choice.Value, true) : Plugin.Api.Sessions.TryStart((SmokingItem)choice.Value);
                if (!result.Success) Notice(result.Message);
#else
                if (CanStartSmoking(_wheel.Standing))
                {
                    if (_wheel.Standing) { StartSmoking(choice.Value, default, true); return; }
                    var character = PlayerManager._instance.LocalPlayer.Character;
                    if (RailingTarget.TryCandidate(character, out var top, out _) &&
                        RailingTarget.TryFacing(character, top, out var facing))
                    {
                        _approach = new RailApproach(character, top, facing, choice.Value);
                        _approach.Begin(this);
                    }
                }
#endif
                return;
            }
            if (_wheel.Busy) return;
            if (_active && (_hands == null ||
                PlayerManager._instance?.LocalPlayer?.Character?.Controller?.HandsAnimator != _hands ||
                UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle != _scene))
            {
                Stop(null, SmokingEndReason.SceneChanged);
                return;
            }
            if (_active && (!_exiting && ((!_standing && !GameplayAvailable()) || (_kind == SmokeKind.Mask ? Time.unscaledTime : Time.time) - _started >= (_session?.Duration ?? 60f) ||
                (!_standing && Gamepad.current?.leftStick.ReadValue().sqrMagnitude > 0.1f))))
                BeginExit(!_standing && !GameplayAvailable() ? SmokingEndReason.GameplayUnavailable :
                    (_kind == SmokeKind.Mask ? Time.unscaledTime : Time.time) - _started >= (_session?.Duration ?? 60f)
                        ? SmokingEndReason.Completed : SmokingEndReason.UserCancelled);
            if (_active && _standing)
            {
                if (FreeSessionConflict()) { Stop(null); return; }
                UpdateSprintLock();
                if (!_exiting && Plugin.StandingSmokePressed()) { BeginExit(); return; }
            }
            if (_exiting)
            {
                if (!GameplayAvailable()) { _hud?.Restore(); _hud = null; }
                if (_customDraw?.ExitProgress >= 1f)
                {
                    // A stalled frame must not skip the release and destroy the held prop instead.
                    if (_cigarette != null && _hands != null) { Evaluate(_hands); DropCigarette(); }
                    Stop(null, _endReason);
                }
                return;
            }
            if (_active && !_standing && keyboard != null && (keyboard.escapeKey.wasPressedThisFrame ||
                keyboard.wKey.isPressed || keyboard.aKey.isPressed || keyboard.sKey.isPressed || keyboard.dKey.isPressed ||
                keyboard.upArrowKey.isPressed || keyboard.downArrowKey.isPressed || keyboard.leftArrowKey.isPressed ||
                keyboard.rightArrowKey.isPressed || keyboard.spaceKey.isPressed))
            {
                BeginExit();
                return;
            }
            if (!GameplayAvailable()) return;
            if (!_active && Plugin.StandingSmokePressed() && CanStartSmoking(true)) { _wheel.Open(true); return; }
            if (!_active && Mouse.current?.leftButton.wasPressedThisFrame == true && CanSmokeAtRail()) _wheel.Open();
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError(e);
            _maskEffect.Clear();
            Stop("Smoking failed; see the Cigarette log.");
        }
#if CIGARETTE_API
        finally { Plugin.Api.DispatchEvents(); }
#endif
    }

#if CIGARETTE_API
    [HideFromIl2Cpp]
    private void FailStart(ResultCode code, string message)
    {
        Plugin.Api.End(SmokingEndReason.Failed, message, code);
        Notice(message);
    }

    [HideFromIl2Cpp]
    internal ApiResult ValidatePosition()
    {
        var character = PlayerManager._instance?.LocalPlayer?.Character;
        if (character?.Controller?.HandsAnimator == null || character.Controller.Camera == null)
            return new(ResultCode.NotReady, "No player hands/camera.");
        return RailingTarget.TryCandidate(character, out _, out var reason)
            ? new(ResultCode.Success) : new(ResultCode.InvalidPosition, reason);
    }

    [HideFromIl2Cpp]
    internal ApiResult ValidateStart(SmokeKind kind, bool standing = false)
    {
        if (_active || _approach != null || _wheel.Busy) return new(ResultCode.Busy, "Smoking or the smoking wheel is already active.");
        if (!GameplayAvailable()) return new(ResultCode.GameplayUnavailable, "Gameplay is paused, unfocused, or blocked by UI.");
        var position = standing ? new ApiResult(ResultCode.Success) : ValidatePosition();
        if (!position.Success) return position;
        if (!CanStartSmoking(standing)) return new(ResultCode.PlayerNotReady, "Stand idle on foot with empty hands and no active interaction.");
        if (kind == SmokeKind.Mask && !MaskModel.Available()) return new(ResultCode.AssetsUnavailable, "Inhalant mask assets are not loaded here.");
        if (!standing && !Resources.FindObjectsOfTypeAll<AnimationClip>().Any(c => c.name == IdleClip))
            return new(ResultCode.AssetsUnavailable, "Smoking animation is not loaded here.");
        return new(ResultCode.Success);
    }

    [HideFromIl2Cpp]
    internal ApiResult TryStart(SmokeKind kind, bool standing = false)
    {
        if (Plugin.Api.Items.GetAvailability((SmokingItem)kind) != ItemAvailability.Available)
            return new(ResultCode.ItemDisabled, "Smoking item is disabled or hidden.");
        if (Plugin.Api.Sessions.GetCurrent() != null) return new(ResultCode.Busy, "A smoking sequence is already active.");
        var validation = ValidateStart(kind, standing);
        if (!validation.Success) return validation;
        var character = PlayerManager._instance.LocalPlayer.Character;
        if (standing)
        {
            var standingId = Plugin.Api.Accept((SmokingItem)kind, Plugin.Api.Items.GetSettings((SmokingItem)kind).SessionDurationSeconds);
            try { StartSmoking(kind, default, true); }
            catch (Exception e) { Plugin.Logger.LogError(e); Stop("Failed to start standing smoking."); }
            return _active ? new(ResultCode.Success, "Standing smoking started.", standingId)
                : new(ResultCode.Failed, "Standing smoking could not start.", standingId);
        }
        if (!RailingTarget.TryCandidate(character, out var top, out var reason) ||
            !RailingTarget.TryFacing(character, top, out var facing)) return new(ResultCode.InvalidPosition, reason);
        var id = Plugin.Api.Accept((SmokingItem)kind, Plugin.Api.Items.GetSettings((SmokingItem)kind).SessionDurationSeconds);
        try
        {
            _approach = new RailApproach(character, top, facing, kind);
            _approach.Begin(this);
            return new(ResultCode.Success, "Approach accepted; wait for SmokingStarted.", id);
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError(e);
            Stop("Failed to begin smoking approach.");
            return new(ResultCode.Failed, "Failed to begin smoking approach.", id);
        }
    }

    [HideFromIl2Cpp]
    internal ApiResult StopFromApi()
    {
        if (_approach != null) Stop(null, SmokingEndReason.ApiStopped);
        else if (_active) BeginExit(SmokingEndReason.ApiStopped);
        return new(ResultCode.Success);
    }

    [HideFromIl2Cpp]
    internal void RefreshEffects()
    {
        SynchronizeEffectContext();
        _high.RefreshPermissions();
        _maskEffect.RefreshPermissions();
    }

    [HideFromIl2Cpp]
    private void SynchronizeEffectContext()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle;
        var player = PlayerManager._instance?.LocalPlayer?.Character?.GetInstanceID() ?? 0;
        if (_effectScene == scene && _effectPlayer == player) return;
        if (_effectScene != -1) Plugin.Api.ClearDirectEffects();
        _high.Dispose(); _maskEffect.Clear();
        _effectScene = scene; _effectPlayer = player;
    }
#endif

    [HideFromIl2Cpp]
    internal void AvailabilityChanged(SmokeKind kind)
    {
        if (_wheel.Busy) _wheel.Close();
        if (_approach?.Kind == kind && !Plugin.ItemAvailable(kind))
#if CIGARETTE_API
            Stop(null, SmokingEndReason.AvailabilityChanged);
#else
            Stop(null);
#endif
        RailPrompt.Release();
    }

    [HideFromIl2Cpp]
    private void StartSmoking(SmokeKind kind, RaycastHit target, bool standing = false)
    {
#if CIGARETTE_API
        if (Plugin.Api.Items.GetAvailability((SmokingItem)kind) != ItemAvailability.Available)
        { Plugin.Api.End(SmokingEndReason.AvailabilityChanged, "Item was disabled during approach.", ResultCode.ItemDisabled); return; }
        if (!GameplayAvailable())
        { Plugin.Api.End(SmokingEndReason.GameplayUnavailable, "Gameplay unavailable.", ResultCode.GameplayUnavailable); return; }
#else
        Stop(null);
        if (!Plugin.ItemAvailable(kind)) return;
#endif
        var character = PlayerManager._instance.LocalPlayer.Character;
        var hands = character.Controller.HandsAnimator;
        if (hands == null || !character.Controller.CanMove || !character.CanEquipItem ||
            character.DrivenBoat != null || character.ObjectHolder.IsHoldingObject ||
            hands.IsSitting || hands.IsPlayingMealAnimation || hands.sleeping || hands.placingItem ||
            hands.IsHoldingTray || hands.Snapped || hands.Animator.GetBool(PlayerHandsAnimator.Param.Fishing))
        {
#if CIGARETTE_API
            FailStart(ResultCode.PlayerNotReady, "Stand on foot with empty hands before smoking.");
#else
            Notice("Stand on foot with empty hands before smoking.");
#endif
            return;
        }
        _standing = standing;
        var railTop = default(RaycastHit);
        var targetReason = "";
        if (!_standing && !RailingTarget.TryTop(character, target.collider, target.point,
            RailingTarget.MaximumDistance, out railTop, out targetReason))
        {
#if CIGARETTE_API
            FailStart(ResultCode.InvalidPosition, "Smoking setup cancelled: " + targetReason);
#else
            Notice("Smoking setup cancelled: " + targetReason);
#endif
            return;
        }
        if (kind == SmokeKind.Mask && !MaskModel.Available())
        {
#if CIGARETTE_API
            FailStart(ResultCode.AssetsUnavailable, "City inhalant-mask assets are not loaded here. Try a populated street.");
#else
            Notice("City inhalant-mask assets are not loaded here. Try a populated street.");
#endif
            return;
        }
        var clipName = IdleClip;
        var clip = _standing ? null : Resources.FindObjectsOfTypeAll<AnimationClip>().FirstOrDefault(c => c.name == clipName);
        if (!_standing && clip == null)
        {
#if CIGARETTE_API
            FailStart(ResultCode.AssetsUnavailable, "Smoking clip is not loaded here. Try a populated street.");
#else
            Notice("Smoking clip is not loaded here. Try a populated street.");
#endif
            Plugin.Logger.LogWarning("Missing loaded clip: " + clipName);
            return;
        }
        var animator = hands.Animator;
        if (animator.IsInTransition(0) || animator.GetCurrentAnimatorStateInfo(0).fullPathHash != Animator.StringToHash(IdleState))
        {
#if CIGARETTE_API
            FailStart(ResultCode.PlayerNotReady, "Wait until your hands are idle before smoking.");
#else
            Notice("Wait until your hands are idle before smoking.");
#endif
            return;
        }
        if (animator.runtimeAnimatorController == null || !animator.HasState(0, Animator.StringToHash(IdleState)) ||
            (!_standing && !animator.runtimeAnimatorController.animationClips.Any(c => c.name == IdleClip)))
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
        _high.SuspendCamera();
        _maskEffect.Suspend();
        _exiting = false;
        _stance = new StanceControl(hands, character.Controller);
        _stance.Place(character, _standing);
        if (_standing) UpdateSprintLock();
        else
        {
            _movementLock = character.Controller.DisableMovement(this);
            _interactionLock = character.Interaction.DisableInteractions(this);
            character.Controller.StopMovement();
        }
        if (!_standing)
        {
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
        }
        foreach (var entry in _renderers) entry.Renderer.enabled = true;
        _kind = kind;
        _blunt = kind == SmokeKind.Joint;
#if CIGARETTE_API
        var duration = Plugin.Api.Sessions.GetCurrent()!.SessionDurationSeconds;
#else
        var duration = kind == SmokeKind.Mask ? Plugin.MaskSessionDuration.Value :
            _blunt ? Plugin.CannabisSessionDuration.Value : Plugin.SessionDuration.Value;
#endif
        _session = new SmokingSession(duration, () => UnityEngine.Random.value);
        if (_blunt) _high.BeginSmoke();
        if (kind == SmokeKind.Mask) _maskEffect.BeginSmoke();
        _started = kind == SmokeKind.Mask ? Time.unscaledTime : Time.time;
        _scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle;
        Evaluate(hands);
        _stance.AlignCustomBody(animator, character.transform.position, character.Controller.Rotation);
        var movingAnchor = _standing ? null : RailSurface.Boat.AnchorFor(railTop.collider);
        if (movingAnchor != null) _stance.Follow(movingAnchor);
        CreateCigarette(animator);
        if (_cigarette == null) throw new InvalidOperationException("No hand/held item available for smoking.");
        if (_cigarette != null)
        {
            _customDraw = new CustomDraw(hands, character.Controller.Camera.transform, _cigarette.transform, character.Controller.Rotation, character, _session, railTop, _kind, _standing);
            if (movingAnchor != null) _customDraw.Follow(movingAnchor);
        }
        RailPrompt.Release();
        if (!_standing)
        {
            _hud = new SmokingHud();
            _hud.Hide();
        }
        Plugin.Logger.LogInfo($"Smoking session started: {_kind}, {(_standing ? "chest pose" : "railing")}, {_session.Duration} seconds.");
#if CIGARETTE_API
        Plugin.Api.Started();
#endif
    }

    [HideFromIl2Cpp]
    private void UpdateSprintLock()
    {
        if (Plugin.AllowSprinting.Value) Release(ref _sprintLock);
        else if (_sprintLock == null) _sprintLock = PlayerManager._instance.LocalPlayer.Character.Controller.DisableSprint(this);
    }

    [HideFromIl2Cpp]
    private bool FreeSessionConflict()
    {
        var character = PlayerManager._instance?.LocalPlayer?.Character;
        var hands = _hands;
        return character == null || hands == null || character.DrivenBoat != null ||
            character.ObjectHolder.IsHoldingObject || !character.CanEquipItem ||
            hands.IsSitting || hands.IsPlayingMealAnimation || hands.sleeping || hands.placingItem ||
            hands.IsHoldingTray || hands.Snapped || hands.Animator.GetBool(PlayerHandsAnimator.Param.Fishing) ||
            hands.Animator.GetCurrentAnimatorStateInfo(0).fullPathHash != Animator.StringToHash(IdleState);
    }

    [HideFromIl2Cpp]
    internal bool UsesNativeHands(PlayerHandsAnimator hands) => _standing && Owns(hands);

    [HideFromIl2Cpp]
    internal void AfterNativeHands(PlayerHandsAnimator hands)
    {
        if (!UsesNativeHands(hands)) return;
        try
        {
            if (FreeSessionConflict()) { Stop(null); return; }
            if (!GameplayAvailable()) return;
            Evaluate(hands);
        }
        catch (Exception e) { Plugin.Logger.LogError(e); Stop("Smoking animation failed; controls restored."); }
    }

    [HideFromIl2Cpp]
    internal void BeforeInteraction(PlayerInteraction interaction)
    {
        if (_active && _standing && PlayerManager._instance?.LocalPlayer?.Character?.Interaction == interaction)
            Stop(null);
    }

    [HideFromIl2Cpp]
    internal bool Owns(PlayerHandsAnimator hands) => _hands != null && _hands == hands;

    [HideFromIl2Cpp]
    internal bool AllowMaskMovement(PlayerCharacterController controller) =>
        !_active && _maskEffect.OwnsSlowMotion && GameplayAvailable() &&
        PlayerManager._instance?.LocalPlayer?.Character?.Controller == controller;

    [HideFromIl2Cpp]
    internal void Evaluate(PlayerHandsAnimator hands)
    {
        // Rail sessions sample a fixed base; free sessions retain native locomotion and overlay the smoking arm.
        var animator = hands.Animator;
        if (!_standing || _customDraw == null)
        {
            animator.Play(IdleState, 0, 0f);
            animator.Update(0f);
        }
        animator.transform.localPosition = _animatorPosition;
        animator.transform.localRotation = _animatorRotation;
        _stance?.Apply();
        _customDraw?.Evaluate();
        if (_customDraw != null && _joint != null) _joint.UpdateBurn(_customDraw.BurnProgress);
        if (_customDraw != null && _cigarette != null && _paper != null && _ember != null)
        {
            var burn = _customDraw.BurnProgress;
            var length = 0.064f * (1f - burn);
            _paper.localPosition = new Vector3(0f, 0f, length * 0.5f);
            var width = _blunt ? 0.010f : 0.007f;
            _paper.localScale = new Vector3(width, Mathf.Max(length, 0.00001f) * 0.5f, width);
            _ember.localPosition = new Vector3(0f, 0f, length + 0.001f);
            _paper.gameObject.SetActive(burn < 1f);
            _ember.gameObject.SetActive(burn < 1f);
        }
    }

    public void LateUpdate()
    {
        // Keep the world pose even if a camera-following ancestor moved after ManualUpdate.
        if (!_active || (_standing && !GameplayAvailable())) return;
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
        if (_kind == SmokeKind.Mask) { MaskModel.Create(_cigarette.transform, layer); return; }
        if (_blunt) { _joint = new JointModel(_cigarette.transform, layer); return; }
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
        var width = _blunt ? 0.010f : 0.007f;
        part.transform.localScale = new Vector3(width, length / 2f, width);
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
    private void BeginExit(SmokingEndReason reason = SmokingEndReason.UserCancelled)
    {
        if (!_active || _exiting) return;
        CreditAfterEffects();
        _endReason = reason;
#if CIGARETTE_API
        Plugin.Api.Finishing();
#endif
        if (_customDraw == null) { Stop(null, reason); return; }
        // Snapshot the hands after applying this frame's boat motion.
        if (_stance?.FollowsMovingRail == true && _hands != null) Evaluate(_hands);
        _exiting = true;
        _customDraw.BeginExit();
        // Menus/focus loss must never wait on the lowering animation to restore their UI.
        if (!GameplayAvailable()) { _hud?.Restore(); _hud = null; }
    }

    [HideFromIl2Cpp]
    private void DropCigarette()
    {
        if (_cigarette == null || _kind == SmokeKind.Mask) return; // Reusable mask is lowered and put away.
        var drop = new DroppedCigarette(_cigarette, _materials.ToArray(), _scene, PlayerManager._instance.LocalPlayer.Character.transform, _joint);
        _drops.Add(drop);
        _joint = null; // Mesh/material ownership follows the dropped joint.
        _cigarette = null;
        _paper = _ember = null;
        _materials.Clear(); // Ownership moves to the falling prop until it despawns.
    }

    [HideFromIl2Cpp]
    internal void Stop(string? message, SmokingEndReason reason = SmokingEndReason.UserCancelled)
    {
        var approach = _approach;
        _approach = null;
        try { approach?.Dispose(); }
        catch (Exception e) { Plugin.Logger.LogWarning("Releasing railing approach: " + e.Message); }
        try { _wheel.Close(); }
        catch (Exception e) { Plugin.Logger.LogWarning("Closing smoking menu: " + e.Message); }
        CreditAfterEffects();
        var hands = _hands;
        _active = false;
        _exiting = false;
        _hands = null; // Always release Harmony interception, even if a Unity object has been destroyed.
        try { _customDraw?.Restore(); }
        catch (Exception e) { Plugin.Logger.LogError("Restoring hand IK: " + e); }
        _customDraw = null;
        try
        {
            if (!_standing && hands != null && hands.Animator != null && _originalController != null)
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
            Release(ref _sprintLock);
            Release(ref _movementLock);
            Release(ref _interactionLock);
            foreach (var entry in _renderers)
                if (entry.Renderer != null) entry.Renderer.enabled = entry.Enabled;
            _renderers.Clear();
            _bones.Clear();
            if (_cigarette != null) Object.Destroy(_cigarette);
            _cigarette = null;
            _joint?.Dispose(); _joint = null;
            _paper = _ember = null;
            _session = null;
            foreach (var material in _materials) if (material != null) Object.Destroy(material);
            _materials.Clear();
            if (_override != null) Object.Destroy(_override);
            _override = null;
            _originalController = null;
        }
#if CIGARETTE_API
        Plugin.Api.End(message != null ? SmokingEndReason.Failed : reason, message ?? "",
            message != null ? ResultCode.Failed : reason == SmokingEndReason.AvailabilityChanged
                ? ResultCode.ItemDisabled : ResultCode.ApproachCancelled);
#endif
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
    internal static bool GameplayAvailable()
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
    internal bool CanSmokeAtRail() => CanStartSmoking(false);

    [HideFromIl2Cpp]
    private bool CanStartSmoking(bool standing)
    {
        if (!Plugin.ItemAvailable(SmokeKind.Cigarette) && !Plugin.ItemAvailable(SmokeKind.Joint) &&
            !Plugin.ItemAvailable(SmokeKind.Mask)) return false;
        if (_active || _approach != null || !GameplayAvailable()) return false;
        var character = PlayerManager._instance.LocalPlayer.Character;
        var hands = character.Controller.HandsAnimator;
        if (hands == null || !character.Controller.CanMove || !character.CanEquipItem ||
            character.Interaction.CurrentFocus != null || !character.Interaction.InteractionActive ||
            character.DrivenBoat != null || character.ObjectHolder.IsHoldingObject || hands.Snapped ||
            hands.IsSitting || hands.IsPlayingMealAnimation || hands.sleeping || hands.placingItem || hands.IsHoldingTray ||
            hands.Animator.GetBool(PlayerHandsAnimator.Param.Fishing)) return false;
        if (hands.Animator.IsInTransition(0) || hands.Animator.GetCurrentAnimatorStateInfo(0).fullPathHash != Animator.StringToHash(IdleState)) return false;
        return standing || RailingTarget.TryCandidate(character, out _, out _);
    }

    [HideFromIl2Cpp]
    private void CreditAfterEffects()
    {
#if CIGARETTE_API
        if (_active && !_exiting && _session != null && _customDraw != null)
            Plugin.Api.Consume(_customDraw.BurnProgress);
#endif
        if (_active && _blunt && !_exiting && _session != null && _customDraw != null)
            _high.Credit(_customDraw.BurnProgress, _session.Duration);
        if (_active && _kind == SmokeKind.Mask && !_exiting && _session != null && _customDraw != null)
            _maskEffect.Credit(_customDraw.BurnProgress, _session.Duration);
    }

    public void OnDisable()
    {
        RailPrompt.Release(); Stop(null, SmokingEndReason.ControllerDisabled);
#if CIGARETTE_API
        Plugin.Api.ClearDirectEffects();
#endif
        _high.Dispose(); _maskEffect.Clear(); foreach (var drop in _drops) drop.Dispose(); _drops.Clear();
#if CIGARETTE_API
        Plugin.Api.DispatchEvents();
#endif
    }
    public void OnDestroy()
    {
        Stop(null, SmokingEndReason.ControllerDisabled);
#if CIGARETTE_API
        Plugin.Api.ClearDirectEffects();
#endif
        RailSurface.Boat.Dispose(); _wheel.Dispose(); _high.Dispose(); _maskEffect.Clear(); foreach (var drop in _drops) drop.Dispose(); _drops.Clear(); if (Instance == this) Instance = null;
#if CIGARETTE_API
        Plugin.Api.DispatchEvents();
#endif
    }
}

[HarmonyPatch(typeof(PlayerHandsAnimator), nameof(PlayerHandsAnimator.ManualUpdate))]
internal static class HandsUpdatePatch
{
    internal struct PlaybackSnapshot
    {
        internal Animator? Animator;
        internal float Speed;
    }

    [HarmonyPrefix]
    private static bool Prefix(PlayerHandsAnimator __instance, out PlaybackSnapshot __state)
    {
        __state = default;
        var smoking = SmokingController.Instance;
        if (smoking == null || !smoking.Owns(__instance)) return true;
        if (smoking.UsesNativeHands(__instance))
        {
            // Use actual sprint state, not the input key or permission setting.
            // Leave the snapshot empty so the finalizer also leaves sprint playback alone.
            if (__instance.character?.Controller?._isSprinting == true) return true;
            var animator = __instance.Animator;
            __state = new PlaybackSnapshot { Animator = animator, Speed = animator.speed };
            // Only non-sprinting native hands evaluation runs at half tempo. Character
            // velocity, input, physics, smoking timing and world time are untouched.
            animator.speed = __state.Speed * 0.5f;
            return true;
        }
        try { smoking.Evaluate(__instance); return false; }
        catch (Exception e)
        {
            Plugin.Logger.LogError(e);
            smoking.Stop("Animation failed; normal controls restored.");
            return true;
        }
    }
    [HarmonyPostfix]
    private static void Postfix(PlayerHandsAnimator __instance) => SmokingController.Instance?.AfterNativeHands(__instance);

    [HarmonyFinalizer]
    private static void Finalizer(PlaybackSnapshot __state)
    {
        // Restore even if evaluation throws or an interaction ends the session.
        // Preserve a speed change made by another owner during this update.
        if (__state.Animator != null && __state.Animator.speed == __state.Speed * 0.5f)
            __state.Animator.speed = __state.Speed;
    }
}

[HarmonyPatch(typeof(PlayerHandsAnimator), nameof(PlayerHandsAnimator.UpdateIK), new Type[] { })]
internal static class HandsIkPatch
{
    [HarmonyPrefix]
    private static bool Prefix(PlayerHandsAnimator __instance) => SmokingController.Instance?.Owns(__instance) != true;
}

[HarmonyPatch(typeof(PlayerInteraction), nameof(PlayerInteraction.OnInteraction))]
internal static class SmokingInteractionPatch
{
    [HarmonyPrefix]
    private static void Prefix(PlayerInteraction __instance)
    {
        // Native OnInteraction does not inspect callback phase; mirror its gates.
        if (__instance.CanInteract && __instance.CurrentFocus != null && !CursorModeManager.IsCursorActive)
            SmokingController.Instance?.BeforeInteraction(__instance);
    }
}
