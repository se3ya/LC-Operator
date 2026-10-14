using System;
using HarmonyLib;
using Operator.Cassette;
using Operator.Recording;
using Unity.Netcode;

namespace Operator.Patches;

[HarmonyPatch(typeof(GameNetworkManager), nameof(GameNetworkManager.Disconnect))]
internal static class DisconnectPatch
{
    [HarmonyPrefix]
    private static void Prefix()
    {
        try
        {
            CassetteLibrary.CleanupUnsaved();
            CassetteLibrary.CleanupPartFiles("Disconnect");
            CassetteLibrary.PruneAllReceived();
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"Failed to clean up cassette files after disconnecting: {ex.Message}");
        }

        if (NetworkManager.Singleton == null) return;

        try
        {
            TerminalMonitorTransfer.Unregister();
            ClipBroadcaster.Instance?.Unregister();
            TerminalCoWatch.Unregister();
            TelevisionCoWatch.Unregister();
            LobbySaveKey.Unregister();
            StoreCostSync.Unregister();
            CassetteTakerManager.Shutdown();
            LobbyPeers.Unregister();
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"Failed to unregister network handlers after disconnecting: {ex.Message}");
        }
    }
}
