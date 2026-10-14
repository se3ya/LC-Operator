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
    private const string MsgState = "Op_CoWatch_State";

    private static readonly PrepareRound _round = new();
    private static int _waitingCount;
    private static string _localPrepareFile = string.Empty;
    private static bool _localReadySent;
    private static readonly ClipWait _localWait = new();
    private static readonly ClipWait _serverWait = new();
    private static bool _localHasFile;
    private static double _nextFileCheck;
    private static readonly PlaybackClock _clock = new();
    private static readonly PlaybackClock _joinClock = new();
    private static string _trackedFile = string.Empty;
    private static bool _localJoin;

    internal static bool IsPreparingLocally => _localPrepareFile.Length > 0;

    internal static bool IsPreparingForLobby => IsServer && _round.IsActive;

    internal static bool IsPreparingFile(string fileName) =>
        string.Equals(_round.FileName, fileName, System.StringComparison.OrdinalIgnoreCase)
        || string.Equals(_localPrepareFile, fileName, System.StringComparison.OrdinalIgnoreCase);

    private static bool _registered;

    internal static bool Enabled =>
        VideoCameraConfig.TerminalCoWatch != null
        && VideoCameraConfig.TerminalCoWatch.Value
        && NetworkManager.Singleton.IsListening;

    private static CustomMessagingManager? Cmm =>
        NetworkManager.Singleton.CustomMessagingManager;

    private static bool IsServer =>
        NetworkManager.Singleton.IsServer;

    internal static void Register()
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm.CustomMessagingManager == null || _registered) return;

        CustomMessagingManager cmm = nm.CustomMessagingManager;
        cmm.RegisterNamedMessageHandler(MsgPlay, OnPlay);
        cmm.RegisterNamedMessageHandler(MsgStop, OnStop);
        cmm.RegisterNamedMessageHandler(MsgTransport, OnTransport);
        cmm.RegisterNamedMessageHandler(MsgPrepare, OnPrepare);
        cmm.RegisterNamedMessageHandler(MsgWaiting, OnWaiting);
        cmm.RegisterNamedMessageHandler(MsgAbort, OnAbort);
        if (!nm.IsServer) cmm.RegisterNamedMessageHandler(MsgState, OnState);
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
            foreach (string name in new[] { MsgPlay, MsgStop, MsgTransport, MsgReqPlay, MsgReqStop, MsgReqTransport, MsgPrepare, MsgReady, MsgGaveUp, MsgWaiting, MsgAbort, MsgState })
            {
                try { cmm.UnregisterNamedMessageHandler(name); } catch { }
            }
        }
        TrackStop();
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

        TrackStop();
        fileName = CassetteLibrary.GetMergedName(fileName);
        var clients = new List<ulong>(LobbyPeers.Listeners) { NetworkManager.Singleton.LocalClientId };
        _round.Begin(fileName, clients);
        _serverWait.Start();
        StartLocalPrepare(fileName);

        Plugin.Log.LogDebug($"Waiting for everyone to have '{fileName}'.");
        SendStringToAll(MsgPrepare, fileName);
        TerminalCassettePlayback.ApplyPrepareLocally(fileName);
    }

    internal static void Update()
    {
        if (!Enabled)
        {
            if (_localPrepareFile.Length > 0) AckWithoutWatching();
            if (IsServer && _round.IsActive) UpdateServerPrepare();
            return;
        }

        if (_localPrepareFile.Length > 0)
        {
            UpdateLocalPrepare();
        }

        if (IsServer && _round.IsActive)
        {
            UpdateServerPrepare();
        }
    }

    private static void UpdateLocalPrepare()
    {
        double now = Time.realtimeSinceStartupAsDouble;
        if (now >= _nextFileCheck)
        {
            _nextFileCheck = now + Constants.Terminal.FileCheckInterval;
            _localHasFile = TerminalCassettePlayback.LocalHasFile(_localPrepareFile);
        }
        if (_localJoin)
        {
            UpdateJoin();
            return;
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
        bool join = _localJoin;
        ClearLocalPrepare();
        TerminalCassettePlayback.ApplyAbortLocally();
        if (join) return;

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
        _round.MarkReady(clientId, fileName);
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
        _round.MarkGaveUp(clientId, fileName);
    }

    private static void UpdateServerPrepare()
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (_serverWait.IsStalled())
        {
            _round.GiveUpPending(nm.ConnectedClientsIds);
            _serverWait.Clear();
        }

        int waiting = _round.CountWaiting(nm.ConnectedClientsIds);
        if (waiting > 0)
        {
            if (_round.IsNewWaiting(waiting))
            {
                _waitingCount = waiting;
                SendByteToAll(MsgWaiting, (byte)Mathf.Min(waiting, byte.MaxValue));
            }
            return;
        }

        PrepareOutcome outcome = _round.Finish();
        ServerClearPrepare();

        string f = outcome.FileName;
        int ready = outcome.Ready.Count;
        int missed = outcome.GaveUp.Count;
        bool hostWatches = outcome.Ready.Contains(nm.LocalClientId);

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

        TrackPlay(f);
        TerminalCassettePlayback.HidePrepareStatus();
        if (Enabled && hostWatches) TerminalCassettePlayback.ApplyPlayLocally(f);
        else TerminalCassettePlayback.ApplyAbortLocally();

        foreach (ulong id in outcome.Ready)
        {
            if (id == nm.LocalClientId) continue;
            if (!IsConnected(id)) continue;
            SendStringTo(MsgPlay, id, f);
        }

        foreach (ulong id in outcome.GaveUp)
        {
            if (id == nm.LocalClientId) continue;
            if (!IsConnected(id)) continue;
            SendByteTo(MsgAbort, id, 0);
        }

        foreach (ulong id in LobbyPeers.Listeners)
        {
            if (outcome.Ready.Contains(id) || outcome.GaveUp.Contains(id)) continue;
            if (!IsConnected(id)) continue;
            SendState(id);
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
            TrackStop();
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
        _round.Clear();
        _serverWait.Clear();
        ClearLocalPrepare();
    }

    private static void TrackPlay(string fileName)
    {
        _trackedFile = fileName;
        _clock.Start(NetworkManager.Singleton.ServerTime.Time);
    }

    internal static void TrackStop()
    {
        _trackedFile = string.Empty;
        _clock.Stop();
    }

    private static void TrackTransport(byte kind, float value, float position)
    {
        if (_trackedFile.Length == 0) return;

        double now = NetworkManager.Singleton.ServerTime.Time;
        _clock.Seek(position, now);
        switch (kind)
        {
            case TransportPause: _clock.Pause(now); break;
            case TransportResume: _clock.Resume(now); break;
            case TransportSpeed: _clock.SetSpeed(value, now); break;
        }
    }

    private static void UpdateJoin()
    {
        string fileName = _localPrepareFile;
        if (_localHasFile)
        {
            ClearLocalPrepare();
            TerminalCassettePlayback.HidePrepareStatus();
            if (!TryJoinNow(fileName)) TerminalCassettePlayback.ApplyAbortLocally();
            return;
        }

        bool stalled = _localWait.IsStalled();
        if (!stalled && !_localWait.IsNothingComing()) return;

        if (stalled) Plugin.Log.LogWarning($"'{fileName}' stopped arriving, so it will not play here.");
        else Plugin.Log.LogInfo($"'{fileName}' is not coming to this machine, so it will not play here.");
        ClearLocalPrepare();
        TerminalCassettePlayback.ApplyAbortLocally();
    }

    private static void StartJoin(string fileName, double position, double stamp, float speed, bool paused)
    {
        _joinClock.Restore(position, stamp, speed, paused);
        if (TryJoinNow(fileName)) return;

        StartLocalPrepare(fileName);
        _localJoin = true;
        TerminalCassettePlayback.ApplyPrepareLocally(fileName);
    }

    private static bool TryJoinNow(string fileName)
    {
        double now = NetworkManager.Singleton.ServerTime.Time;
        double position = _joinClock.Position(now);
        if (!TerminalCassettePlayback.ApplyJoinLocally(fileName, position, _joinClock.Speed, _joinClock.IsPaused)) return false;

        Plugin.Log.LogDebug($"Joined '{fileName}' that is already playing, {position:0} seconds in.");
        return true;
    }

    internal static void SendState(ulong clientId)
    {
        CustomMessagingManager? cmm = Cmm;
        if (cmm == null || !IsServer || _trackedFile.Length == 0) return;
        if (clientId == NetworkManager.Singleton.LocalClientId) return;

        double now = NetworkManager.Singleton.ServerTime.Time;
        int size = sizeof(bool) + sizeof(float) + (sizeof(double) * 2) + FastBufferWriter.GetWriteSize(_trackedFile);
        using var writer = new FastBufferWriter(size, Allocator.Temp);
        writer.WriteValueSafe(_clock.IsPaused);
        writer.WriteValueSafe(_clock.Speed);
        writer.WriteValueSafe(_clock.Position(now));
        writer.WriteValueSafe(now);
        writer.WriteValueSafe(_trackedFile);
        cmm.SendNamedMessage(MsgState, clientId, writer, NetworkDelivery.Reliable);
        Plugin.Log.LogDebug($"Sent the playing state of '{_trackedFile}' to client {clientId}.");
    }

    private static void OnState(ulong sender, FastBufferReader reader)
    {
        if (IsServer || sender != NetworkManager.ServerClientId) return;

        reader.ReadValueSafe(out bool paused);
        reader.ReadValueSafe(out float speed);
        reader.ReadValueSafe(out double position);
        reader.ReadValueSafe(out double stamp);
        reader.ReadValueSafe(out string fileName);
        if (!Enabled || !Recording.ClipName.IsSafe(fileName)) return;
        if (TerminalCassettePlayback.IsPlaying) return;
        if (IsPreparingLocally)
        {
            if (_localJoin) return;
            ClearLocalPrepare();
        }

        StartJoin(fileName, position, stamp, speed, paused);
    }

    private static void StartLocalPrepare(string fileName)
    {
        _localJoin = false;
        _localPrepareFile = fileName;
        _localReadySent = false;
        _waitingCount = 0;
        _localHasFile = false;
        _nextFileCheck = 0d;
        _localWait.Start();
    }

    private static void ClearLocalPrepare()
    {
        _localJoin = false;
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
            ApplyAsServer(kind, value, positionSeconds);
        }
        else
        {
            SendTransportToServer(kind, value, positionSeconds);
        }
    }

    private static void ApplyAsServer(byte kind, float value, float positionSeconds)
    {
        TerminalCassettePlayback.ApplyTransportLocally(kind, value, positionSeconds);
        TrackTransport(kind, value, positionSeconds);
        SendTransportToAll(kind, value, positionSeconds);
    }

    private static void OnRequestPlay(ulong sender, FastBufferReader reader)
    {
        reader.ReadValueSafe(out string fileName);
        if (_round.IsActive) return;
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
        TrackStop();
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
        ApplyAsServer(kind, value, positionSeconds);
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
        if (cmm == null) return;
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
        if (cmm == null) return;
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
        if (cmm == null) return;
        using var writer = new FastBufferWriter(sizeof(byte) + sizeof(float) + sizeof(float), Allocator.Temp);
        writer.WriteValueSafe(kind);
        writer.WriteValueSafe(value);
        writer.WriteValueSafe(positionSeconds);
        cmm.SendNamedMessage(MsgReqTransport, NetworkManager.ServerClientId, writer);
    }
}
