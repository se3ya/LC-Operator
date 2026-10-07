using System;
using System.Collections;
using HarmonyLib;
using Unity.Netcode;
using UnityEngine;
using Operator.Cassette;
using Operator.Recording;

namespace Operator.Patches;

[HarmonyPatch(typeof(StartOfRound), nameof(StartOfRound.Start))]
internal static class GameStartPatch
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        try
        {
            CassetteLibrary.CleanupUnsaved();
            CassetteLibrary.CleanupPartFiles("GameStart");
            CompanyMoon.Forget();
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"Failed to reset cassette state when round started: {ex.Message}");
        }

        try
        {
            ClipBroadcaster.GetOrCreate().Register();
            TerminalCoWatch.Register();
            TerminalMonitorTransfer.Register();
            TelevisionCoWatch.Register();
            LobbySaveKey.Register();
            StoreCostSync.Register();
            CassetteTakerManager.Init();
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"Failed to set up cassette networking for this lobby: {ex}");
        }

        try
        {
            LobbyPeers.Register();
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"Failed to announce this player to the lobby, shared playback will skip them: {ex}");
        }

        if (Plugin.Instance is { } plugin && EnemyBountyLoader.Loaded)
        {
            VideoCameraConfig.ClearBountyOrphans(plugin.Config);
        }
        else
        {
            EnemyBountyLoader.ReportNotLoaded();
        }
    }
}

[HarmonyPatch(typeof(StartOfRound), nameof(StartOfRound.StartGame))]
internal static class EncoderWarmupPatch
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        if (!VideoCameraConfig.RecordingEnabled.Value) return;
        try { _ = Operator.Recording.VideoEncoder.RewarmAsync(); }
        catch (Exception ex) { Plugin.Log.LogDebug($"Failed to rewarm encoder on landing: {ex.Message}"); }
    }
}

[HarmonyPatch(typeof(StartOfRound), nameof(StartOfRound.SyncAlreadyHeldObjectsClientRpc))]
internal static class HeldSyncPatch
{
    [HarmonyPostfix]
    private static void Postfix(StartOfRound __instance, NetworkObjectReference[] gObjects, int[] playersHeldBy, int syncWithClient)
    {
        NetworkManager network = NetworkManager.Singleton;
        if (network == null || network.IsServer) return;
        if (syncWithClient != (int)network.LocalClientId) return;
        if (gObjects == null || playersHeldBy == null) return;

        for (int i = 0; i < gObjects.Length && i < playersHeldBy.Length; i++)
        {
            if (!gObjects[i].TryGet(out NetworkObject networkObject)) continue;
            VideoCameraItem camera = networkObject.GetComponent<VideoCameraItem>();
            if (camera == null || camera.playerHeldBy != null) continue;
            int holder = playersHeldBy[i];
            if (holder < 0 || holder >= __instance.allPlayerScripts.Length) continue;

            try
            {
                camera.playerHeldBy = __instance.allPlayerScripts[holder];
                if (!camera.isPocketed) camera.EquipItem();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Failed to sync held camera for late join: {ex}");
            }
        }
    }
}

[HarmonyPatch(typeof(StartOfRound), "ShipLeave")]
internal static class ShipLeavePatch
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        foreach (VideoCameraItem camera in UnityEngine.Object.FindObjectsOfType<VideoCameraItem>(true))
        {
            try
            {
                camera.FinalizeOnDayEnd();
                camera.ServerScheduleChainClose();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Failed to finish recording when ship left: {ex}");
            }
        }

    }
}

[HarmonyPatch(typeof(StartOfRound), nameof(StartOfRound.ShipHasLeft))]
internal static class OrbitRestorePatch
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        try
        {
            TerminalMonitorTransfer.ForceLocalStop();
            TelevisionCoWatch.StopForOrbit();
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"Failed to stop monitor transfer in orbit: {ex.Message}");
        }
    }
}

[HarmonyPatch(typeof(StartOfRound), nameof(StartOfRound.OnDestroy))]
internal static class TeardownRestorePatch
{
    [HarmonyPrefix]
    private static void Prefix()
    {
        try
        {
            TerminalMonitorTransfer.ForceLocalStop();
            TelevisionCoWatch.ForceLocalStop();
        }
        catch (Exception)
        {
        }
    }
}

[HarmonyPatch(typeof(StartOfRound), nameof(StartOfRound.OnShipLandedMiscEvents))]
internal static class ShipLandedPatch
{
    [HarmonyPostfix]
    private static void Postfix(StartOfRound __instance)
    {
        if (__instance == null || __instance.currentLevel == null) return;
        Operator.Cassette.EnemyBountyTracker.CountLevelScrap();
        if (!__instance.IsServer || !__instance.currentLevel.spawnEnemiesAndScrap) return;
        Plugin.Log.LogDebug($"Danger pay is x{Operator.Cassette.EnemyBountyTracker.GetDanger():0.00} on {__instance.currentLevel.PlanetName} ({Operator.Cassette.EnemyBountyTracker.LevelScrap:0} scrap).");
    }
}

[HarmonyPatch(typeof(StartOfRound), nameof(StartOfRound.ShipHasLeft))]
internal static class RestockPatch
{
    [HarmonyPostfix]
    private static void Postfix(StartOfRound __instance)
    {
        Operator.Cassette.EnemyBountyTracker.ClearLevelScrap();
        if (__instance == null || !__instance.IsServer)
        {
            return;
        }

        foreach (VideoCameraItem camera in UnityEngine.Object.FindObjectsOfType<VideoCameraItem>(true))
        {
            try
            {
                camera.ServerRestockBlanks();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Failed to restock blank cassettes on camera: {ex.Message}");
            }
        }
    }
}

[HarmonyPatch(typeof(StartOfRound), nameof(StartOfRound.ShipHasLeft))]
internal static class OrbitRepairPatch
{
    [HarmonyPostfix]
    private static void Postfix(StartOfRound __instance)
    {
        if (__instance == null || !__instance.IsServer) return;
        if (VideoCameraConfig.LensRepair.Value != LensRepair.EachOrbit) return;
        VideoCameraItem.ServerRepairAllLenses("orbit");
    }
}

[HarmonyPatch(typeof(StartOfRound), nameof(StartOfRound.Start))]
internal static class ReconcilePatch
{
    [HarmonyPostfix]
    private static void Postfix(StartOfRound __instance)
    {
        if (!__instance.IsServer) return;
        __instance.StartCoroutine(ReconcileAfterItemsSettle());
    }

    private static IEnumerator ReconcileAfterItemsSettle()
    {
        yield return new WaitForSeconds(Constants.Cassette.ReconcileDelay);
        CassetteLibrary.ReconcileLobbyFolder();
    }
}
