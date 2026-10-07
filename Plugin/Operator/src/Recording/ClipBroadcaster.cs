using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Operator.Cassette;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Operator.Recording;

internal sealed class ClipBroadcaster : MonoBehaviour
{
    private const string MsgBegin = "Op_Clip_Begin";
    private const string MsgChunk = "Op_Clip_Chunk";
    private const string MsgEnd = "Op_Clip_End";
    private const string MsgAck = "Op_Clip_Ack";
    private const string MsgFloor = "Op_Clip_Floor";
    private const string MsgAbort = "Op_Clip_Abort";
    private const string MsgResend = "Op_Clip_Resend";
    private const string MsgForward = "Op_Clip_Forward";

    private static readonly string[] AllMessages =
    {
        MsgBegin, MsgChunk, MsgEnd, MsgAck, MsgFloor, MsgAbort, MsgResend, MsgForward,
    };

    private static readonly int ChunkSize = Constants.ClipTransfer.ChunkSize;
    private static readonly int FlowWindowChunks = Constants.ClipTransfer.FlowWindowChunks;
    private static readonly int AckIntervalChunks = Constants.ClipTransfer.AckIntervalChunks;
    private static readonly float FlowStallTimeout = Constants.ClipTransfer.FlowStallTimeout;
    private static readonly int MaxResendChunks = Constants.ClipTransfer.MaxResendChunks;
    private static readonly float RetryDelay = Constants.ClipTransfer.RetryDelay;
    private static readonly int MaxRetries = Constants.ClipTransfer.MaxRetries;
    private static readonly float RecoveryTimeout = Constants.ClipTransfer.RecoveryTimeout;
    private static readonly float SenderCacheTime = Constants.ClipTransfer.SenderCacheTime;

    private sealed class IncomingClip
    {
        public int ExpectedChunks;
        public ulong SenderClientId;
        public FileStream? Stream;
        public string PartPath = string.Empty;
        public string FinalPath = string.Empty;
        public readonly HashSet<int> ReceivedIds = new();
        public float LastActivity;
        public int LastAckedCount;
        public bool EndSignaled;
    }

    private readonly Dictionary<string, IncomingClip> _incoming = new();

    internal bool TryGetIncomingProgress(string fileName, out int received, out int expected)
    {
        received = 0;
        expected = 0;
        if (string.IsNullOrEmpty(fileName)) return false;
        if (!_incoming.TryGetValue(fileName, out IncomingClip? clip) || clip == null) return false;
        received = clip.ReceivedIds.Count;
        expected = clip.ExpectedChunks;
        return true;
    }

    internal bool HasIncoming => _incoming.Count > 0;

    internal int ProgressChunks()
    {
        int received = 0;
        foreach (IncomingClip clip in _incoming.Values)
        {
            received += clip.ReceivedIds.Count;
        }
        foreach (Dictionary<ulong, int> map in _ackByReceiver.Values)
        {
            foreach (int acked in map.Values) received += acked;
        }
        return received;
    }

    private readonly HashSet<string> _monitoring = new();

    private readonly Queue<(string FileName, byte[] Bytes, int ChunkCount)> _pendingBroadcasts = new();
    private bool _broadcastPumpRunning;

    private readonly Dictionary<string, byte[]> _outgoingCache = new();
    private readonly Dictionary<string, ulong> _senderByFile = new();
    private readonly Dictionary<string, HashSet<ulong>> _recipients = new();
    private readonly Dictionary<string, int> _sendFloor = new();

    private readonly Dictionary<string, Dictionary<ulong, int>> _ackByReceiver = new();
    private readonly Dictionary<string, int> _lastFloorSent = new();
    private readonly Dictionary<string, float> _beginTime = new();

    private bool _registered;
    private float _nextFloorPoll;

    public static ClipBroadcaster? Instance { get; private set; }

    public static ClipBroadcaster GetOrCreate()
    {
        if (Instance != null) return Instance;

        var host = new GameObject("OperatorClipBroadcaster");
        DontDestroyOnLoad(host);
        host.hideFlags = HideFlags.HideAndDontSave;
        Instance = host.AddComponent<ClipBroadcaster>();
        return Instance;
    }

    private static NetworkManager? Net => NetworkManager.Singleton;

    private static CustomMessagingManager? Cmm =>
        Net != null ? Net.CustomMessagingManager : null;

    private static bool IsServer => Net != null && Net.IsServer;

    private static ulong LocalId => Net != null ? Net.LocalClientId : 0UL;

    public bool IsReady => _registered && Cmm != null;

    public void Register()
    {
        CustomMessagingManager? cmm = Cmm;
        if (cmm == null || _registered) return;

        cmm.RegisterNamedMessageHandler(MsgBegin, OnBegin);
        cmm.RegisterNamedMessageHandler(MsgChunk, OnChunk);
        cmm.RegisterNamedMessageHandler(MsgEnd, OnEnd);
        cmm.RegisterNamedMessageHandler(MsgFloor, OnFloor);
        cmm.RegisterNamedMessageHandler(MsgAbort, OnAbort);
        cmm.RegisterNamedMessageHandler(MsgForward, OnForward);
        if (IsServer)
        {
            cmm.RegisterNamedMessageHandler(MsgAck, OnAck);
            cmm.RegisterNamedMessageHandler(MsgResend, OnResendRequest);
        }
        _registered = true;
    }

    public void Unregister()
    {
        CustomMessagingManager? cmm = Cmm;
        if (cmm != null)
        {
            foreach (string name in AllMessages)
            {
                try { cmm.UnregisterNamedMessageHandler(name); } catch { }
            }
        }
        _registered = false;
        StopAllCoroutines();
        _broadcastPumpRunning = false;

        foreach (IncomingClip clip in _incoming.Values)
        {
            try { clip.Stream?.Dispose(); } catch { }
        }
        _incoming.Clear();
        _monitoring.Clear();
        _pendingBroadcasts.Clear();
        _outgoingCache.Clear();
        _senderByFile.Clear();
        _recipients.Clear();
        _sendFloor.Clear();
        _ackByReceiver.Clear();
        _lastFloorSent.Clear();
        _beginTime.Clear();
    }

    private void SetRecipients(string fileName, ulong sender)
    {
        NetworkManager? manager = Net;
        if (manager == null || !IsServer) return;

        var listeners = new HashSet<ulong>();
        foreach (ulong id in LobbyPeers.Listeners)
        {
            if (id != sender) listeners.Add(id);
        }

        _recipients[fileName] = listeners;
        _beginTime[fileName] = Time.realtimeSinceStartup;
    }

    private static bool AnyoneToSendTo()
    {
        NetworkManager? manager = NetworkManager.Singleton;
        if (manager == null || !manager.IsListening) return false;

        try
        {
            return manager.ConnectedClientsIds.Count > 1;
        }
        catch
        {
            return false;
        }
    }

    public void BeginBroadcast(string filePath)
    {
        if (!IsReady)
        {
            Plugin.Log.LogWarning("Clip transfer was not ready, recording was not sent.");
            return;
        }

        if (!File.Exists(filePath))
        {
            Plugin.Log.LogWarning($"Could not send recording, file not found: {filePath}");
            return;
        }

        if (!AnyoneToSendTo())
        {
            return;
        }

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(filePath);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"Failed to read recording to send it: {ex.Message}");
            return;
        }

        string fileName = Path.GetFileName(filePath);
        if (_outgoingCache.ContainsKey(fileName))
        {
            Plugin.Log.LogDebug($"Already sending '{fileName}', skipping.");
            return;
        }
        int chunkCount = (bytes.Length + ChunkSize - 1) / ChunkSize;
        if (chunkCount <= 0 || chunkCount > Constants.ClipTransfer.MaxChunks)
        {
            Plugin.Log.LogWarning($"Could not send '{fileName}', {bytes.Length} bytes is out of range.");
            return;
        }

        _outgoingCache[fileName] = bytes;
        _pendingBroadcasts.Enqueue((fileName, bytes, chunkCount));
        Plugin.Log.LogDebug($"Queued '{fileName}' to send, {bytes.Length / 1024} KB in {chunkCount} chunks.");

        if (!_broadcastPumpRunning)
        {
            _broadcastPumpRunning = true;
            StartCoroutine(BroadcastPumpCoroutine());
        }
    }

    private IEnumerator BroadcastPumpCoroutine()
    {
        while (_pendingBroadcasts.Count > 0)
        {
            (string fileName, byte[] bytes, int chunkCount) = _pendingBroadcasts.Dequeue();
            yield return SendChunksCoroutine(fileName, bytes, chunkCount);
        }
        _broadcastPumpRunning = false;
    }

    private IEnumerator SendChunksCoroutine(string fileName, byte[] bytes, int chunkCount)
    {
        _senderByFile[fileName] = LocalId;
        CleanupFlowState(fileName);
        SetRecipients(fileName, LocalId);
        AnnounceBegin(fileName, chunkCount, LocalId);

        var flowPoll = new WaitForSeconds(Constants.ClipTransfer.FlowPollInterval);
        var sendPace = new WaitForSeconds(1f / Mathf.Max(1, Constants.ClipTransfer.SendRate));
        bool stallReported = false;
        for (int i = 0; i < chunkCount; i++)
        {
            if (GetSendFloor(fileName) == int.MaxValue)
            {
                Plugin.Log.LogDebug($"Nobody is left to receive '{fileName}', stopping at chunk {i}.");
                break;
            }

            float blockedStart = Time.realtimeSinceStartup;
            while (i - GetSendFloor(fileName) >= FlowWindowChunks)
            {
                if (Time.realtimeSinceStartup - blockedStart > FlowStallTimeout)
                {
                    if (!stallReported)
                    {
                        stallReported = true;
                        Plugin.Log.LogWarning($"Sending '{fileName}' has stalled at chunk {i} of {chunkCount}. A player is not keeping up.");
                    }
                    break;
                }
                yield return flowPoll;
            }

            SendOneChunk(fileName, i, bytes);
            yield return sendPace;
        }

        AnnounceEnd(fileName, LocalId);
        StartCoroutine(EvictSenderCacheCoroutine(fileName));
    }

    private int GetSendFloor(string fileName) =>
        _sendFloor.TryGetValue(fileName, out int floor) ? floor : 0;

    private void SendOneChunk(string fileName, int chunkId, byte[] bytes)
    {
        int offset = chunkId * ChunkSize;
        if (offset < 0 || offset >= bytes.Length) return;

        int len = Mathf.Min(ChunkSize, bytes.Length - offset);
        var chunk = new byte[len];
        Buffer.BlockCopy(bytes, offset, chunk, 0, len);

        int size = FastBufferWriter.GetWriteSize(fileName) + sizeof(int) + sizeof(int) + len;
        using var writer = new FastBufferWriter(size, Allocator.Temp);
        writer.WriteValueSafe(fileName);
        writer.WriteValueSafe(chunkId);
        writer.WriteValueSafe(len);
        writer.WriteBytesSafe(chunk, len);
        Relay(MsgChunk, writer, ApplyChunk, fileName, chunkId, chunk);
    }

    private IEnumerator EvictSenderCacheCoroutine(string fileName)
    {
        yield return new WaitForSeconds(SenderCacheTime);
        _outgoingCache.Remove(fileName);

        for (int i = 0; i < Constants.ClipTransfer.MaxEvictWaits && IsServer && _recipients.ContainsKey(fileName); i++)
        {
            yield return new WaitForSeconds(SenderCacheTime);
        }

        CleanupFlowState(fileName);
        _senderByFile.Remove(fileName);
    }

    private void CleanupFlowState(string fileName)
    {
        _sendFloor.Remove(fileName);
        _ackByReceiver.Remove(fileName);
        _lastFloorSent.Remove(fileName);
        _beginTime.Remove(fileName);
        _recipients.Remove(fileName);
    }

    private static void SendToServer(string message, FastBufferWriter writer, NetworkDelivery delivery)
    {
        CustomMessagingManager? cmm = Cmm;
        if (cmm == null || Net == null) return;
        cmm.SendNamedMessage(message, NetworkManager.ServerClientId, writer, delivery);
    }

    private static void SendToEveryClient(string message, FastBufferWriter writer, NetworkDelivery delivery)
    {
        CustomMessagingManager? cmm = Cmm;
        if (cmm == null || Net == null) return;

        foreach (ulong id in Net.ConnectedClientsIds)
        {
            if (id == Net.LocalClientId) continue;
            cmm.SendNamedMessage(message, id, writer, delivery);
        }
    }

    private void Relay(string message, FastBufferWriter writer, Action<ulong, string, int, byte[]> localApply, string fileName, int chunkId, byte[] data)
    {
        if (IsServer)
        {
            SendToEveryClient(message, writer, NetworkDelivery.ReliableFragmentedSequenced);
            localApply(LocalId, fileName, chunkId, data);
        }
        else
        {
            SendToServer(message, writer, NetworkDelivery.ReliableFragmentedSequenced);
        }
    }

    private void AnnounceBegin(string fileName, int chunkCount, ulong sender)
    {
        int size = FastBufferWriter.GetWriteSize(fileName) + sizeof(int) + sizeof(ulong);
        using var writer = new FastBufferWriter(size, Allocator.Temp);
        writer.WriteValueSafe(fileName);
        writer.WriteValueSafe(chunkCount);
        writer.WriteValueSafe(sender);

        if (IsServer)
        {
            SendToEveryClient(MsgBegin, writer, NetworkDelivery.ReliableFragmentedSequenced);
            ApplyBegin(fileName, chunkCount, sender);
        }
        else
        {
            SendToServer(MsgBegin, writer, NetworkDelivery.ReliableFragmentedSequenced);
        }
    }

    private void AnnounceEnd(string fileName, ulong sender)
    {
        int size = FastBufferWriter.GetWriteSize(fileName) + sizeof(ulong);
        using var writer = new FastBufferWriter(size, Allocator.Temp);
        writer.WriteValueSafe(fileName);
        writer.WriteValueSafe(sender);

        if (IsServer)
        {
            SendToEveryClient(MsgEnd, writer, NetworkDelivery.ReliableFragmentedSequenced);
            ApplyEnd(fileName, sender);
        }
        else
        {
            SendToServer(MsgEnd, writer, NetworkDelivery.ReliableFragmentedSequenced);
        }
    }

    private void OnBegin(ulong from, FastBufferReader reader)
    {
        reader.ReadValueSafe(out string fileName);
        reader.ReadValueSafe(out int chunkCount);
        reader.ReadValueSafe(out ulong sender);

        if (!ClipName.IsSafe(fileName))
        {
            Plugin.Log.LogWarning($"Refused an unsafe clip name from client {from}.");
            return;
        }

        if (chunkCount <= 0 || chunkCount > Constants.ClipTransfer.MaxChunks) return;

        if (IsServer && from != NetworkManager.ServerClientId)
        {
            if (!IsOwner(fileName, from)) return;
            sender = from;
            _senderByFile[fileName] = sender;
            CleanupFlowState(fileName);
            SetRecipients(fileName, sender);
            int size = FastBufferWriter.GetWriteSize(fileName) + sizeof(int) + sizeof(ulong);
            using var writer = new FastBufferWriter(size, Allocator.Temp);
            writer.WriteValueSafe(fileName);
            writer.WriteValueSafe(chunkCount);
            writer.WriteValueSafe(sender);
            SendToEveryClient(MsgBegin, writer, NetworkDelivery.ReliableFragmentedSequenced);
        }
        ApplyBegin(fileName, chunkCount, sender);
    }

    private void OnChunk(ulong from, FastBufferReader reader)
    {
        reader.ReadValueSafe(out string fileName);
        reader.ReadValueSafe(out int chunkId);
        reader.ReadValueSafe(out int length);
        if (length < 0 || length > ChunkSize) return;
        var data = new byte[length];
        reader.ReadBytesSafe(ref data, length);

        if (IsServer && from != NetworkManager.ServerClientId)
        {
            if (!IsOwner(fileName, from)) return;
            int size = FastBufferWriter.GetWriteSize(fileName) + sizeof(int) + sizeof(int) + length;
            using var writer = new FastBufferWriter(size, Allocator.Temp);
            writer.WriteValueSafe(fileName);
            writer.WriteValueSafe(chunkId);
            writer.WriteValueSafe(length);
            writer.WriteBytesSafe(data, length);
            SendToEveryClient(MsgChunk, writer, NetworkDelivery.ReliableFragmentedSequenced);
        }
        ApplyChunk(from, fileName, chunkId, data);
    }

    private void OnEnd(ulong from, FastBufferReader reader)
    {
        reader.ReadValueSafe(out string fileName);
        reader.ReadValueSafe(out ulong sender);

        if (IsServer && from != NetworkManager.ServerClientId)
        {
            if (!IsOwner(fileName, from)) return;
            sender = from;
            _senderByFile[fileName] = sender;
            int size = FastBufferWriter.GetWriteSize(fileName) + sizeof(ulong);
            using var writer = new FastBufferWriter(size, Allocator.Temp);
            writer.WriteValueSafe(fileName);
            writer.WriteValueSafe(sender);
            SendToEveryClient(MsgEnd, writer, NetworkDelivery.ReliableFragmentedSequenced);
            StartCoroutine(EvictSenderCacheCoroutine(fileName));
        }
        ApplyEnd(fileName, sender);
    }

    private bool IsOwner(string fileName, ulong clientId)
    {
        if (_senderByFile.TryGetValue(fileName, out ulong owner)) return owner == clientId;
        return _senderByFile.Count < Constants.ClipTransfer.MaxActiveClips;
    }

    private void ApplyBegin(string fileName, int chunkCount, ulong senderClientId)
    {
        if (LocalId == senderClientId) return;
        if (_incoming.ContainsKey(fileName)) return;
        if (_incoming.Count >= Constants.ClipTransfer.MaxActiveClips)
        {
            Plugin.Log.LogDebug($"Ignored '{fileName}', already receiving {_incoming.Count} clips.");
            return;
        }
        if (chunkCount <= 0 || chunkCount > Constants.ClipTransfer.MaxChunks)
        {
            Plugin.Log.LogWarning($"Refused '{fileName}', {chunkCount} chunks is out of range.");
            AnnounceSkipped(fileName);
            return;
        }

        if (!ClipName.IsSafe(fileName))
        {
            AnnounceSkipped(fileName);
            return;
        }

        try
        {
            string receivedDir = Path.Combine(Recorder.GetOutputDirectory(), "Received");
            Directory.CreateDirectory(receivedDir);

            string finalPath = Path.Combine(receivedDir, $"client{senderClientId}_{fileName}");
            string partPath = finalPath + ".part";
            var stream = new FileStream(partPath, FileMode.Create, FileAccess.Write, FileShare.None);

            _senderByFile[fileName] = senderClientId;
            _incoming[fileName] = new IncomingClip
            {
                ExpectedChunks = chunkCount,
                SenderClientId = senderClientId,
                Stream = stream,
                PartPath = partPath,
                FinalPath = finalPath,
                LastActivity = Time.realtimeSinceStartup,
            };
            Plugin.Log.LogDebug($"Incoming clip '{fileName}' from client {senderClientId}, {chunkCount} chunks.");

            if (_monitoring.Add(fileName))
            {
                StartCoroutine(MonitorIncomingCoroutine(fileName));
            }
        }
        catch (Exception ex)
        {
            if (!DiskSpace.ReportIfOutOfSpace(ex, $"'{fileName}'"))
            {
                Plugin.Log.LogError($"Failed to open receive file for '{fileName}': {ex.Message}");
            }
            AnnounceSkipped(fileName);
        }
    }

    private void ApplyChunk(ulong _, string fileName, int chunkId, byte[] data)
    {
        if (!_incoming.TryGetValue(fileName, out IncomingClip? clip) || clip?.Stream == null)
        {
            return;
        }

        if (chunkId < 0 || chunkId >= clip.ExpectedChunks) return;

        try
        {
            clip.Stream.Seek((long)chunkId * ChunkSize, SeekOrigin.Begin);
            clip.Stream.Write(data, 0, data.Length);
            clip.ReceivedIds.Add(chunkId);
            clip.LastActivity = Time.realtimeSinceStartup;

            if (clip.ReceivedIds.Count >= clip.ExpectedChunks)
            {
                MaybeSendAck(fileName, clip, force: true);
                FinalizeIncoming(fileName, clip);
                return;
            }

            MaybeSendAck(fileName, clip, force: false);
        }
        catch (Exception ex)
        {
            if (!DiskSpace.ReportIfOutOfSpace(ex, $"'{fileName}'"))
            {
                Plugin.Log.LogError($"Failed to write chunk {chunkId} of '{fileName}': {ex.Message}");
            }
            CleanupIncoming(fileName, clip);
        }
    }

    private void ApplyEnd(string fileName, ulong senderClientId)
    {
        if (LocalId == senderClientId) return;
        if (!_incoming.TryGetValue(fileName, out IncomingClip? clip) || clip == null) return;

        clip.EndSignaled = true;

        if (clip.ReceivedIds.Count >= clip.ExpectedChunks)
        {
            FinalizeIncoming(fileName, clip);
        }
    }

    private void MaybeSendAck(string fileName, IncomingClip clip, bool force)
    {
        int count = clip.ReceivedIds.Count;
        if (!force && count - clip.LastAckedCount < AckIntervalChunks)
        {
            return;
        }
        clip.LastAckedCount = count;

        if (IsServer)
        {
            ApplyAck(fileName, LocalId, count);
            return;
        }

        int size = FastBufferWriter.GetWriteSize(fileName) + sizeof(int);
        using var writer = new FastBufferWriter(size, Allocator.Temp);
        writer.WriteValueSafe(fileName);
        writer.WriteValueSafe(count);
        SendToServer(MsgAck, writer, NetworkDelivery.Reliable);
    }

    private void OnAck(ulong from, FastBufferReader reader)
    {
        reader.ReadValueSafe(out string fileName);
        reader.ReadValueSafe(out int count);
        if (!_senderByFile.ContainsKey(fileName)) return;
        ApplyAck(fileName, from, count);
    }

    private void ApplyAck(string fileName, ulong receiverId, int count)
    {
        if (!IsServer) return;

        if (!_ackByReceiver.TryGetValue(fileName, out Dictionary<ulong, int>? map) || map == null)
        {
            map = new Dictionary<ulong, int>();
            _ackByReceiver[fileName] = map;
        }
        if (!map.TryGetValue(receiverId, out int prev) || count > prev)
        {
            map[receiverId] = count;
        }

        RefreshFloor(fileName);
    }

    private void AnnounceDone(string fileName, IncomingClip clip) =>
        AnnounceSkipped(fileName);

    private void AnnounceSkipped(string fileName)
    {
        if (IsServer)
        {
            DropRecipient(fileName, LocalId);
            return;
        }

        int size = FastBufferWriter.GetWriteSize(fileName) + sizeof(int);
        using var writer = new FastBufferWriter(size, Allocator.Temp);
        writer.WriteValueSafe(fileName);
        writer.WriteValueSafe(Constants.ClipTransfer.MaxChunks);
        SendToServer(MsgAck, writer, NetworkDelivery.Reliable);
    }

    private void DropRecipient(string fileName, ulong clientId)
    {
        if (!IsServer) return;
        if (!_recipients.TryGetValue(fileName, out HashSet<ulong>? listeners) || listeners == null) return;
        if (!listeners.Remove(clientId)) return;
        RefreshFloor(fileName);
    }

    private void RefreshFloor(string fileName)
    {
        if (!IsServer) return;

        int floor = GetAckFloor(fileName);
        ulong sender = _senderByFile.TryGetValue(fileName, out ulong s) ? s : ulong.MaxValue;

        if (sender == LocalId)
        {
            _sendFloor[fileName] = floor;
        }
        else if (sender != ulong.MaxValue && Net != null && Net.ConnectedClientsIds.Contains(sender))
        {
            if (!_lastFloorSent.TryGetValue(fileName, out int lastF) || lastF != floor)
            {
                _lastFloorSent[fileName] = floor;
                int size = FastBufferWriter.GetWriteSize(fileName) + sizeof(int);
                using var writer = new FastBufferWriter(size, Allocator.Temp);
                writer.WriteValueSafe(fileName);
                writer.WriteValueSafe(floor);
                Cmm?.SendNamedMessage(MsgFloor, sender, writer, NetworkDelivery.Reliable);
            }
        }
    }

    private int GetAckFloor(string fileName)
    {
        if (Net == null) return 0;
        _ackByReceiver.TryGetValue(fileName, out Dictionary<ulong, int>? map);

        ulong sender = _senderByFile.TryGetValue(fileName, out ulong s) ? s : ulong.MaxValue;
        _recipients.TryGetValue(fileName, out HashSet<ulong>? expected);

        bool late = _beginTime.TryGetValue(fileName, out float began)
                    && Time.realtimeSinceStartup - began > FlowStallTimeout;

        int floor = int.MaxValue;
        bool any = false;
        foreach (ulong id in Net.ConnectedClientsIds)
        {
            if (id == sender) continue;
            if (expected != null && !expected.Contains(id)) continue;

            if (map == null || !map.TryGetValue(id, out int c))
            {
                if (late) continue;
                c = 0;
            }

            any = true;
            if (c < floor) floor = c;
        }

        return any ? floor : int.MaxValue;
    }

    private void OnFloor(ulong from, FastBufferReader reader)
    {
        reader.ReadValueSafe(out string fileName);
        reader.ReadValueSafe(out int floor);
        if (from != NetworkManager.ServerClientId) return;
        if (!_outgoingCache.ContainsKey(fileName)) return;
        _sendFloor[fileName] = floor;
    }

    private void Update()
    {
        if (!IsServer || _recipients.Count == 0) return;
        if (Time.realtimeSinceStartup < _nextFloorPoll) return;

        _nextFloorPoll = Time.realtimeSinceStartup + Constants.ClipTransfer.FlowPollInterval;
        try
        {
            foreach (string fileName in _recipients.Keys)
            {
                RefreshFloor(fileName);
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"Could not refresh the clip send floor: {ex.Message}");
            _nextFloorPoll = Time.realtimeSinceStartup + Constants.ClipTransfer.FlowStallTimeout;
        }
    }

    private IEnumerator MonitorIncomingCoroutine(string fileName)
    {
        WaitForSeconds wait = new(RetryDelay);
        float startTime = Time.realtimeSinceStartup;
        int boundedAttempts = 0;
        int lastCount = -1;

        try
        {
            while (true)
            {
                yield return wait;

                if (!_incoming.TryGetValue(fileName, out IncomingClip? clip) || clip == null)
                {
                    yield break;
                }

                if (clip.ReceivedIds.Count >= clip.ExpectedChunks)
                {
                    FinalizeIncoming(fileName, clip);
                    yield break;
                }

                if (IsServer && Net != null && clip.SenderClientId != LocalId
                    && !Net.ConnectedClientsIds.Contains(clip.SenderClientId))
                {
                    Plugin.Log.LogWarning($"Client {clip.SenderClientId} left, aborting '{fileName}' at {clip.ReceivedIds.Count} of {clip.ExpectedChunks} chunks.");
                    BroadcastAbort(fileName);
                    yield break;
                }

                // deadline from start killed healthy big clips so give up on silence instead.
                // MinRecoveryRate sits under SendRate because WaitForSeconds rounds up to frame
                float paceBudget = clip.ExpectedChunks / Constants.ClipTransfer.MinRecoveryRate;
                bool starved = Time.realtimeSinceStartup - clip.LastActivity > RecoveryTimeout;
                bool crawling = Time.realtimeSinceStartup - startTime > RecoveryTimeout + paceBudget;
                if (starved || crawling)
                {
                    float waited = Time.realtimeSinceStartup - startTime;
                    string why = starved ? "no chunk arrived" : "too slow to finish";
                    Plugin.Log.LogError($"Gave up on clip '{fileName}' after {waited:F0}s, {why} ({clip.ReceivedIds.Count} of {clip.ExpectedChunks} chunks).");
                    CleanupIncoming(fileName, clip);
                    yield break;
                }

                MaybeSendAck(fileName, clip, force: true);

                bool idle = Time.realtimeSinceStartup - clip.LastActivity >= RetryDelay;
                if (!idle)
                {
                    continue;
                }

                if (clip.EndSignaled)
                {
                    if (clip.ReceivedIds.Count > lastCount)
                    {
                        lastCount = clip.ReceivedIds.Count;
                        boundedAttempts = 0;
                    }

                    boundedAttempts++;
                    if (boundedAttempts > MaxRetries)
                    {
                        Plugin.Log.LogError($"Clip '{fileName}' failed after {MaxRetries} recovery attempts, {clip.ReceivedIds.Count} of {clip.ExpectedChunks} chunks arrived.");
                        CleanupIncoming(fileName, clip);
                        yield break;
                    }
                }

                var missing = new List<int>();
                for (int i = 0; i < clip.ExpectedChunks && missing.Count < MaxResendChunks; i++)
                {
                    if (!clip.ReceivedIds.Contains(i)) missing.Add(i);
                }

                if (missing.Count == 0)
                {
                    FinalizeIncoming(fileName, clip);
                    yield break;
                }

                string phase = clip.EndSignaled ? $"attempt {boundedAttempts}/{MaxRetries}" : "stalled";
                Plugin.Log.LogDebug($"Clip '{fileName}' at {clip.ReceivedIds.Count} of {clip.ExpectedChunks} chunks, requesting {missing.Count} again ({phase}).");
                RequestMissingChunks(fileName, missing.ToArray());
            }
        }
        finally
        {
            _monitoring.Remove(fileName);
        }
    }

    private void BroadcastAbort(string fileName)
    {
        int size = FastBufferWriter.GetWriteSize(fileName);
        using var writer = new FastBufferWriter(size, Allocator.Temp);
        writer.WriteValueSafe(fileName);
        SendToEveryClient(MsgAbort, writer, NetworkDelivery.Reliable);
        ApplyAbort(fileName);
    }

    private void OnAbort(ulong from, FastBufferReader reader)
    {
        reader.ReadValueSafe(out string fileName);
        bool owner = _senderByFile.TryGetValue(fileName, out ulong sender) && sender == from;
        if (from != NetworkManager.ServerClientId && !owner) return;
        ApplyAbort(fileName);
    }

    private void ApplyAbort(string fileName)
    {
        if (_incoming.TryGetValue(fileName, out IncomingClip? clip) && clip != null)
        {
            Plugin.Log.LogWarning($"Clip '{fileName}' was aborted because sender left.");
            CleanupIncoming(fileName, clip);
        }
    }

    private void RequestMissingChunks(string fileName, int[] missingIds)
    {
        if (missingIds == null || missingIds.Length == 0) return;

        if (IsServer)
        {
            ServerRouteResend(fileName, missingIds);
            return;
        }

        int size = FastBufferWriter.GetWriteSize(fileName) + sizeof(int) + missingIds.Length * sizeof(int);
        using var writer = new FastBufferWriter(size, Allocator.Temp);
        writer.WriteValueSafe(fileName);
        writer.WriteValueSafe(missingIds.Length);
        foreach (int id in missingIds) writer.WriteValueSafe(id);
        SendToServer(MsgResend, writer, NetworkDelivery.ReliableFragmentedSequenced);
    }

    private void OnResendRequest(ulong from, FastBufferReader reader)
    {
        int[] missingIds = ReadIntArray(reader, out string fileName);
        ServerRouteResend(fileName, missingIds);
    }

    private void ServerRouteResend(string fileName, int[] missingIds)
    {
        if (!IsServer || missingIds.Length == 0) return;

        if (!_senderByFile.TryGetValue(fileName, out ulong senderId))
        {
            Plugin.Log.LogDebug($"Resend requested for '{fileName}' but no sender is known.");
            return;
        }

        if (senderId == LocalId)
        {
            ResendChunks(fileName, missingIds);
            return;
        }

        int size = FastBufferWriter.GetWriteSize(fileName) + sizeof(int) + missingIds.Length * sizeof(int);
        using var writer = new FastBufferWriter(size, Allocator.Temp);
        writer.WriteValueSafe(fileName);
        writer.WriteValueSafe(missingIds.Length);
        foreach (int id in missingIds) writer.WriteValueSafe(id);
        Cmm?.SendNamedMessage(MsgForward, senderId, writer, NetworkDelivery.ReliableFragmentedSequenced);
    }

    private void OnForward(ulong from, FastBufferReader reader)
    {
        int[] missingIds = ReadIntArray(reader, out string fileName);
        ResendChunks(fileName, missingIds);
    }

    private static int[] ReadIntArray(FastBufferReader reader, out string fileName)
    {
        reader.ReadValueSafe(out fileName);
        reader.ReadValueSafe(out int count);
        if (count < 0 || count > Constants.ClipTransfer.MaxResendChunks)
        {
            return Array.Empty<int>();
        }
        var ids = new int[count];
        for (int i = 0; i < count; i++)
        {
            reader.ReadValueSafe(out ids[i]);
        }
        return ids;
    }

    private void ResendChunks(string fileName, int[] missingIds)
    {
        if (!_outgoingCache.ContainsKey(fileName))
        {
            Plugin.Log.LogDebug($"Cannot resend '{fileName}', its bytes are no longer cached.");
            return;
        }

        StartCoroutine(ResendChunksCoroutine(fileName, missingIds));
    }

    private IEnumerator ResendChunksCoroutine(string fileName, int[] missingIds)
    {
        foreach (int id in missingIds)
        {
            if (!_outgoingCache.TryGetValue(fileName, out byte[]? bytes) || bytes == null)
            {
                yield break;
            }
            SendOneChunk(fileName, id, bytes);
            yield return null;
        }
    }

    private void FinalizeIncoming(string fileName, IncomingClip clip)
    {
        _incoming.Remove(fileName);
        AnnounceDone(fileName, clip);
        StartCoroutine(EvictSenderCacheCoroutine(fileName));
        try
        {
            clip.Stream?.Flush();
            clip.Stream?.Dispose();
            clip.Stream = null;

            if (File.Exists(clip.FinalPath))
            {
                File.Delete(clip.FinalPath);
            }
            File.Move(clip.PartPath, clip.FinalPath);

            long bytes = new FileInfo(clip.FinalPath).Length;
            Plugin.Log.LogInfo($"Received clip from client {clip.SenderClientId}: '{Path.GetFileName(clip.FinalPath)}' ({bytes / 1024} KB).");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"Failed to finalize received clip '{fileName}': {ex.Message}");
        }
    }

    private void CleanupIncoming(string fileName, IncomingClip clip)
    {
        _incoming.Remove(fileName);
        AnnounceDone(fileName, clip);
        StartCoroutine(EvictSenderCacheCoroutine(fileName));
        try
        {
            clip.Stream?.Dispose();
            clip.Stream = null;
            if (File.Exists(clip.PartPath)) File.Delete(clip.PartPath);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogDebug($"Could not clean up partial file for '{fileName}': {ex.Message}");
        }
    }

    private void OnDestroy()
    {
        foreach (IncomingClip clip in _incoming.Values)
        {
            try { clip.Stream?.Dispose(); } catch { }
        }
        _incoming.Clear();
        if (Instance == this) Instance = null;
    }
}
