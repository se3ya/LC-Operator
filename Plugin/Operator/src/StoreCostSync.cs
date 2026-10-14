using Unity.Collections;
using Unity.Netcode;

namespace Operator;

internal static class StoreCostSync
{
    private const string MsgCost = "Op_StoreCost";
    private const string MsgRequest = "Op_StoreCostReq";

    private static bool _registered;
    private static bool _hasHostValue;
    private static int _hostValue;

    internal static int Cost
    {
        get
        {
            try
            {
                NetworkManager nm = NetworkManager.Singleton;
                if (nm.IsListening && !nm.IsServer && _hasHostValue) return _hostValue;
                return VideoCameraConfig.StoreCost.Value;
            }
            catch
            {
                return Constants.ConfigRanges.StoreCostMin;
            }
        }
    }

    internal static void Register()
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm.CustomMessagingManager == null || _registered) return;

        nm.CustomMessagingManager.RegisterNamedMessageHandler(MsgCost, OnCost);

        if (nm.IsServer)
        {
            nm.CustomMessagingManager.RegisterNamedMessageHandler(MsgRequest, OnRequest);
            nm.OnClientConnectedCallback += SendToClient;
        }
        else
        {
            using var writer = new FastBufferWriter(sizeof(byte), Allocator.Temp);
            writer.WriteValueSafe((byte)0);
            nm.CustomMessagingManager.SendNamedMessage(MsgRequest, NetworkManager.ServerClientId, writer, NetworkDelivery.Reliable);
        }

        _registered = true;
    }

    internal static void Unregister()
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm.CustomMessagingManager != null)
        {
            try { nm.CustomMessagingManager.UnregisterNamedMessageHandler(MsgCost); } catch { }
            try { nm.CustomMessagingManager.UnregisterNamedMessageHandler(MsgRequest); } catch { }
        }
        nm.OnClientConnectedCallback -= SendToClient;

        _hasHostValue = false;
        _hostValue = 0;
        _registered = false;
    }

    private static void SendToClient(ulong clientId)
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (!nm.IsServer || nm.CustomMessagingManager == null) return;
        if (clientId == nm.LocalClientId) return;

        using var writer = new FastBufferWriter(sizeof(int), Allocator.Temp);
        writer.WriteValueSafe(VideoCameraConfig.StoreCost.Value);
        nm.CustomMessagingManager.SendNamedMessage(MsgCost, clientId, writer, NetworkDelivery.Reliable);
    }

    private static void OnRequest(ulong from, FastBufferReader reader)
    {
        SendToClient(from);
    }

    private static void OnCost(ulong from, FastBufferReader reader)
    {
        if (from != NetworkManager.ServerClientId) return;

        int cost;
        try
        {
            reader.ReadValueSafe(out cost);
        }
        catch (System.Exception ex)
        {
            Plugin.Log.LogWarning($"Could not read store cost from host: {ex.Message}");
            return;
        }

        _hostValue = UnityEngine.Mathf.Clamp(cost, Constants.ConfigRanges.StoreCostMin, Constants.ConfigRanges.StoreCostMax);
        _hasHostValue = true;
        Plugin.Log.LogDebug($"Store cost set by host to {_hostValue}.");
    }
}
