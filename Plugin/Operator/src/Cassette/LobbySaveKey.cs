using System;
using System.Text;
using Unity.Collections;
using Unity.Netcode;

namespace Operator.Cassette;

internal static class LobbySaveKey
{
    private const string MsgSaveKey = "Op_LobbySave";

    private static bool _registered;
    private static string _hostSaveName = string.Empty;

    internal static string Current
    {
        get
        {
            if (_hostSaveName.Length > 0) return _hostSaveName;
            return MakeSafe(GameNetworkManager.Instance.currentSaveFileName ?? string.Empty);
        }
    }

    internal static void Register()
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm.CustomMessagingManager == null || _registered) return;

        nm.CustomMessagingManager.RegisterNamedMessageHandler(MsgSaveKey, OnSaveKey);

        if (nm.IsServer)
        {
            nm.OnClientConnectedCallback += SendToClient;
            foreach (ulong clientId in nm.ConnectedClientsIds)
            {
                SendToClient(clientId);
            }
        }

        _registered = true;
        Plugin.Log.LogDebug($"Lobby save key messaging registered, local save is '{Current}'.");
    }

    internal static void Unregister()
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm.CustomMessagingManager != null)
        {
            try { nm.CustomMessagingManager.UnregisterNamedMessageHandler(MsgSaveKey); } catch { }
        }
        nm.OnClientConnectedCallback -= SendToClient;

        _hostSaveName = string.Empty;
        _registered = false;
    }

    private static void SendToClient(ulong clientId)
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (!nm.IsServer || nm.CustomMessagingManager == null) return;
        if (clientId == nm.LocalClientId) return;

        string save = MakeSafe(GameNetworkManager.Instance.currentSaveFileName ?? string.Empty);
        if (save.Length == 0) return;

        byte[] payload = Encoding.UTF8.GetBytes(save);
        using var writer = new FastBufferWriter(payload.Length + sizeof(int), Allocator.Temp);
        writer.WriteValueSafe(payload.Length);
        writer.WriteBytesSafe(payload, payload.Length);
        nm.CustomMessagingManager.SendNamedMessage(MsgSaveKey, clientId, writer, NetworkDelivery.ReliableFragmentedSequenced);
    }

    private static void OnSaveKey(ulong senderClientId, FastBufferReader reader)
    {
        if (NetworkManager.Singleton.IsServer || senderClientId != NetworkManager.ServerClientId) return;

        try
        {
            reader.ReadValueSafe(out int length);
            if (length <= 0 || length > 256) return;

            var payload = new byte[length];
            reader.ReadBytesSafe(ref payload, length);
            string save = Encoding.UTF8.GetString(payload);
            if (!IsSafe(save))
            {
                Plugin.Log.LogWarning("Refused an unsafe save name from the host.");
                return;
            }

            _hostSaveName = save;
            Plugin.Log.LogInfo($"Lobby footage will be filed under the host save '{save}'.");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"Could not read the host save name: {ex.Message}");
        }
    }

    private static bool IsSafe(string name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > Constants.Cassette.MaxSaveNameLength) return false;
        if (char.IsWhiteSpace(name[0])) return false;

        char last = name[name.Length - 1];
        if (last == '.' || char.IsWhiteSpace(last)) return false;

        foreach (char c in name)
        {
            if (Blocked(c)) return false;
        }

        return !IsDevice(name);
    }

    private static string MakeSafe(string name)
    {
        if (string.IsNullOrEmpty(name)) return string.Empty;

        int start = 0;
        while (start < name.Length && char.IsWhiteSpace(name[start])) start++;

        int end = name.Length;
        if (end - start > Constants.Cassette.MaxSaveNameLength) end = start + Constants.Cassette.MaxSaveNameLength;
        while (end > start && (name[end - 1] == '.' || char.IsWhiteSpace(name[end - 1]))) end--;
        if (end <= start) return string.Empty;

        var safe = new StringBuilder(end - start);
        for (int i = start; i < end; i++)
        {
            char c = name[i];
            if (char.IsHighSurrogate(c) && i + 1 < end && char.IsLowSurrogate(name[i + 1]))
            {
                safe.Append(c);
                safe.Append(name[i + 1]);
                i++;
                continue;
            }

            safe.Append(Blocked(c) || char.IsSurrogate(c) ? '_' : c);
        }

        string result = safe.ToString();
        return IsDevice(result) ? string.Empty : result;
    }

    private static bool Blocked(char c)
    {
        return c < ' ' || c == '"' || c == '*' || c == '/' || c == ':'
            || c == '<' || c == '>' || c == '?' || c == '\\' || c == '|';
    }

    private static bool IsDevice(string name)
    {
        int dot = name.IndexOf('.');
        return Recording.ClipName.IsDeviceName(dot < 0 ? name : name.Substring(0, dot));
    }
}
