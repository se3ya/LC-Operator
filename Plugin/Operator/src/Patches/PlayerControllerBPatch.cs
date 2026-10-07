using System.Collections.Generic;
using GameNetcodeStuff;
using HarmonyLib;
using Operator.Cassette;
using UnityEngine;

namespace Operator.Patches;

[HarmonyPatch(typeof(PlayerControllerB), nameof(PlayerControllerB.PlaceGrabbableObject))]
internal static class PlaceRotationPatch
{
    [HarmonyPostfix]
    private static void UseWorldYaw(PlayerControllerB __instance, bool matchRotationOfParent, GrabbableObject placeObject)
    {
        if (matchRotationOfParent || placeObject == null) return;
        if (placeObject is not VideoCameraItem && placeObject is not CassetteItem) return;
        placeObject.floorYRot = (int)__instance.transform.eulerAngles.y;
    }
}

[HarmonyPatch(typeof(PlayerControllerB), nameof(PlayerControllerB.SetObjectAsNoLongerHeld))]
internal static class DropRotationPatch
{
    [HarmonyPostfix]
    private static void UseWorldYaw(PlayerControllerB __instance, GrabbableObject dropObject)
    {
        if (dropObject == null) return;
        if (dropObject is not VideoCameraItem && dropObject is not CassetteItem) return;
        dropObject.floorYRot = (int)__instance.transform.eulerAngles.y;
    }
}

[HarmonyPatch(typeof(PlayerControllerB), nameof(PlayerControllerB.DamagePlayer))]
internal static class CrackHitPatch
{
    internal sealed class HitState
    {
        public int Health = -1;
        public List<VideoCameraItem> Cameras = new();
    }

    [HarmonyPrefix]
    private static void Before(PlayerControllerB __instance, out HitState __state)
    {
        __state = new HitState();
        StartOfRound sor = StartOfRound.Instance;
        if (sor == null || __instance != sor.localPlayerController || !__instance.IsOwner) return;
        if (__instance.isPlayerDead || !__instance.AllowPlayerDeath()) return;

        __state.Health = __instance.health;
        VideoCameraItem.GetCarried(__instance, __state.Cameras);
    }

    [HarmonyPostfix]
    private static void After(PlayerControllerB __instance, int damageNumber, HitState __state)
    {
        if (__state.Health < 0 || __state.Cameras.Count == 0) return;

        if (__instance.health == __state.Health && !__instance.isPlayerDead) return;
        int lost = Mathf.Max(damageNumber, __state.Health - __instance.health);
        if (lost < Mathf.Max(1, VideoCameraConfig.CrackDamageThreshold.Value)) return;

        foreach (VideoCameraItem camera in __state.Cameras)
        {
            if (camera != null) camera.ReportHit(lost);
        }
    }
}

[HarmonyPatch(typeof(PlayerControllerB), "Start")]
internal static class HoldRigPatch
{
    [HarmonyPostfix]
    [HarmonyPriority(Priority.First)]
    private static void AddRig(PlayerControllerB __instance)
    {
        HoldRig.Add(__instance);
    }
}

[HarmonyPatch(typeof(PlayerControllerB), "SwitchToItemSlot")]
internal static class SlotIconPatch
{
    [HarmonyPostfix]
    [HarmonyPriority(Priority.Last)]
    private static void ApplyTint(PlayerControllerB __instance, int slot, GrabbableObject fillSlotWithItem)
    {
        if (fillSlotWithItem == null) return;
        CassetteHudIcon.ApplySlotIcon(__instance, slot, fillSlotWithItem);
    }
}

[HarmonyPatch(typeof(PlayerControllerB), "LateUpdate")]
[HarmonyAfter("com.seeya.firstpersonview", "com.seeya.cameraoverhaul")]
internal static class SteadyHoldPatch
{
    [HarmonyPostfix]
    private static void Steady(PlayerControllerB __instance)
    {
        StartOfRound sor = StartOfRound.Instance;
        if (sor == null || __instance != sor.localPlayerController) return;
        if (__instance.currentlyHeldObjectServer is VideoCameraItem camera) camera.SteadyArms(__instance);
    }
}
