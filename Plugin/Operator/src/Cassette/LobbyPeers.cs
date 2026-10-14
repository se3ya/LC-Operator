using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;

namespace Operator.Cassette;

internal static class LobbyPeers
{
    private const string MsgHello = "Op_Hello";
    private const string MsgState = "Op_State";

    private static readonly HashSet<ulong> _listeners = new();
    private static bool _registered;

    internal static IReadOnlyCollection<ulong> Listeners => _listeners;

    internal static void Register()
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm.CustomMessagingManager == null) return;

        _listeners.Clear();

        if (!_registered)
        {
            nm.CustomMessagingManager.RegisterNamedMessageHandler(MsgState, OnState);
            if (nm.IsServer)
            {
                nm.CustomMessagingManager.RegisterNamedMessageHandler(MsgHello, OnHello);
                nm.OnClientDisconnectCallback += Drop;
            }
        }

        if (nm.IsServer)
        {
            _listeners.Add(nm.LocalClientId);
        }
        else
        {
            using var writer = new FastBufferWriter(sizeof(byte), Allocator.Temp);
            writer.WriteValueSafe((byte)0);
            nm.CustomMessagingManager.SendNamedMessage(MsgHello, NetworkManager.ServerClientId, writer, NetworkDelivery.Reliable);
        }

        _registered = true;
    }

    internal static void Unregister()
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm.CustomMessagingManager != null)
        {
            try { nm.CustomMessagingManager.UnregisterNamedMessageHandler(MsgHello); } catch { }
            try { nm.CustomMessagingManager.UnregisterNamedMessageHandler(MsgState); } catch { }
        }
        nm.OnClientDisconnectCallback -= Drop;

        _listeners.Clear();
        _registered = false;
    }

    private static void Drop(ulong clientId) => _listeners.Remove(clientId);

    private static void OnHello(ulong from, FastBufferReader reader)
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (!nm.IsServer || nm.CustomMessagingManager == null) return;
        if (from == nm.LocalClientId) return;

        _listeners.Add(from);

        using var writer = new FastBufferWriter(sizeof(float), Allocator.Temp);
        writer.WriteValueSafe(TerminalCassettePlayback.Volume);
        nm.CustomMessagingManager.SendNamedMessage(MsgState, from, writer, NetworkDelivery.Reliable);
        TerminalCoWatch.SendState(from);
        TelevisionCoWatch.SendSnapshot(from);
    }

    private static void OnState(ulong from, FastBufferReader reader)
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm.IsServer || from != NetworkManager.ServerClientId) return;

        reader.ReadValueSafe(out float volume);
        if (float.IsNaN(volume)) return;
        TerminalCassettePlayback.Volume = volume;
    }
}
