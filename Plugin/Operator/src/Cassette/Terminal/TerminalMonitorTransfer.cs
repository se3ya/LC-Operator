using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Operator.Cassette;

internal static class TerminalMonitorTransfer
{
    private const string MsgPlay = "Op_Mon_Play";
    private const string MsgReqPlay = "Op_Mon_ReqPlay";
    private const string MsgStop = "Op_Mon_Stop";
    private const string MsgReqStop = "Op_Mon_ReqStop";

    private static bool _registered;
    private static bool _finishedHooked;

    private static string _pendingPruneFile = string.Empty;

    internal static bool Enabled =>
        ShipMonitors.Available
        && NetworkManager.Singleton != null
        && NetworkManager.Singleton.IsListening;

    internal static bool IsActive => MonitorPlayback.IsActive;

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
        if (nm.IsServer)
        {
            cmm.RegisterNamedMessageHandler(MsgReqPlay, OnRequestPlay);
            cmm.RegisterNamedMessageHandler(MsgReqStop, OnRequestStop);
        }

        if (!_finishedHooked)
        {
            MonitorPlayback.LocalPlaybackFinished += OnLocalPlaybackFinished;
            MonitorPlayback.LocalPlaybackFailed += OnLocalPlaybackFailed;
            _finishedHooked = true;
        }

        _registered = true;
    }

    internal static void Unregister()
    {
        CustomMessagingManager? cmm = Cmm;
        if (cmm != null)
        {
            foreach (string name in new[] { MsgPlay, MsgStop, MsgReqPlay, MsgReqStop })
            {
                try { cmm.UnregisterNamedMessageHandler(name); } catch { }
            }
        }

        if (_finishedHooked)
        {
            MonitorPlayback.LocalPlaybackFinished -= OnLocalPlaybackFinished;
            MonitorPlayback.LocalPlaybackFailed -= OnLocalPlaybackFailed;
            _finishedHooked = false;
        }

        ForceLocalStop();
        _registered = false;
    }

    internal static void RequestPlay(string fileName, int monitorIndex)
    {
        if (Cmm == null || string.IsNullOrEmpty(fileName)) return;
        if (IsServer)
        {
            ServerBeginTransfer(fileName, monitorIndex);
        }
        else
        {
            SendPlayToServer(fileName, monitorIndex);
        }
    }

    internal static void RequestStop()
    {
        if (Cmm == null) return;
        if (IsServer)
        {
            ApplyStopLocally();
            SendByteToAll(MsgStop, 0);
        }
        else
        {
            SendByteToServer(MsgReqStop, 0);
        }
    }

    private static void ServerBeginTransfer(string fileName, int monitorIndex)
    {
        if (!IsServer || Cmm == null) return;

        fileName = CassetteLibrary.GetMergedName(fileName);
        if (CassetteLibrary.IsMergePending(fileName) || CassetteItem.IsEncodingFile(fileName))
        {
            Plugin.Log.LogDebug($"'{fileName}' is not ready yet, so it stays on the terminal.");
            return;
        }
        Plugin.Log.LogDebug($"Moving '{fileName}' to monitor {monitorIndex} for lobby.");
        ApplyPlayLocally(fileName, monitorIndex);
        SendPlayToAll(fileName, monitorIndex);
    }

    private static void OnLocalPlaybackFailed()
    {
        _pendingPruneFile = string.Empty;
        if (IsServer && IsActive)
        {
            RequestStop();
        }
    }

    private static void OnLocalPlaybackFinished()
    {
        if (IsServer && IsActive)
        {
            RequestStop();
        }
    }

    internal static void ForceLocalStop()
    {
        if (MonitorPlayback.IsActive || !string.IsNullOrEmpty(_pendingPruneFile))
        {
            ApplyStopLocally();
        }
    }

    private static void ApplyPlayLocally(string fileName, int monitorIndex)
    {
        if (!ShipMonitors.Available || string.IsNullOrEmpty(fileName)) return;
        if (string.IsNullOrEmpty(TerminalCassettePlayback.FindLocalPath(fileName)))
        {
            Plugin.Log.LogWarning($"'{fileName}' is not ready here so terminal keeps playing it.");
            return;
        }

        string justPlayed = TerminalCassettePlayback.StopForHandoff();
        if (!MonitorPlayback.Play(fileName, monitorIndex))
        {
            if (!string.IsNullOrEmpty(justPlayed)) TerminalCassettePlayback.ApplyPlayLocally(justPlayed);
            return;
        }

        if (!string.IsNullOrEmpty(justPlayed)) _pendingPruneFile = justPlayed;
        TerminalCassettePlayback.SetupHandlerForMonitor();
    }

    private static void ApplyStopLocally()
    {
        MonitorPlayback.Stop();

        string prune = _pendingPruneFile;
        _pendingPruneFile = string.Empty;
        if (!string.IsNullOrEmpty(prune))
        {
            CassetteLibrary.PruneReceivedClip(prune);
        }
        VideoCameraItem.RetrySegmentCleanup();
    }

    private static void OnRequestPlay(ulong sender, FastBufferReader reader)
    {
        reader.ReadValueSafe(out string fileName);
        reader.ReadValueSafe(out int monitorIndex);
        ServerBeginTransfer(fileName, monitorIndex);
    }

    private static void OnRequestStop(ulong sender, FastBufferReader reader)
    {
        ApplyStopLocally();
        SendByteToAll(MsgStop, 0);
    }

    private static void OnPlay(ulong sender, FastBufferReader reader)
    {
        if (IsServer) return;
        reader.ReadValueSafe(out string fileName);
        reader.ReadValueSafe(out int monitorIndex);
        ApplyPlayLocally(fileName, monitorIndex);
    }

    private static void OnStop(ulong sender, FastBufferReader reader)
    {
        if (IsServer) return;
        ApplyStopLocally();
    }

    private static void SendPlayToAll(string fileName, int monitorIndex)
    {
        CustomMessagingManager? cmm = Cmm;
        if (cmm == null) return;
        using var writer = new FastBufferWriter(FastBufferWriter.GetWriteSize(fileName) + sizeof(int), Allocator.Temp);
        writer.WriteValueSafe(fileName);
        writer.WriteValueSafe(monitorIndex);
        cmm.SendNamedMessageToAll(MsgPlay, writer);
    }

    private static void SendPlayToServer(string fileName, int monitorIndex)
    {
        CustomMessagingManager? cmm = Cmm;
        if (cmm == null) return;
        using var writer = new FastBufferWriter(FastBufferWriter.GetWriteSize(fileName) + sizeof(int), Allocator.Temp);
        writer.WriteValueSafe(fileName);
        writer.WriteValueSafe(monitorIndex);
        cmm.SendNamedMessage(MsgReqPlay, NetworkManager.ServerClientId, writer);
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
}
