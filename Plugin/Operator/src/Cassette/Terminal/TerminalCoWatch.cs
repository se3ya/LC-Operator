using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Operator.Cassette;

internal static class TerminalCoWatch
{
    internal const byte TransportPause = 0;
    internal const byte TransportResume = 1;
    internal const byte TransportSpeed = 2;
    internal const byte TransportVolume = 3;

    private const string MsgPlay = "Op_CoWatch_Play";
    private const string MsgReqPlay = "Op_CoWatch_ReqPlay";
    private const string MsgStop = "Op_CoWatch_Stop";
    private const string MsgReqStop = "Op_CoWatch_ReqStop";
    private const string MsgTransport = "Op_CoWatch_Transport";
    private const string MsgReqTransport = "Op_CoWatch_ReqTransport";
    private const string MsgPrepare = "Op_CoWatch_Prepare";
    private const string MsgReady = "Op_CoWatch_Ready";
    private const string MsgGaveUp = "Op_CoWatch_GaveUp";
    private const string MsgWaiting = "Op_CoWatch_Waiting";
    private const string MsgAbort = "Op_CoWatch_Abort";

    private static string _prepareFile = string.Empty;
    private static readonly HashSet<ulong> _prepareClients = new();
    private static readonly HashSet<ulong> _prepareReady = new();
    private static readonly HashSet<ulong> _prepareGaveUp = new();
    private static int _lastWaitingSent = -1;
    private static int _waitingCount;
    private static string _localPrepareFile = string.Empty;
    private static bool _localReadySent;
    private static readonly ClipWait _localWait = new();
    private static readonly ClipWait _serverWait = new();
    private static bool _localHasFile;
    private static double _nextFileCheck;

    internal static bool IsPreparingLocally => _localPrepareFile.Length > 0;

    internal static bool IsPreparingForLobby => IsServer && _prepareFile.Length > 0;

    internal static bool IsPreparingFile(string fileName) =>
        string.Equals(_prepareFile, fileName, System.StringComparison.OrdinalIgnoreCase)
        || string.Equals(_localPrepareFile, fileName, System.StringComparison.OrdinalIgnoreCase);

    private static bool _registered;

    internal static bool Enabled =>
        VideoCameraConfig.TerminalCoWatch != null
        && VideoCameraConfig.TerminalCoWatch.Value
        && NetworkManager.Singleton != null
        && NetworkManager.Singleton.IsListening;

    private static CustomMessagingManager? Cmm =>
        NetworkManager.Singleton != null ? NetworkManager.Singleton.CustomMessagingManager : null;

    private static bool IsServer =>
        NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;

    internal static void Register()
    {
        NetworkManager? nm = NetworkManager.Singleton;
        if (nm == null || nm.CustomMessagingManager == null || _registered) return;

        CustomMessagingManager cmm = nm.CustomMessagingManager;
        cmm.RegisterNamedMessageHandler(MsgPlay, OnPlay);
        cmm.RegisterNamedMessageHandler(MsgStop, OnStop);
        cmm.RegisterNamedMessageHandler(MsgTransport, OnTransport);
        cmm.RegisterNamedMessageHandler(MsgPrepare, OnPrepare);
        cmm.RegisterNamedMessageHandler(MsgWaiting, OnWaiting);
        cmm.RegisterNamedMessageHandler(MsgAbort, OnAbort);
        if (nm.IsServer)
        {
            cmm.RegisterNamedMessageHandler(MsgReqPlay, OnRequestPlay);
            cmm.RegisterNamedMessageHandler(MsgReqStop, OnRequestStop);
            cmm.RegisterNamedMessageHandler(MsgReqTransport, OnRequestTransport);
            cmm.RegisterNamedMessageHandler(MsgReady, OnReady);
            cmm.RegisterNamedMessageHandler(MsgGaveUp, OnGaveUp);
        }
        _registered = true;
    }

    internal static void Unregister()
    {
        CustomMessagingManager? cmm = Cmm;
        if (cmm != null)
        {
            foreach (string name in new[] { MsgPlay, MsgStop, MsgTransport, MsgReqPlay, MsgReqStop, MsgReqTransport, MsgPrepare, MsgReady, MsgGaveUp, MsgWaiting, MsgAbort })
            {
                try { cmm.UnregisterNamedMessageHandler(name); } catch { }
            }
        }
        ServerClearPrepare();
        _registered = false;
    }

    internal static void RequestPlay(string fileName)
    {
        if (Cmm == null)
        {
            return;
        }
        if (IsServer)
        {
            ServerBeginPrepare(fileName);
        }
        else
        {
            SendStringToServer(MsgReqPlay, fileName);
        }
    }

    private static void ServerBeginPrepare(string fileName)
    {
        if (!IsServer || Cmm == null || string.IsNullOrEmpty(fileName)) return;

        fileName = CassetteLibrary.GetMergedName(fileName);
        _prepareFile = fileName;
        _prepareClients.Clear();
        _prepareClients.Add(NetworkManager.Singleton.LocalClientId);
        foreach (ulong id in LobbyPeers.Listeners) _prepareClients.Add(id);
        _prepareReady.Clear();
        _prepareGaveUp.Clear();
        _lastWaitingSent = -1;
        _serverWait.Start();
        StartLocalPrepare(fileName);

        Plugin.Log.LogDebug($"Waiting for everyone to have '{fileName}'.");
        SendStringToAll(MsgPrepare, fileName);
        TerminalCassettePlayback.ApplyPrepareLocally(fileName);
    }

    internal static void Tick()
    {
        if (!Enabled)
        {
            if (_localPrepareFile.Length > 0) AckWithoutWatching();
            if (IsServer && _prepareFile.Length > 0) ServerTickPrepare();
            return;
        }

        if (_localPrepareFile.Length > 0)
        {
            TickLocalPrepare();
        }

        if (IsServer && _prepareFile.Length > 0)
        {
            ServerTickPrepare();
        }
    }

    private static void TickLocalPrepare()
    {
        double now = Time.realtimeSinceStartupAsDouble;
        if (now >= _nextFileCheck)
        {
            _nextFileCheck = now + Constants.Terminal.FileCheckInterval;
            _localHasFile = TerminalCassettePlayback.LocalHasFile(_localPrepareFile);
        }
        TerminalCassettePlayback.UpdatePrepareStatus(_localHasFile, _waitingCount);

        if (_localReadySent) return;

        if (_localHasFile)
        {
            _localReadySent = true;
            if (IsServer)
            {
                ServerMarkReady(NetworkManager.Singleton.LocalClientId, _localPrepareFile);
            }
            else
            {
                SendStringToServer(MsgReady, _localPrepareFile);
            }
            return;
        }

        bool stalled = _localWait.IsStalled();
        if (!stalled && !_localWait.IsNothingComing()) return;

        string fileName = _localPrepareFile;
        if (stalled) Plugin.Log.LogWarning($"'{fileName}' stopped arriving, dropping out of shared playback.");
        else Plugin.Log.LogInfo($"'{fileName}' is not coming to this machine, dropping out of shared playback.");
        ClearLocalPrepare();
        TerminalCassettePlayback.ApplyAbortLocally();
        if (IsServer)
        {
            ServerMarkGaveUp(NetworkManager.Singleton.LocalClientId, fileName);
        }
        else
        {
            SendStringToServer(MsgGaveUp, fileName);
        }
    }

    private static void AckWithoutWatching()
    {
        string fileName = _localPrepareFile;
        ClearLocalPrepare();
        TerminalCassettePlayback.ApplyAbortLocally();

        if (IsServer)
        {
            ServerMarkReady(NetworkManager.Singleton.LocalClientId, fileName);
            return;
        }

        SendStringToServer(MsgReady, fileName);
    }

    private static void ServerMarkReady(ulong clientId, string fileName)
    {
        if (!IsServer) return;
        if (fileName != _prepareFile) return;
        _prepareReady.Add(clientId);
    }

    private static bool IsConnected(ulong clientId)
    {
        foreach (ulong id in NetworkManager.Singleton.ConnectedClientsIds)
        {
            if (id == clientId) return true;
        }
        return false;
    }

    private static void ServerMarkGaveUp(ulong clientId, string fileName)
    {
        if (!IsServer) return;
        if (fileName != _prepareFile) return;
        _prepareGaveUp.Add(clientId);
    }

    private static void ServerTickPrepare()
    {
        if (_serverWait.IsStalled())
        {
            foreach (ulong id in NetworkManager.Singleton.ConnectedClientsIds)
            {
                if (!_prepareClients.Contains(id)) continue;
                if (_prepareReady.Contains(id)) continue;
                _prepareGaveUp.Add(id);
            }
            _serverWait.Clear();
        }

        int waiting = 0;
        foreach (ulong id in NetworkManager.Singleton.ConnectedClientsIds)
        {
            if (!_prepareClients.Contains(id)) continue;
            if (_prepareReady.Contains(id) || _prepareGaveUp.Contains(id)) continue;
            waiting++;
        }

        if (waiting > 0)
        {
            if (waiting != _lastWaitingSent)
            {
                _lastWaitingSent = waiting;
                _waitingCount = waiting;
                SendByteToAll(MsgWaiting, (byte)Mathf.Min(waiting, byte.MaxValue));
            }
            return;
        }

        string f = _prepareFile;
        int ready = _prepareReady.Count;
        int missed = _prepareGaveUp.Count;
        bool hostWatches = _prepareReady.Contains(NetworkManager.Singleton.LocalClientId);
        var watchers = new List<ulong>(_prepareReady);
        var dropped = new List<ulong>(_prepareGaveUp);
        ServerClearPrepare();

        if (ready == 0)
        {
            Plugin.Log.LogWarning($"Nobody has '{f}', so there is nothing to play.");
            TerminalCassettePlayback.ApplyAbortLocally();
            SendByteToAll(MsgAbort, 0);
            return;
        }

        if (missed > 0)
        {
            Plugin.Log.LogWarning($"{missed} of {ready + missed} players never got '{f}'. Playing for rest.");
        }
        else
        {
            Plugin.Log.LogDebug($"All peers have '{f}', starting synchronized playback.");
        }

        TerminalCassettePlayback.HidePrepareStatus();
        if (Enabled && hostWatches) TerminalCassettePlayback.ApplyPlayLocally(f);
        else TerminalCassettePlayback.ApplyAbortLocally();

        foreach (ulong id in watchers)
        {
            if (id == NetworkManager.Singleton.LocalClientId) continue;
            if (!IsConnected(id)) continue;
            SendStringTo(MsgPlay, id, f);
        }

        foreach (ulong id in dropped)
        {
            if (id == NetworkManager.Singleton.LocalClientId) continue;
            if (!IsConnected(id)) continue;
            SendByteTo(MsgAbort, id, 0);
        }
    }

    internal static void RequestStop()
    {
        if (Cmm == null)
        {
            return;
        }
        if (IsServer)
        {
            ServerClearPrepare();
            TerminalCassettePlayback.ApplyStopLocally();
            SendByteToAll(MsgStop, 0);
        }
        else
        {
            ClearLocalPrepare();
            SendByteToServer(MsgReqStop, 0);
        }
    }

    private static void ServerClearPrepare()
    {
        _prepareFile = string.Empty;
        _prepareClients.Clear();
        _prepareReady.Clear();
        _prepareGaveUp.Clear();
        _lastWaitingSent = -1;
        _serverWait.Clear();
        ClearLocalPrepare();
    }

    private static void StartLocalPrepare(string fileName)
    {
        _localPrepareFile = fileName;
        _localReadySent = false;
        _waitingCount = 0;
        _localHasFile = false;
        _nextFileCheck = 0d;
        _localWait.Start();
    }

    private static void ClearLocalPrepare()
    {
        _localPrepareFile = string.Empty;
        _localReadySent = false;
        _waitingCount = 0;
        _localHasFile = false;
        _nextFileCheck = 0d;
        _localWait.Clear();
    }

    internal static void RequestTransport(byte kind, float value, float positionSeconds)
    {
        if (Cmm == null)
        {
            return;
        }
        if (IsServer)
        {
            TerminalCassettePlayback.ApplyTransportLocally(kind, value, positionSeconds);
            SendTransportToAll(kind, value, positionSeconds);
        }
        else
        {
            SendTransportToServer(kind, value, positionSeconds);
        }
    }

    private static void OnRequestPlay(ulong sender, FastBufferReader reader)
    {
        reader.ReadValueSafe(out string fileName);
        if (_prepareFile.Length > 0) return;
        ServerBeginPrepare(fileName);
    }

    private static void OnPrepare(ulong sender, FastBufferReader reader)
    {
        if (IsServer) return;
        reader.ReadValueSafe(out string fileName);
        StartLocalPrepare(fileName);
        TerminalCassettePlayback.ApplyPrepareLocally(fileName);
    }

    private static void OnReady(ulong sender, FastBufferReader reader)
    {
        reader.ReadValueSafe(out string fileName);
        ServerMarkReady(sender, fileName);
    }

    private static void OnGaveUp(ulong sender, FastBufferReader reader)
    {
        reader.ReadValueSafe(out string fileName);
        ServerMarkGaveUp(sender, fileName);
    }

    private static void OnWaiting(ulong sender, FastBufferReader reader)
    {
        if (IsServer) return;
        reader.ReadValueSafe(out byte waiting);
        _waitingCount = waiting;
    }

    private static void OnAbort(ulong sender, FastBufferReader reader)
    {
        if (IsServer) return;
        ClearLocalPrepare();
        TerminalCassettePlayback.ApplyAbortLocally();
    }

    private static void OnPlay(ulong sender, FastBufferReader reader)
    {
        if (IsServer) return;
        reader.ReadValueSafe(out string fileName);
        ClearLocalPrepare();
        if (!Enabled)
        {
            TerminalCassettePlayback.ApplyAbortLocally();
            return;
        }
        TerminalCassettePlayback.HidePrepareStatus();
        TerminalCassettePlayback.ApplyPlayLocally(fileName);
    }

    private static void OnRequestStop(ulong sender, FastBufferReader reader)
    {
        ServerClearPrepare();
        TerminalCassettePlayback.ApplyStopLocally();
        SendByteToAll(MsgStop, 0);
    }

    private static void OnStop(ulong sender, FastBufferReader reader)
    {
        if (IsServer) return;
        ClearLocalPrepare();
        TerminalCassettePlayback.ApplyStopLocally();
    }

    private static void OnRequestTransport(ulong sender, FastBufferReader reader)
    {
        reader.ReadValueSafe(out byte kind);
        reader.ReadValueSafe(out float value);
        reader.ReadValueSafe(out float positionSeconds);
        TerminalCassettePlayback.ApplyTransportLocally(kind, value, positionSeconds);
        SendTransportToAll(kind, value, positionSeconds);
    }

    private static void OnTransport(ulong sender, FastBufferReader reader)
    {
        if (IsServer) return;
        reader.ReadValueSafe(out byte kind);
        reader.ReadValueSafe(out float value);
        reader.ReadValueSafe(out float positionSeconds);
        TerminalCassettePlayback.ApplyTransportLocally(kind, value, positionSeconds);
    }

    private static void SendStringToAll(string msg, string value)
    {
        CustomMessagingManager? cmm = Cmm;
        if (cmm == null) return;
        using var writer = new FastBufferWriter(FastBufferWriter.GetWriteSize(value), Allocator.Temp);
        writer.WriteValueSafe(value);
        cmm.SendNamedMessageToAll(msg, writer);
    }

    private static void SendStringTo(string msg, ulong clientId, string value)
    {
        CustomMessagingManager? cmm = Cmm;
        if (cmm == null) return;
        using var writer = new FastBufferWriter(FastBufferWriter.GetWriteSize(value), Allocator.Temp);
        writer.WriteValueSafe(value);
        cmm.SendNamedMessage(msg, clientId, writer);
    }

    private static void SendStringToServer(string msg, string value)
    {
        CustomMessagingManager? cmm = Cmm;
        NetworkManager? nm = NetworkManager.Singleton;
        if (cmm == null || nm == null) return;
        using var writer = new FastBufferWriter(FastBufferWriter.GetWriteSize(value), Allocator.Temp);
        writer.WriteValueSafe(value);
        cmm.SendNamedMessage(msg, NetworkManager.ServerClientId, writer);
    }

    private static void SendByteToAll(string msg, byte value)
    {
        CustomMessagingManager? cmm = Cmm;
        if (cmm == null) return;
        using var writer = new FastBufferWriter(sizeof(byte), Allocator.Temp);
        writer.WriteValueSafe(value);
        cmm.SendNamedMessageToAll(msg, writer);
    }

    private static void SendByteTo(string msg, ulong clientId, byte value)
    {
        CustomMessagingManager? cmm = Cmm;
        if (cmm == null) return;
        using var writer = new FastBufferWriter(sizeof(byte), Allocator.Temp);
        writer.WriteValueSafe(value);
        cmm.SendNamedMessage(msg, clientId, writer);
    }

    private static void SendByteToServer(string msg, byte value)
    {
        CustomMessagingManager? cmm = Cmm;
        NetworkManager? nm = NetworkManager.Singleton;
        if (cmm == null || nm == null) return;
        using var writer = new FastBufferWriter(sizeof(byte), Allocator.Temp);
        writer.WriteValueSafe(value);
        cmm.SendNamedMessage(msg, NetworkManager.ServerClientId, writer);
    }

    private static void SendTransportToAll(byte kind, float value, float positionSeconds)
    {
        CustomMessagingManager? cmm = Cmm;
        if (cmm == null) return;
        using var writer = new FastBufferWriter(sizeof(byte) + sizeof(float) + sizeof(float), Allocator.Temp);
        writer.WriteValueSafe(kind);
        writer.WriteValueSafe(value);
        writer.WriteValueSafe(positionSeconds);
        cmm.SendNamedMessageToAll(MsgTransport, writer);
    }

    private static void SendTransportToServer(byte kind, float value, float positionSeconds)
    {
        CustomMessagingManager? cmm = Cmm;
        NetworkManager? nm = NetworkManager.Singleton;
        if (cmm == null || nm == null) return;
        using var writer = new FastBufferWriter(sizeof(byte) + sizeof(float) + sizeof(float), Allocator.Temp);
        writer.WriteValueSafe(kind);
        writer.WriteValueSafe(value);
        writer.WriteValueSafe(positionSeconds);
        cmm.SendNamedMessage(MsgReqTransport, NetworkManager.ServerClientId, writer);
    }
}
