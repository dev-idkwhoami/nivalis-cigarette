using HarmonyLib;
using Nivalis;
using UnityEngine;

namespace NivalisMods.Cigarette;

[HarmonyPatch(typeof(PlayerCharacterController), nameof(PlayerCharacterController.ManualUpdate))]
internal static class MaskMovementPatch
{
    private static PlayerCharacterController? _updating;
    private static float _movementScale = 1f;

    internal struct Snapshot
    {
        internal float WorldScale, PreviousMovementScale;
        internal PlayerCharacterController? PreviousController;
    }

    [HarmonyPrefix]
    private static void Prefix(PlayerCharacterController __instance, out Snapshot __state)
    {
        __state = default;
        if (SmokingController.Instance?.AllowMaskMovement(__instance) != true) return;
        // Native ManualUpdate returns early when timeScale < 1, although its
        // movement integration uses unscaledDeltaTime. Let the native path run,
        // retaining input, collision and movement-lock checks, then restore time.
        __state = new Snapshot
        {
            WorldScale = Time.timeScale, PreviousController = _updating,
            PreviousMovementScale = _movementScale
        };
        _updating = __instance;
        _movementScale = __state.WorldScale;
        Time.timeScale = 1f;
    }

    internal static float MovementScale(PlayerCharacterController controller) =>
        controller == _updating ? _movementScale : 1f;

    [HarmonyFinalizer]
    private static void Finalizer(Snapshot __state)
    {
        if (__state.WorldScale <= 0f) return;
        _updating = __state.PreviousController;
        _movementScale = __state.PreviousMovementScale;
        // Also restore on exceptions; don't overwrite a pause/change made inside.
        if (Time.timeScale == 1f) Time.timeScale = __state.WorldScale;
    }
}

[HarmonyPatch(typeof(PlayerCharacterController), nameof(PlayerCharacterController.UpdateMovement))]
internal static class MaskMovementTimingPatch
{
    [HarmonyPrefix, HarmonyPriority(Priority.Last)]
    private static void Prefix(PlayerCharacterController __instance, ref float __0)
    {
        // Native ManualUpdate supplies unscaled time. Use the captured world
        // scale even though its movement gate temporarily sees timeScale = 1.
        // Scale time once, preserving native walk/sprint, gravity and collisions.
        __0 *= MaskMovementPatch.MovementScale(__instance);
    }
}
