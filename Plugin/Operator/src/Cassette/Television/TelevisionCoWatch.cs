using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Operator.Cassette;

internal static class TelevisionCoWatch
{
    private const string MsgInsert = "Op_TV_Insert";
    private const string MsgReqInsert = "Op_TV_ReqInsert";
    private const string MsgPlay = "Op_TV_Play";
    private const string MsgReqPlay = "Op_TV_ReqPlay";
    private const string MsgStop = "Op_TV_Stop";
    private const string MsgReqStop = "Op_TV_ReqStop";
    private const string MsgPause = "Op_TV_Pause";
    private const string MsgReqPause = "Op_TV_ReqPause";
    private const string MsgSpeed = "Op_TV_Speed";
    private const string MsgReqSpeed = "Op_TV_ReqSpeed";
    private const string MsgEject = "Op_TV_Eject";
    private const string MsgReqEject = "Op_TV_ReqEject";
    private const string MsgState = "Op_TV_State";
    private const string MsgPrepare = "Op_TV_Prepare";
    private const string MsgReady = "Op_TV_Ready";
    private const string MsgGaveUp = "Op_TV_GaveUp";
    private const string MsgWaiting = "Op_TV_Waiting";
    private const string MsgAbort = "Op_TV_Abort";

    private static readonly string[] AllMessages =
    {
        MsgInsert, MsgPlay, MsgStop, MsgPause, MsgSpeed, MsgEject,
        MsgReqInsert, MsgReqPlay, MsgReqStop, MsgReqPause, MsgReqSpeed, MsgReqEject, MsgState,
        MsgPrepare, MsgReady, MsgGaveUp, MsgWaiting, MsgAbort,
    };

    private static readonly PrepareRound _round = new();
    private static readonly ClipWait _serverWait = new();
    private static readonly ClipWait _localWait = new();
    private static string _localFile = string.Empty;
    private static bool _localJoin;
    private static bool _localReadySent;
    private static bool _localHasFile;
    private static int _waitingCount;
    private static double _nextFileCheck;

    private static bool _registered;
    private static bool _finishedHooked;

    internal static bool IsPlayRequested => TelevisionPlayback.IsPlayRequested;
    internal static bool HasLoaded => TelevisionPlayback.HasLoaded;
    internal static bool IsPaused => TelevisionPlayback.IsPaused;
    internal static float CurrentSpeed => TelevisionPlayback.CurrentSpeed;
    internal static string LoadedFileName => TelevisionPlayback.LoadedFileName;
    internal static bool IsPreparing => _round.IsActive || _localFile.Length > 0;
    internal static int SnapshotVersion { get; private set; }

    private static CustomMessagingManager? Cmm =>
        NetworkManager.Singleton.CustomMessagingManager;

    private static bool IsServer =>
        NetworkManager.Singleton.IsServer;

    private static bool Watching =>
        VideoCameraConfig.TelevisionCassetteWatching.Value && CassetteTakerManager.IsRunning;

    internal static void Register()
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm.CustomMessagingManager == null || _registered) return;

        CustomMessagingManager cmm = nm.CustomMessagingManager;
        cmm.RegisterNamedMessageHandler(MsgInsert, OnInsert);
        cmm.RegisterNamedMessageHandler(MsgPlay, OnPlay);
        cmm.RegisterNamedMessageHandler(MsgStop, OnStop);
        cmm.RegisterNamedMessageHandler(MsgPause, OnPause);
        cmm.RegisterNamedMessageHandler(MsgSpeed, OnSpeed);
        cmm.RegisterNamedMessageHandler(MsgEject, OnEject);
        if (!nm.IsServer) cmm.RegisterNamedMessageHandler(MsgState, OnState);
        cmm.RegisterNamedMessageHandler(MsgPrepare, OnPrepare);
        cmm.RegisterNamedMessageHandler(MsgWaiting, OnWaiting);
        cmm.RegisterNamedMessageHandler(MsgAbort, OnAbort);
        if (nm.IsServer)
        {
            cmm.RegisterNamedMessageHandler(MsgReqInsert, OnRequestInsert);
            cmm.RegisterNamedMessageHandler(MsgReqPlay, OnRequestPlay);
            cmm.RegisterNamedMessageHandler(MsgReqStop, OnRequestStop);
            cmm.RegisterNamedMessageHandler(MsgReqPause, OnRequestPause);
            cmm.RegisterNamedMessageHandler(MsgReqSpeed, OnRequestSpeed);
            cmm.RegisterNamedMessageHandler(MsgReqEject, OnRequestEject);
            cmm.RegisterNamedMessageHandler(MsgReady, OnReady);
            cmm.RegisterNamedMessageHandler(MsgGaveUp, OnGaveUp);
        }

        if (!_finishedHooked)
        {
            TelevisionPlayback.LocalPlaybackFinished += OnLocalPlaybackFinished;
            _finishedHooked = true;
        }

        _registered = true;
    }

    internal static void Unregister()
    {
        CustomMessagingManager? cmm = Cmm;
        if (cmm != null)
        {
            foreach (string name in AllMessages)
            {
                try { cmm.UnregisterNamedMessageHandler(name); } catch { }
            }
        }

        if (_finishedHooked)
        {
            TelevisionPlayback.LocalPlaybackFinished -= OnLocalPlaybackFinished;
            _finishedHooked = false;
        }

        ForceLocalStop();
        _registered = false;
    }

    internal static void RequestInsert(CassetteData data)
    {
        if (Cmm == null || data.IsEmpty) return;
        if (IsServer) { ApplyInsert(data); SendDataToAll(MsgInsert, data); }
        else SendDataToServer(MsgReqInsert, data);
    }

    internal static void RequestPlay()
    {
        if (Cmm == null) return;
        if (IsServer) ServerBeginPrepare();
        else SendByteToServer(MsgReqPlay, 0);
    }

    internal static void RequestStop() => SendByte(MsgStop, MsgReqStop, 0, ApplyStop);
    internal static void RequestPause(bool paused) => SendByte(MsgPause, MsgReqPause, (byte)(paused ? 1 : 0), () => ApplyPause(paused));

    internal static void RequestSpeed(float speed)
    {
        if (Cmm == null) return;
        if (IsServer) { ApplySpeed(speed); SendFloatToAll(MsgSpeed, speed); }
        else SendFloatToServer(MsgReqSpeed, speed);
    }

    internal static void RequestEject()
    {
        if (Cmm == null || !HasLoaded) return;
        if (IsServer) ServerEject();
        else SendByteToServer(MsgReqEject, 0);
    }

    internal static void StopForOrbit()
    {
        ClearPrepare();
        if (HasLoaded) TelevisionPlayback.SetPlaying(false);
    }

    internal static void ForceLocalStop()
    {
        ClearPrepare();
        TelevisionPlayback.ClearAll();
        SnapshotVersion++;
    }

    internal static void SendSnapshot(ulong clientId)
    {
        CustomMessagingManager? cmm = Cmm;
        if (cmm == null || !IsServer) return;

        bool loaded = TelevisionPlayback.HasLoaded;
        CassetteData data = TelevisionPlayback.LoadedData;
        TVScript? tv = CassetteTakerManager.ActiveTv;
        int size = (sizeof(bool) * 4) + sizeof(float) + (sizeof(double) * 2) + (loaded ? DataSize(data) : 0);
        using var writer = new FastBufferWriter(size, Allocator.Temp);
        writer.WriteValueSafe(loaded);
        if (loaded) WriteData(writer, data);
        writer.WriteValueSafe(TelevisionPlayback.IsPlayRequested);
        writer.WriteValueSafe(TelevisionPlayback.IsPaused);
        writer.WriteValueSafe(TelevisionPlayback.CurrentSpeed);
        writer.WriteValueSafe(tv != null && tv.tvOn);
        writer.WriteValueSafe(TelevisionPlayback.Position);
        writer.WriteValueSafe(NetworkManager.Singleton.ServerTime.Time);
        cmm.SendNamedMessage(MsgState, clientId, writer);
    }

    private static void OnLocalPlaybackFinished()
    {
        if (!IsServer) return;
        if (IsPlayRequested) RequestStop();
    }

    internal static void Update()
    {
        if (_localFile.Length > 0) UpdateLocalPrepare();
        if (_round.IsActive && IsServer) UpdateServerPrepare();
    }

    private static void ServerBeginPrepare()
    {
        if (!HasLoaded || IsPlayRequested || _round.IsActive) return;

        if (!Watching)
        {
            ApplyPlay();
            SendByteToAll(MsgPlay, 0);
            return;
        }

        string file = LoadedFileName;
        var clients = new List<ulong>(LobbyPeers.Listeners) { NetworkManager.Singleton.LocalClientId };

        _round.Begin(file, clients);
        _serverWait.Start();
        StartLocalPrepare(file, false);
        Plugin.Log.LogDebug($"Waiting for everyone to have '{file}'.");
        SendStringToListeners(MsgPrepare, file);
    }

    private static void UpdateLocalPrepare()
    {
        double now = Time.realtimeSinceStartupAsDouble;
        if (now >= _nextFileCheck)
        {
            _nextFileCheck = now + Constants.Terminal.FileCheckInterval;
            _localHasFile = TerminalCassettePlayback.LocalHasFile(_localFile);
        }

        if (_localJoin && _localHasFile)
        {
            ClearLocalPrepare();
            return;
        }

        TelevisionPlayback.SetCard(PrepareText.For(_localHasFile, _waitingCount));
        if (_localReadySent) return;

        string fileName = _localFile;
        if (_localHasFile)
        {
            _localReadySent = true;
            if (IsServer) _round.MarkReady(NetworkManager.Singleton.LocalClientId, fileName);
            else SendStringToServer(MsgReady, fileName);
            return;
        }

        bool stalled = _localWait.IsStalled();
        if (!stalled && !_localWait.IsNothingComing()) return;

        if (stalled) Plugin.Log.LogWarning($"'{fileName}' stopped arriving, dropping out of shared playback.");
        else Plugin.Log.LogInfo($"'{fileName}' is not coming to this machine, dropping out of shared playback.");

        bool join = _localJoin;
        ClearLocalPrepare();
        if (join) return;

        if (IsServer) _round.MarkGaveUp(NetworkManager.Singleton.LocalClientId, fileName);
        else SendStringToServer(MsgGaveUp, fileName);
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
                SendByteToListeners(MsgWaiting, (byte)Mathf.Min(waiting, byte.MaxValue));
            }
            return;
        }

        PrepareOutcome outcome = _round.Finish();
        ClearPrepare();

        string f = outcome.FileName;
        int ready = outcome.Ready.Count;
        int missed = outcome.GaveUp.Count;

        if (ready == 0)
        {
            Plugin.Log.LogWarning($"Nobody has '{f}', so there is nothing to play.");
            SendByteToListeners(MsgAbort, 0);
            return;
        }

        if (missed > 0) Plugin.Log.LogWarning($"{missed} of {ready + missed} players never got '{f}'. Playing for rest.");
        else Plugin.Log.LogDebug($"All peers have '{f}', starting synchronized playback.");

        ApplyPlay();
        SendByteToAll(MsgPlay, 0);
    }

    private static void StartLocalPrepare(string fileName, bool join)
    {
        _localFile = fileName;
        _localJoin = join;
        _localReadySent = false;
        _localHasFile = false;
        _waitingCount = 0;
        _nextFileCheck = 0d;
        _localWait.Start();
    }

    private static void ClearLocalPrepare()
    {
        _localFile = string.Empty;
        _localJoin = false;
        _localReadySent = false;
        _localHasFile = false;
        _waitingCount = 0;
        _nextFileCheck = 0d;
        _localWait.Clear();
        TelevisionPlayback.SetCard(string.Empty);
    }

    private static void ClearPrepare()
    {
        _round.Clear();
        _serverWait.Clear();
        ClearLocalPrepare();
    }

    private static void OnPrepare(ulong sender, FastBufferReader reader)
    {
        if (IsServer) return;
        reader.ReadValueSafe(out string fileName);
        if (!Watching)
        {
            SendStringToServer(MsgReady, fileName);
            return;
        }
        StartLocalPrepare(fileName, false);
    }

    private static void OnReady(ulong sender, FastBufferReader reader)
    {
        reader.ReadValueSafe(out string fileName);
        _round.MarkReady(sender, fileName);
    }

    private static void OnGaveUp(ulong sender, FastBufferReader reader)
    {
        reader.ReadValueSafe(out string fileName);
        _round.MarkGaveUp(sender, fileName);
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
    }

    private static void ApplyInsert(CassetteData data)
    {
        ClearPrepare();
        TelevisionPlayback.SetLoaded(data);
    }

    private static void ApplyPlay()
    {
        ClearLocalPrepare();
        TelevisionPlayback.SetPlaying(true);
    }

    private static void ApplyStop()
    {
        ClearPrepare();
        TelevisionPlayback.SetPlaying(false);
    }

    private static void ApplyPause(bool paused) => TelevisionPlayback.SetPaused(paused);
    private static void ApplySpeed(float speed) => TelevisionPlayback.SetSpeed(speed);

    private static void ApplyEject()
    {
        ClearPrepare();
        TelevisionPlayback.SetLoaded(CassetteData.Empty);
    }

    private static void ServerEject()
    {
        if (!IsServer) return;
        CassetteData data = TelevisionPlayback.LoadedData;
        if (!data.IsEmpty) SpawnEjectedCassette(data);
        ApplyEject();
        SendByteToAll(MsgEject, 0);
    }

    private static void SpawnEjectedCassette(CassetteData data)
    {
        CassetteTakerDeck? deck = CassetteTakerManager.ActiveDeck;
        if (deck == null || CassetteRegistrar.CassettePrefab == null)
        {
            Plugin.Log.LogError("Could not spawn ejected cassette, deck or its prefab is missing.");
            return;
        }

        deck.GetSlotPose(out Vector3 pos, out Quaternion rot, out Vector3 dir);

        GameObject go = UnityEngine.Object.Instantiate(CassetteRegistrar.CassettePrefab, pos, rot);
        if (!go.TryGetComponent(out NetworkObject netObj))
        {
            UnityEngine.Object.Destroy(go);
            return;
        }
        netObj.Spawn(destroyWithScene: true);

        CassetteItem? cassette = go.GetComponent<CassetteItem>();
        if (cassette == null) return;

        cassette.ServerSetMetadata(data.Id, data.FileName, data.Timestamp, data.Duration, data.Value, EnemyBountyTracker.Serialize(EnemyBountyTracker.Parse(data.Breakdown ?? string.Empty, trusted: true)), encoding: false);
        try { cassette.Start(); } catch (Exception ex) { Plugin.Log.LogWarning($"Failed to start ejected cassette: {ex.Message}"); }
        cassette.transform.position = pos;
        cassette.startFallingPosition = pos;
        try { cassette.EnableItemMeshes(true); } catch { }

        float outForce = Constants.Television.DeckEjectOutForce;
        float upForce = Constants.Television.DeckEjectUpForce;
        if (UnityEngine.Random.value < Constants.Television.DeckEjectCrazyChance)
        {
            outForce *= Constants.Television.DeckEjectCrazyMultiplier;
            upForce *= Constants.Television.DeckEjectCrazyMultiplier;
        }

        bool tossed = false;
        try
        {
            tossed = cassette.ServerBeginEjectToss(
                dir,
                outForce,
                upForce,
                Constants.Television.DeckEjectSettleVelocity,
                Constants.Television.DeckEjectSettleTimeout);
        }
        catch (Exception ex) { Plugin.Log.LogDebug($"Failed to toss ejected cassette: {ex.Message}"); }

        if (!tossed)
        {
            try { cassette.FallToGround(); } catch { }
        }

        Plugin.Log.LogDebug($"Ejected cassette '{data.Id}' out of deck.");
    }

    private static void OnRequestInsert(ulong sender, FastBufferReader reader)
    {
        CassetteData d = ReadData(reader);
        if (!Recording.ClipName.IsSafe(d.FileName)) return;

        CassetteItem? tape = CassetteItem.FindById(d.Id, d.FileName);
        if (tape == null)
        {
            Plugin.Log.LogWarning($"Deck insert from client {sender} refused, host has no cassette '{d.Id}'.");
            return;
        }

        d.Value = CassetteLimits.ClampStoredValue(tape.scrapValue);
        d.Breakdown = tape.BreakdownRaw;
        ApplyInsert(d);
        SendDataToAll(MsgInsert, d);
    }
    private static void OnRequestPlay(ulong sender, FastBufferReader reader) { ServerBeginPrepare(); }
    private static void OnRequestStop(ulong sender, FastBufferReader reader) { ApplyStop(); SendByteToAll(MsgStop, 0); }
    private static void OnRequestPause(ulong sender, FastBufferReader reader) { reader.ReadValueSafe(out byte v); ApplyPause(v != 0); SendByteToAll(MsgPause, v); }
    private static void OnRequestSpeed(ulong sender, FastBufferReader reader) { reader.ReadValueSafe(out float v); if (float.IsNaN(v) || float.IsInfinity(v)) return; ApplySpeed(v); SendFloatToAll(MsgSpeed, v); }
    private static void OnRequestEject(ulong sender, FastBufferReader reader) { ServerEject(); }

    private static void OnInsert(ulong sender, FastBufferReader reader) { if (IsServer) return; ApplyInsert(ReadData(reader)); }
    private static void OnPlay(ulong sender, FastBufferReader reader) { if (IsServer) return; ApplyPlay(); }
    private static void OnStop(ulong sender, FastBufferReader reader) { if (IsServer) return; ApplyStop(); }
    private static void OnPause(ulong sender, FastBufferReader reader) { if (IsServer) return; reader.ReadValueSafe(out byte v); ApplyPause(v != 0); }
    private static void OnSpeed(ulong sender, FastBufferReader reader) { if (IsServer) return; reader.ReadValueSafe(out float v); ApplySpeed(v); }
    private static void OnEject(ulong sender, FastBufferReader reader) { if (IsServer) return; ApplyEject(); }

    private static void OnState(ulong sender, FastBufferReader reader)
    {
        if (IsServer || sender != NetworkManager.ServerClientId) return;

        reader.ReadValueSafe(out bool loaded);
        CassetteData data = loaded ? ReadData(reader) : CassetteData.Empty;
        reader.ReadValueSafe(out bool playing);
        reader.ReadValueSafe(out bool paused);
        reader.ReadValueSafe(out float speed);
        reader.ReadValueSafe(out bool tvOn);
        reader.ReadValueSafe(out double position);
        reader.ReadValueSafe(out double stamp);

        TVScript? tv = CassetteTakerManager.ActiveTv != null ? CassetteTakerManager.ActiveTv : UnityEngine.Object.FindObjectOfType<TVScript>();
        if (tv != null && tvOn && !tv.tvOn) tv.tvOn = true;

        CassetteData current = TelevisionPlayback.LoadedData;
        bool same = loaded && TelevisionPlayback.HasLoaded && current.Id == data.Id && current.FileName == data.FileName;
        if (!same) ApplyInsert(data);
        if (playing) ApplyPlay();
        else ApplyStop();
        ApplyPause(playing && paused);
        ApplySpeed(speed);
        bool running = loaded && playing && !paused && tvOn;
        double elapsed = running ? Math.Max(0.0, NetworkManager.Singleton.ServerTime.Time - stamp) * speed : 0.0;
        TelevisionPlayback.SetClock(position + elapsed);
        SnapshotVersion++;
        if (loaded && playing && Watching && !TerminalCassettePlayback.LocalHasFile(data.FileName))
        {
            StartLocalPrepare(data.FileName, true);
        }
    }

    private static void SendByte(string broadcastMsg, string reqMsg, byte value, Action applyOnServer)
    {
        if (Cmm == null) return;
        if (IsServer) { applyOnServer(); SendByteToAll(broadcastMsg, value); }
        else SendByteToServer(reqMsg, value);
    }

    private static void WriteData(FastBufferWriter w, in CassetteData d)
    {
        w.WriteValueSafe(d.Id ?? string.Empty);
        w.WriteValueSafe(d.FileName ?? string.Empty);
        w.WriteValueSafe(d.Timestamp ?? string.Empty);
        w.WriteValueSafe(d.Duration);
        w.WriteValueSafe(d.Value);
        w.WriteValueSafe(d.Breakdown ?? string.Empty);
    }

    private static CassetteData ReadData(FastBufferReader r)
    {
        r.ReadValueSafe(out string id);
        r.ReadValueSafe(out string fileName);
        r.ReadValueSafe(out string timestamp);
        r.ReadValueSafe(out float duration);
        r.ReadValueSafe(out int value);
        r.ReadValueSafe(out string breakdown);
        return new CassetteData { Id = id, FileName = fileName, Timestamp = timestamp, Duration = duration, Value = value, Breakdown = breakdown };
    }

    private static int DataSize(in CassetteData d) =>
        FastBufferWriter.GetWriteSize(d.Id ?? string.Empty)
        + FastBufferWriter.GetWriteSize(d.FileName ?? string.Empty)
        + FastBufferWriter.GetWriteSize(d.Timestamp ?? string.Empty)
        + sizeof(float) + sizeof(int)
        + FastBufferWriter.GetWriteSize(d.Breakdown ?? string.Empty);

    private static void SendDataToAll(string msg, CassetteData data)
    {
        CustomMessagingManager? cmm = Cmm;
        if (cmm == null) return;
        using var writer = new FastBufferWriter(DataSize(data), Allocator.Temp);
        WriteData(writer, data);
        cmm.SendNamedMessageToAll(msg, writer);
    }

    private static void SendDataToServer(string msg, CassetteData data)
    {
        CustomMessagingManager? cmm = Cmm;
        if (cmm == null) return;
        using var writer = new FastBufferWriter(DataSize(data), Allocator.Temp);
        WriteData(writer, data);
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

    private static void SendByteToServer(string msg, byte value)
    {
        CustomMessagingManager? cmm = Cmm;
        if (cmm == null) return;
        using var writer = new FastBufferWriter(sizeof(byte), Allocator.Temp);
        writer.WriteValueSafe(value);
        cmm.SendNamedMessage(msg, NetworkManager.ServerClientId, writer);
    }

    private static void SendFloatToAll(string msg, float value)
    {
        CustomMessagingManager? cmm = Cmm;
        if (cmm == null) return;
        using var writer = new FastBufferWriter(sizeof(float), Allocator.Temp);
        writer.WriteValueSafe(value);
        cmm.SendNamedMessageToAll(msg, writer);
    }

    private static void SendFloatToServer(string msg, float value)
    {
        CustomMessagingManager? cmm = Cmm;
        if (cmm == null) return;
        using var writer = new FastBufferWriter(sizeof(float), Allocator.Temp);
        writer.WriteValueSafe(value);
        cmm.SendNamedMessage(msg, NetworkManager.ServerClientId, writer);
    }

    private static bool IsConnected(ulong clientId)
    {
        foreach (ulong id in NetworkManager.Singleton.ConnectedClientsIds)
        {
            if (id == clientId) return true;
        }
        return false;
    }

    private static void SendStringToServer(string msg, string value)
    {
        CustomMessagingManager? cmm = Cmm;
        if (cmm == null) return;
        using var writer = new FastBufferWriter(FastBufferWriter.GetWriteSize(value), Allocator.Temp);
        writer.WriteValueSafe(value);
        cmm.SendNamedMessage(msg, NetworkManager.ServerClientId, writer);
    }

    private static void SendStringToListeners(string msg, string value)
    {
        CustomMessagingManager? cmm = Cmm;
        if (cmm == null) return;
        ulong local = NetworkManager.Singleton.LocalClientId;
        foreach (ulong id in LobbyPeers.Listeners)
        {
            if (id == local || !IsConnected(id)) continue;
            using var writer = new FastBufferWriter(FastBufferWriter.GetWriteSize(value), Allocator.Temp);
            writer.WriteValueSafe(value);
            cmm.SendNamedMessage(msg, id, writer);
        }
    }

    private static void SendByteToListeners(string msg, byte value)
    {
        CustomMessagingManager? cmm = Cmm;
        if (cmm == null) return;
        ulong local = NetworkManager.Singleton.LocalClientId;
        foreach (ulong id in LobbyPeers.Listeners)
        {
            if (id == local || !IsConnected(id)) continue;
            using var writer = new FastBufferWriter(sizeof(byte), Allocator.Temp);
            writer.WriteValueSafe(value);
            cmm.SendNamedMessage(msg, id, writer);
        }
    }
}
