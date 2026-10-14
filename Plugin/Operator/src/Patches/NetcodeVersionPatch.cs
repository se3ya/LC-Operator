using System;
using System.Text;
using HarmonyLib;
using Unity.Netcode;

namespace Operator.Patches;

[HarmonyPatch(typeof(GameNetworkManager), nameof(GameNetworkManager.SetConnectionDataBeforeConnecting))]
internal static class VersionStampPatch
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        try
        {
            NetworkManager nm = NetworkManager.Singleton;

            byte[]? existing = nm.NetworkConfig.ConnectionData;
            string payload = existing != null && existing.Length > 0
                ? Encoding.ASCII.GetString(existing)
                : string.Empty;

            nm.NetworkConfig.ConnectionData = Encoding.ASCII.GetBytes(
                $"{payload},{Constants.Network.NetcodeVersionField}{Constants.Network.NetcodeVersion}");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"Failed to stamp netcode version, an Operator host will refuse this client: {ex}");
        }
    }
}

#pragma warning disable Harmony003
[HarmonyPatch(typeof(GameNetworkManager), nameof(GameNetworkManager.ConnectionApproval))]
internal static class VersionCheckPatch
{
    [HarmonyPostfix]
    private static void Postfix(
        NetworkManager.ConnectionApprovalRequest request,
        NetworkManager.ConnectionApprovalResponse response)
    {
        try
        {
            if (!response.Approved) return;

            if (request.ClientNetworkId == NetworkManager.Singleton.LocalClientId) return;

            int theirs = ReadVersion(request.Payload);
            if (theirs == Constants.Network.NetcodeVersion) return;

            response.Approved = false;
            response.Reason = theirs < 0
                ? $"The host is running Operator netcode {Constants.Network.NetcodeVersion} while you are on an older build."
                : $"The host is running Operator netcode {Constants.Network.NetcodeVersion} while you are on {theirs}.";
            Plugin.Log.LogWarning($"Refused client {request.ClientNetworkId} on netcode {theirs}.");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"Failed to check netcode version of joining client: {ex.Message}");
        }
    }

    private static int ReadVersion(byte[]? payload)
    {
        if (payload == null || payload.Length == 0) return -1;

        string[] fields = Encoding.ASCII.GetString(payload).Split(',');
        for (int i = 0; i < fields.Length; i++)
        {
            string field = fields[i];
            if (!field.StartsWith(Constants.Network.NetcodeVersionField, StringComparison.Ordinal)) continue;
            return int.TryParse(field.AsSpan(Constants.Network.NetcodeVersionField.Length), out int parsed)
                ? parsed
                : -1;
        }

        return -1;
    }
}
#pragma warning restore Harmony003
