using System;
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

    private static readonly string[] AllMessages =
    {
        MsgInsert, MsgPlay, MsgStop, MsgPause, MsgSpeed, MsgEject,
        MsgReqInsert, MsgReqPlay, MsgReqStop, MsgReqPause, MsgReqSpeed, MsgReqEject, MsgState,
    };

    private static bool _registered;
    private static bool _finishedHooked;

    internal static bool IsPlayRequested => TelevisionPlayback.IsPlayRequested;
    internal static bool HasLoaded => TelevisionPlayback.HasLoaded;
    internal static bool IsPaused => TelevisionPlayback.IsPaused;
    internal static float CurrentSpeed => TelevisionPlayback.CurrentSpeed;
    internal static string LoadedFileName => TelevisionPlayback.LoadedFileName;
    internal static int SnapshotVersion { get; private set; }

    private static CustomMessagingManager? Cmm =>
        NetworkManager.Singleton != null ? NetworkManager.Singleton.CustomMessagingManager : null;

    private static bool IsServer =>
        NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;

    internal static void Register()
    {
        NetworkManager? nm = NetworkManager.Singleton;
        if (nm == null || nm.CustomMessagingManager == null || _registered) return;

        CustomMessagingManager cmm = nm.CustomMessagingManager;
        cmm.RegisterNamedMessageHandler(MsgInsert, OnInsert);
        cmm.RegisterNamedMessageHandler(MsgPlay, OnPlay);
        cmm.RegisterNamedMessageHandler(MsgStop, OnStop);
        cmm.RegisterNamedMessageHandler(MsgPause, OnPause);
        cmm.RegisterNamedMessageHandler(MsgSpeed, OnSpeed);
        cmm.RegisterNamedMessageHandler(MsgEject, OnEject);
        if (!nm.IsServer) cmm.RegisterNamedMessageHandler(MsgState, OnState);
        if (nm.IsServer)
        {
            cmm.RegisterNamedMessageHandler(MsgReqInsert, OnRequestInsert);
            cmm.RegisterNamedMessageHandler(MsgReqPlay, OnRequestPlay);
            cmm.RegisterNamedMessageHandler(MsgReqStop, OnRequestStop);
            cmm.RegisterNamedMessageHandler(MsgReqPause, OnRequestPause);
            cmm.RegisterNamedMessageHandler(MsgReqSpeed, OnRequestSpeed);
            cmm.RegisterNamedMessageHandler(MsgReqEject, OnRequestEject);
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

    internal static void RequestPlay() => SendByte(MsgPlay, MsgReqPlay, 0, ApplyPlay);
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
        if (HasLoaded) TelevisionPlayback.SetPlaying(false);
    }

    internal static void ForceLocalStop()
    {
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
        int size = sizeof(bool) * 4 + sizeof(float) + sizeof(double) * 2 + (loaded ? DataSize(data) : 0);
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

    private static void ApplyInsert(CassetteData data) => TelevisionPlayback.SetLoaded(data);
    private static void ApplyPlay() => TelevisionPlayback.SetPlaying(true);
    private static void ApplyStop() => TelevisionPlayback.SetPlaying(false);
    private static void ApplyPause(bool paused) => TelevisionPlayback.SetPaused(paused);
    private static void ApplySpeed(float speed) => TelevisionPlayback.SetSpeed(speed);
    private static void ApplyEject() => TelevisionPlayback.SetLoaded(CassetteData.Empty);

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
    private static void OnRequestPlay(ulong sender, FastBufferReader reader) { ApplyPlay(); SendByteToAll(MsgPlay, 0); }
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
}
