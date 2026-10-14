using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unity.Netcode;
using UnityEngine;
using Operator.Cassette;
using Operator.Recording;

namespace Operator;

internal sealed partial class VideoCameraItem
{
    private readonly List<ChainSegment> _chainSegments = new();
    private bool _chainOpen;
    private float _chainLastActivityTime;
    private bool _chainClosePending;
    private float _chainCloseDeadline;
    private float _chainCloseHardDeadline;
    private readonly List<ChainSegment> _chainHeld = new();
    private float _chainHeldUntil;
    private readonly List<ChainSegment> _serverStored = new();
    private string _serverStoredAwait = string.Empty;
    private ulong _serverStoredAwaitOwner;

    private static readonly Dictionary<System.Threading.Tasks.Task, VideoCameraItem> _mergeJobs = new();

    internal static bool IsMerging
    {
        get
        {
            foreach (KeyValuePair<System.Threading.Tasks.Task, VideoCameraItem> job in _mergeJobs)
            {
                if (job.Value != null && !job.Key.IsCompleted) return true;
            }
            _mergeJobs.Clear();
            return false;
        }
    }

    private static readonly Dictionary<IEnumerator, VideoCameraItem> _stitching = new();
    private static readonly HashSet<string> _skippedSegments = new(StringComparer.OrdinalIgnoreCase);

    internal static bool IsStitching
    {
        get
        {
            foreach (KeyValuePair<IEnumerator, VideoCameraItem> job in _stitching)
            {
                if (job.Value != null) return true;
            }
            _stitching.Clear();
            return false;
        }
    }

    private const float StitchSilenceTimeout = 120f;
    private const float MaxSegmentAge = 86400f;
    private const float StitchPollInterval = 1f;
    private const float ChainIdleTimeout = 600f;
    private const float ChainCloseGrace = 15f;
    private const float ChainCloseTimeout = 30f;
    private const float ChainReopenGrace = 300f;
    private const float HandoffWindow = 5f;

    private readonly struct ChainSegment
    {
        public readonly ulong OwnerClientId;
        public readonly string FileName;
        public readonly bool EndedByHandoff;
        public readonly float Duration;
        public readonly int Value;
        public readonly string? Breakdown;
        public readonly float StartEstimate;

        public ChainSegment(ulong ownerClientId, string fileName, bool endedByHandoff, float duration, int value, string? breakdown, float startEstimate)
        {
            OwnerClientId = ownerClientId;
            FileName = fileName;
            EndedByHandoff = endedByHandoff;
            Duration = duration;
            Value = value;
            Breakdown = breakdown;
            StartEstimate = startEstimate;
        }
    }

    private void ReportRecordingSegment(string fileName, bool endedByHandoff, float duration, string breakdown, float segmentStartRealtime)
    {
        if (string.IsNullOrEmpty(fileName))
        {
            return;
        }

        float age = Mathf.Clamp(Time.realtimeSinceStartup - segmentStartRealtime, 0f, MaxSegmentAge);
        ulong ownerId = NetworkManager.Singleton.LocalClientId;
        if (IsServer)
        {
            ServerAddSegment(ownerId, fileName, endedByHandoff, duration, breakdown, age);
        }
        else
        {
            ReportRecordingSegmentServerRpc(ownerId, fileName, endedByHandoff, duration, breakdown, age);
        }
    }

    private void ReportRecordingFailure(bool endedByHandoff)
    {
        ulong ownerId = NetworkManager.Singleton.LocalClientId;
        if (IsServer)
        {
            ServerSkipSegment(ownerId, endedByHandoff);
        }
        else
        {
            ReportRecordingFailureServerRpc(ownerId, endedByHandoff);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void ReportRecordingFailureServerRpc(ulong ownerClientId, bool endedByHandoff, ServerRpcParams rpcParams = default)
    {
        ServerSkipSegment(rpcParams.Receive.SenderClientId, endedByHandoff);
    }

    private void ServerSkipSegment(ulong ownerClientId, bool endedByHandoff)
    {
        if (!IsServer) return;
        ServerCloseTake();
        if (_serverStored.Count > 0 && !HasStoredTape() && _serverStoredAwait.Length == 0) _serverStored.Clear();

        if (!_chainOpen)
        {
            Plugin.Log.LogDebug($"Cassette from client {ownerClientId} failed with no chain waiting on it.");
            if (!StillFilming()) ServerResetTapeClock();
            return;
        }

        if (endedByHandoff)
        {
            _chainLastActivityTime = Time.realtimeSinceStartup;
            Plugin.Log.LogWarning($"Cassette from client {ownerClientId} failed mid chain so merged film will have a gap.");
            return;
        }

        Plugin.Log.LogWarning($"Closing cassette from client {ownerClientId} failed so chain is closing without it.");
        ServerCloseChain("the closing cassette failed to write");
    }

    [ServerRpc(RequireOwnership = false)]
    private void ReportRecordingSegmentServerRpc(ulong ownerClientId, string fileName, bool endedByHandoff, float duration, string breakdown, float ageSeconds)
    {
        ServerAddSegment(ownerClientId, fileName, endedByHandoff, duration, breakdown, ageSeconds);
    }

    private void ServerAddSegment(ulong ownerClientId, string fileName, bool endedByHandoff, float duration, string breakdown, float ageSeconds)
    {
        if (!IsServer)
        {
            return;
        }
        ServerCloseTake();

        if (!Recording.ClipName.IsSafe(fileName))
        {
            Plugin.Log.LogWarning($"Refused segment file name from client {ownerClientId}.");
            return;
        }

        List<Operator.Cassette.EnemyBountyTracker.EnemyRow> rows = Operator.Cassette.EnemyBountyTracker.SetDanger(
            Operator.Cassette.EnemyBountyTracker.Parse(breakdown ?? string.Empty),
            Operator.Cassette.EnemyBountyTracker.GetDanger());
        int value = _serverSessionWorthless ? 0 : Operator.Cassette.EnemyBountyTracker.GetValue(rows);
        value = Operator.Cassette.CassetteLimits.ClampValue(value, ownerClientId, "Segment value");
        string? segmentBreakdown = _serverSessionWorthless
            ? null
            : Operator.Cassette.EnemyBountyTracker.Serialize(Operator.Cassette.EnemyBountyTracker.Counted(GetHeldRows(fileName) ?? rows));
        duration = Operator.Cassette.CassetteLimits.ClampDuration(duration, ownerClientId);
        ageSeconds = Mathf.Clamp(ageSeconds, 0f, MaxSegmentAge);
        float startEstimate = Time.realtimeSinceStartup - ageSeconds;

        if (_serverStored.Count > 0)
        {
            if (IsStoredFile(fileName)) return;
            if (ContinuesStored(fileName))
            {
                _serverStoredAwait = string.Empty;
                ServerOpenStored(startEstimate);
            }
            else if (_serverStoredAwait.Length == 0)
            {
                Plugin.Log.LogDebug($"Dropped {_serverStored.Count} stored tape segments, their tape left the camera.");
                _serverStored.Clear();
            }
        }

        if (!_chainOpen && ServerReopenHeldChain())
        {
            Plugin.Log.LogDebug("Continuation turned up after chain was closed, so chain is open again.");
        }

        if (!_chainOpen)
        {
            if (endedByHandoff)
            {
                _chainSegments.Clear();
                _chainSegments.Add(new ChainSegment(ownerClientId, fileName, true, duration, value, segmentBreakdown, startEstimate));
                _chainOpen = true;
                _chainLastActivityTime = Time.realtimeSinceStartup;
                Plugin.Log.LogDebug(
                    $"Chain opened with segment 0 from client {ownerClientId}, '{fileName}' ({duration:0.#}s).");
            }
            else
            {
                Plugin.Log.LogDebug(
                    $"Segment '{fileName}' from client {ownerClientId} arrived with no chain waiting so it becomes its own cassette.");
            }
            return;
        }

        _chainSegments.Add(new ChainSegment(ownerClientId, fileName, endedByHandoff, duration, value, segmentBreakdown, startEstimate));
        _chainLastActivityTime = Time.realtimeSinceStartup;
        if (endedByHandoff)
        {
            Plugin.Log.LogDebug(
                $"Chain extended to {_chainSegments.Count} segments with '{fileName}' from client {ownerClientId} ({duration:0.#}s).");
        }
        else
        {
            ServerCloseChain(null);
        }
    }

    private bool HasStoredTape()
    {
        return _serverStored.Count > 0
            && _serverHasPendingCassette
            && string.Equals(_serverPendingFileName, _serverStored[_serverStored.Count - 1].FileName, StringComparison.OrdinalIgnoreCase);
    }

    private bool IsStoredFile(string fileName)
    {
        foreach (ChainSegment part in _serverStored)
        {
            if (string.Equals(part.FileName, fileName, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private bool ContinuesStored(string fileName)
    {
        if (_serverStoredAwait.Length > 0)
        {
            return string.Equals(_serverStoredAwait, fileName, StringComparison.OrdinalIgnoreCase);
        }
        return HasStoredTape()
            || (_serverHasPendingCassette && string.Equals(_serverPendingFileName, fileName, StringComparison.OrdinalIgnoreCase));
    }

    private void ServerAwaitStored(string fileName, ulong owner)
    {
        _serverStoredAwait = fileName;
        _serverStoredAwaitOwner = owner;
    }

    private void ServerForgetStored(string reason)
    {
        if (_serverStored.Count == 0 && _serverStoredAwait.Length == 0) return;
        Plugin.Log.LogDebug($"Dropped {_serverStored.Count} stored tape segments because {reason}.");
        _serverStored.Clear();
        _serverStoredAwait = string.Empty;
    }

    private void ServerKeepChain()
    {
        if (!IsServer || !_serverHasPendingCassette || HasStoredTape()) return;
        if (!_chainOpen && EndsWithPending(_chainHeld)) ServerReopenHeldChain();
        if (!_chainOpen || _chainSegments.Count == 0) return;

        _chainSegments.Sort((a, b) => a.StartEstimate.CompareTo(b.StartEstimate));
        if (!string.Equals(_chainSegments[_chainSegments.Count - 1].FileName, _serverPendingFileName, StringComparison.OrdinalIgnoreCase)) return;

        _serverStored.Clear();
        _serverStored.AddRange(_chainSegments);
        _chainSegments.Clear();
        _chainOpen = false;
        _serverStoredAwait = string.Empty;
        Plugin.Log.LogDebug($"Kept {_serverStored.Count} chain segments for the camera to continue, last '{_serverPendingFileName}'.");
    }

    private bool EndsWithPending(List<ChainSegment> segments)
    {
        ChainSegment? latest = null;
        foreach (ChainSegment segment in segments)
        {
            if (latest == null || segment.StartEstimate >= latest.Value.StartEstimate) latest = segment;
        }
        return latest != null && string.Equals(latest.Value.FileName, _serverPendingFileName, StringComparison.OrdinalIgnoreCase);
    }

    [ServerRpc(RequireOwnership = false)]
    private void KeepChainServerRpc()
    {
        ServerKeepChain();
    }

    private void ServerStoreParts(ulong owner, string fileName)
    {
        if (_serverStoredAwait.Length > 0) ServerForgetStored("a new take was stored before the ejected one finished");
        if (!_chainOpen) ServerReopenHeldChain();
        var parts = new List<ChainSegment>(_serverStored);
        if (_chainOpen)
        {
            _chainSegments.Sort((a, b) => a.StartEstimate.CompareTo(b.StartEstimate));
            parts.AddRange(_chainSegments);
            _chainSegments.Clear();
            _chainOpen = false;
        }
        parts.Add(new ChainSegment(owner, fileName, true, _serverPendingDuration, _serverPendingValue,
            _serverSessionWorthless ? null : _serverPendingBreakdown, Time.realtimeSinceStartup));

        _serverStored.Clear();
        foreach (ChainSegment part in parts)
        {
            if (!IsStoredFile(part.FileName)) _serverStored.Add(part);
        }
        Plugin.Log.LogDebug($"Kept {_serverStored.Count} tape segments for the camera to continue, last '{fileName}'.");
    }

    private void ServerOpenStored(float before)
    {
        if (_chainOpen)
        {
            foreach (ChainSegment segment in _chainSegments) before = Mathf.Min(before, segment.StartEstimate);
        }
        else
        {
            _chainSegments.Clear();
        }

        var seeded = new List<ChainSegment>();
        for (int i = 0; i < _serverStored.Count; i++)
        {
            ChainSegment part = _serverStored[i];
            seeded.Add(new ChainSegment(part.OwnerClientId, part.FileName, true, part.Duration, part.Value, part.Breakdown, before - (_serverStored.Count - i)));
        }
        _chainSegments.InsertRange(0, seeded);
        _serverStored.Clear();
        _chainOpen = true;
        _chainLastActivityTime = Time.realtimeSinceStartup;
        Plugin.Log.LogDebug($"Continuing a stored tape, chain opened with {_chainSegments.Count} segments.");
    }

    private List<ChainSegment>? TakeStored(string fileName)
    {
        if (_serverStored.Count == 0) return null;
        if (!string.Equals(_serverStored[_serverStored.Count - 1].FileName, fileName, StringComparison.OrdinalIgnoreCase)) return null;
        var parts = new List<ChainSegment>(_serverStored);
        _serverStored.Clear();
        return parts;
    }

    private void ServerCloseStored(List<ChainSegment> parts)
    {
        if (parts.Count < 2 || _chainOpen) return;
        _chainSegments.Clear();
        float now = Time.realtimeSinceStartup;
        for (int i = 0; i < parts.Count; i++)
        {
            ChainSegment part = parts[i];
            bool closing = i == parts.Count - 1;
            _chainSegments.Add(new ChainSegment(part.OwnerClientId, part.FileName, !closing, part.Duration, part.Value, part.Breakdown, now - (parts.Count - i)));
        }
        _chainOpen = true;
        ServerCloseChain(null);
    }

    internal void ServerScheduleChainClose()
    {
        if (!IsServer) return;
        _chainClosePending = true;
        _chainCloseDeadline = Time.realtimeSinceStartup + ChainCloseGrace;
        _chainCloseHardDeadline = Time.realtimeSinceStartup + ChainCloseTimeout;
        _chainHeld.Clear();
    }

    private void UpdateChainClose()
    {
        if (!IsServer) return;

        if (_chainOpen && !StillFilming()
            && Time.realtimeSinceStartup - _chainLastActivityTime > ChainIdleTimeout)
        {
            ServerCloseChain($"nothing joined it for {ChainIdleTimeout:0}s");
        }

        if (!_chainClosePending) return;

        if (StillFilming() && Time.realtimeSinceStartup < _chainCloseHardDeadline)
        {
            _chainCloseDeadline = Mathf.Min(
                Time.realtimeSinceStartup + ChainCloseGrace,
                _chainCloseHardDeadline);
            return;
        }

        if (Time.realtimeSinceStartup < _chainCloseDeadline) return;

        _chainClosePending = false;
        ServerCloseChain("the round ended before anyone continued filming");
    }

    private bool StillFilming()
    {
        if (_recorder != null && _recorder.IsActive) return true;

        try
        {
            return _recordingState.Value || _sessionOpen.Value;
        }
        catch
        {
            return false;
        }
    }

    private void ServerCloseChain(string? earlyReason)
    {
        if (!IsServer || !_chainOpen) return;

        if (earlyReason != null)
        {
            Plugin.Log.LogDebug($"Closing open chain because {earlyReason}.");
        }

        _chainSegments.Sort((a, b) => a.StartEstimate.CompareTo(b.StartEstimate));
        LogChainClosed();
        bool stitched = ServerFinalizeChain();
        _chainOpen = false;

        _chainHeld.Clear();
        if (!stitched && _chainSegments.Count > 0 && _chainSegments[_chainSegments.Count - 1].EndedByHandoff)
        {
            _chainHeld.AddRange(_chainSegments);
            _chainHeldUntil = Time.realtimeSinceStartup + ChainReopenGrace;
            Plugin.Log.LogDebug(
                $"Holding {_chainHeld.Count} segments for {ChainReopenGrace:0}s in case continuation is still being written.");
        }

        _chainSegments.Clear();
        _serverShadowTake = null;
        if (!StillFilming()) ServerResetTapeClock();
    }

    private void LogChainClosed()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < _chainSegments.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            ChainSegment s = _chainSegments[i];
            sb.Append($"[{i}] client{s.OwnerClientId}:'{s.FileName}' ({s.Duration:0.#}s){(s.EndedByHandoff ? " handoff" : " close")}");
        }
        Plugin.Log.LogDebug($"Chain closed with {_chainSegments.Count} segments: {sb}");
    }

    private bool ServerReopenHeldChain()
    {
        if (!IsServer || _chainHeld.Count == 0) return false;

        if (Time.realtimeSinceStartup > _chainHeldUntil)
        {
            _chainHeld.Clear();
            return false;
        }

        _chainSegments.Clear();
        _chainSegments.AddRange(_chainHeld);
        _chainHeld.Clear();
        _chainOpen = true;
        _chainLastActivityTime = Time.realtimeSinceStartup;
        return true;
    }

    private bool ServerFinalizeChain()
    {
        if (!IsServer) return false;
        if (_chainSegments.Count < 2)
        {
            Plugin.Log.LogDebug($"Nothing to merge in chain of {_chainSegments.Count} segments.");
            return false;
        }

        string mergedName = $"VideoCamera_merged_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}_{NetworkObjectId}.mp4";
        var payload = new StringBuilder();
        int combinedValue = 0;
        float totalDuration = 0f;
        string closingFile = string.Empty;
        int closingValue = 0;
        float closingDuration = 0f;
        for (int i = 0; i < _chainSegments.Count; i++)
        {
            if (i > 0) payload.Append('|');
            ChainSegment s = _chainSegments[i];
            payload.Append(s.OwnerClientId).Append(':').Append(s.FileName);
            combinedValue += s.Value;
            totalDuration += s.Duration;
            if (!s.EndedByHandoff)
            {
                closingFile = s.FileName;
                closingValue = s.Value;
                closingDuration = s.Duration;
            }
        }

        if (string.IsNullOrEmpty(closingFile))
        {
            ChainSegment last = _chainSegments[^1];
            closingFile = last.FileName;
            closingValue = last.Value;
            closingDuration = last.Duration;
            Plugin.Log.LogWarning("No closing segment in this chain so merged film may not attach to cassette.");
        }

        var rows = new List<Operator.Cassette.EnemyBountyTracker.EnemyRow>();
        List<Operator.Cassette.EnemyBountyTracker.EnemyRow>? closingRows = null;
        bool priced = false;
        foreach (ChainSegment s in _chainSegments)
        {
            if (s.Breakdown == null) continue;
            priced = true;
            if (string.Equals(s.FileName, closingFile, StringComparison.OrdinalIgnoreCase))
            {
                closingRows = GetHeldRows(closingFile) ?? Operator.Cassette.EnemyBountyTracker.Parse(s.Breakdown, trusted: true);
                rows.AddRange(closingRows);
            }
            else
            {
                rows.AddRange(Operator.Cassette.EnemyBountyTracker.Parse(s.Breakdown, trusted: true));
            }
        }

        string mergedBreakdown = string.Empty;
        string closingBreakdown = string.Empty;
        if (priced)
        {
            List<Operator.Cassette.EnemyBountyTracker.EnemyRow> claimed = Operator.Cassette.EnemyBountyTracker.Claim(Operator.Cassette.EnemyBountyTracker.Counted(rows));
            mergedBreakdown = Operator.Cassette.EnemyBountyTracker.Serialize(claimed);
            combinedValue = Operator.Cassette.CassetteLimits.ClampValue(Operator.Cassette.EnemyBountyTracker.GetValue(claimed), _chainSegments[0].OwnerClientId, "Chain value");
            if (closingRows != null)
            {
                closingBreakdown = Operator.Cassette.EnemyBountyTracker.Serialize(closingRows);
                closingValue = Operator.Cassette.CassetteLimits.ClampValue(Operator.Cassette.EnemyBountyTracker.GetValue(closingRows), _chainSegments[0].OwnerClientId, "Segment value");
            }
        }

        _serverClosingByJob[mergedName] = (closingValue, closingDuration, closingBreakdown);

        ServerSetChainValue(closingFile, combinedValue, totalDuration, mergedBreakdown);

        Plugin.Log.LogDebug($"Broadcasting stitch job '{mergedName}', {_chainSegments.Count} segments worth ${combinedValue} over {totalDuration:0}s.");
        StitchChainClientRpc(mergedName, payload.ToString(), combinedValue, totalDuration, closingFile);
        return true;
    }

    [ClientRpc]
    private void StitchChainClientRpc(string mergedFileName, string segmentsPayload, int combinedValue, float totalDuration, string closingFileName)
    {
        List<SegmentRef> segs = ParseSegments(segmentsPayload);
        if (segs.Count < 2 || string.IsNullOrEmpty(mergedFileName))
        {
            if (IsServer) ServerMarkChainUnmerged(mergedFileName, closingFileName);
            return;
        }
        foreach (SegmentRef seg in segs) CassetteLibrary.AddPendingMerge(seg.File, mergedFileName, this);
        StartCoroutine(TrackStitch(mergedFileName, StitchWhenReady(mergedFileName, segs, combinedValue, totalDuration, closingFileName)));
    }

    private IEnumerator TrackStitch(string mergedFileName, IEnumerator stitch)
    {
        _stitching[stitch] = this;
        while (true)
        {
            bool more;
            try
            {
                more = stitch.MoveNext();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Merging film failed: {ex}");
                more = false;
            }

            if (!more)
            {
                _stitching.Remove(stitch);
                CassetteLibrary.ClearPendingMerge(mergedFileName);
                yield break;
            }
            yield return stitch.Current;
        }
    }

    private readonly Dictionary<string, (int Value, float Duration, string Breakdown)> _serverClosingByJob = new();

    private List<Operator.Cassette.EnemyBountyTracker.EnemyRow>? GetHeldRows(string fileName)
    {
        CassetteItem? cassette = CassetteItem.FindByFile(fileName);
        if (cassette != null) return Operator.Cassette.EnemyBountyTracker.Parse(cassette.BreakdownRaw, trusted: true);
        if (_serverHasPendingCassette && string.Equals(_serverPendingFileName, fileName, StringComparison.OrdinalIgnoreCase))
        {
            return Operator.Cassette.EnemyBountyTracker.Parse(_serverPendingBreakdown, trusted: true);
        }
        return null;
    }

    private readonly struct SegmentRef
    {
        public readonly ulong Owner;
        public readonly string File;
        public SegmentRef(ulong owner, string file) { Owner = owner; File = file; }
    }

    private static List<SegmentRef> ParseSegments(string payload)
    {
        var list = new List<SegmentRef>();
        if (string.IsNullOrEmpty(payload)) return list;
        foreach (string tok in payload.Split('|'))
        {
            int idx = tok.IndexOf(':');
            if (idx <= 0) continue;
            if (!ulong.TryParse(tok.AsSpan(0, idx), out ulong owner)) continue;
            string file = tok.Substring(idx + 1);
            if (string.IsNullOrEmpty(file)) continue;
            list.Add(new SegmentRef(owner, file));
        }
        return list;
    }

    private IEnumerator StitchWhenReady(string mergedFileName, List<SegmentRef> segs, int combinedValue, float totalDuration, string closingFileName)
    {
        var wait = new WaitForSeconds(StitchPollInterval);
        float lastProgressTime = Time.realtimeSinceStartup;
        int lastProgress = -1;
        int lastResolved = -1;

        while (true)
        {
            bool allReady = true;
            int resolved = 0;
            foreach (SegmentRef seg in segs)
            {
                string? p = FindSegmentPath(seg.Owner, seg.File);
                if (p == null)
                {
                    allReady = false;
                    continue;
                }
                resolved++;
            }

            if (allReady) break;

            int chunks = 0;
            bool anyInFlight = false;
            ClipBroadcaster? broadcaster = ClipBroadcaster.Instance;
            if (broadcaster != null)
            {
                foreach (SegmentRef seg in segs)
                {
                    if (!broadcaster.TryGetIncomingProgress(seg.File, out int received, out int expected)) continue;
                    anyInFlight = true;
                    chunks += received;
                }
            }

            if (chunks != lastProgress || resolved != lastResolved)
            {
                lastProgress = chunks;
                lastResolved = resolved;
                lastProgressTime = Time.realtimeSinceStartup;
            }

            float silent = Time.realtimeSinceStartup - lastProgressTime;
            if (!anyInFlight && silent > StitchSilenceTimeout)
            {
                string missing = string.Join(", ", segs
                    .Where(s => FindSegmentPath(s.Owner, s.File) == null)
                    .Select(s => $"client{s.Owner}:'{s.File}'"));
                Plugin.Log.LogError($"Gave up merging film after {silent:F0}s with no progress, still missing {missing}.");
                if (IsServer) ServerMarkChainUnmerged(mergedFileName, closingFileName);
                yield break;
            }
            yield return wait;
        }

        string outDir = Recorder.GetLobbyOutputDirectory();
        string outPath = Path.Combine(outDir, mergedFileName);

        const int maxStitchAttempts = 3;
        bool ok = false;
        bool noSpace = false;
        bool damaged = false;
        for (int attempt = 1; attempt <= maxStitchAttempts && !ok; attempt++)
        {
            var pathsCopy = new List<string>(segs.Count);
            bool resolved = true;
            foreach (SegmentRef seg in segs)
            {
                string? p = FindSegmentPath(seg.Owner, seg.File);
                if (p == null) { resolved = false; break; }
                pathsCopy.Add(p);
            }

            if (resolved)
            {
                Plugin.Log.LogDebug($"All {segs.Count} segments are here, merging into '{mergedFileName}', attempt {attempt}/{maxStitchAttempts}.");
                bool oneOwner = true;
                for (int i = 1; i < segs.Count; i++)
                {
                    if (segs[i].Owner != segs[0].Owner) { oneOwner = false; break; }
                }
                var stitchTask = RecordingStitcher.ConcatAsync(pathsCopy, outPath, oneOwner);
                _mergeJobs[stitchTask] = this;
                yield return new WaitUntil(() => stitchTask.IsCompleted);
                _mergeJobs.Remove(stitchTask);
                RecordingStitcher.StitchResult result =
                    stitchTask.Status == System.Threading.Tasks.TaskStatus.RanToCompletion
                        ? stitchTask.Result
                        : RecordingStitcher.StitchResult.Failed;
                ok = result == RecordingStitcher.StitchResult.Ok;
                noSpace = result == RecordingStitcher.StitchResult.NoSpace;
                damaged = result == RecordingStitcher.StitchResult.Damaged;
            }

            if (noSpace || damaged) break;

            if (!ok && attempt < maxStitchAttempts)
            {
                Plugin.Log.LogWarning($"Merge attempt {attempt}/{maxStitchAttempts} for '{mergedFileName}' failed, retrying.");
                yield return new WaitForSeconds(1.5f);
            }
        }

        if (!ok)
        {
            if (noSpace)
            {
                Plugin.Log.LogError($"Gave up merging film '{mergedFileName}', drive is full.");
            }
            else if (damaged)
            {
                Plugin.Log.LogError($"Gave up merging film '{mergedFileName}', segment is missing footage.");
            }
            else
            {
                Plugin.Log.LogError($"Failed to merge film '{mergedFileName}' after {maxStitchAttempts} attempts.");
            }
            if (IsServer) ServerMarkChainUnmerged(mergedFileName, closingFileName);
            yield break;
        }

        double mb = new FileInfo(outPath).Length / (1024.0 * 1024.0);
        Plugin.Log.LogInfo($"Merged {segs.Count} segments into '{mergedFileName}', {mb:F1} MB.");

        foreach (SegmentRef seg in segs)
        {
            CassetteLibrary.RegisterMergedRedirect(seg.File, mergedFileName);
        }

        RegisterMergedCassette(mergedFileName, combinedValue, totalDuration);
        if (IsServer)
        {
            _serverClosingByJob.Remove(mergedFileName);
            ServerRepointCassette(closingFileName, mergedFileName, combinedValue, totalDuration);
        }
        CleanupChainSegmentFiles(segs, mergedFileName);
    }

    private void ServerSetChainValue(string closingFileName, int combinedValue, float totalDuration, string mergedBreakdown)
    {
        if (!IsServer || string.IsNullOrEmpty(closingFileName)) return;

        foreach (CassetteItem cassette in UnityEngine.Object.FindObjectsOfType<CassetteItem>())
        {
            if (cassette == null) continue;
            if (!string.Equals(cassette.FileName, closingFileName, StringComparison.OrdinalIgnoreCase)) continue;

            cassette.ServerSetMetadata(cassette.CassetteId, cassette.FileName, cassette.Timestamp, totalDuration, combinedValue, mergedBreakdown, cassette.IsEncoding);
            Plugin.Log.LogDebug($"Cassette '{cassette.CassetteId.ToUpperInvariant()}' is now worth ${combinedValue} for whole chain.");
            return;
        }

        if (string.Equals(_serverPendingFileName, closingFileName, StringComparison.OrdinalIgnoreCase))
        {
            _serverPendingValue = Mathf.Max(1, combinedValue);
            _serverPendingDuration = totalDuration;
            _serverPendingBreakdown = mergedBreakdown;
            UpdateWalletDecorScanClientRpc(true, _serverPendingCassetteId, _serverPendingValue, _serverPendingTimestamp);
            Plugin.Log.LogDebug($"Cassette in camera is now worth ${combinedValue} for whole chain.");
            return;
        }

        Plugin.Log.LogWarning($"No cassette matched '{closingFileName}' so chain value was not applied.");
    }

    private void ServerMarkChainUnmerged(string mergedFileName, string closingFileName)
    {
        if (!IsServer || string.IsNullOrEmpty(closingFileName)) return;
        if (!_serverClosingByJob.TryGetValue(mergedFileName, out var own)) return;
        _serverClosingByJob.Remove(mergedFileName);
        DropMergedFilmClientRpc(mergedFileName);

        int honestValue = Mathf.Max(1, own.Value);
        float honestDuration = own.Duration;

        foreach (CassetteItem cassette in UnityEngine.Object.FindObjectsOfType<CassetteItem>())
        {
            if (cassette == null) continue;
            if (!string.Equals(cassette.FileName, closingFileName, StringComparison.OrdinalIgnoreCase)) continue;

            cassette.ServerSetMetadata(cassette.CassetteId, cassette.FileName, cassette.Timestamp,
                honestDuration, honestValue, own.Breakdown, cassette.IsEncoding);
            Plugin.Log.LogWarning($"Film could not be merged so cassette '{cassette.CassetteId.ToUpperInvariant()}' carries only its own segment, worth ${honestValue}.");
            return;
        }

        if (string.Equals(_serverPendingFileName, closingFileName, StringComparison.OrdinalIgnoreCase))
        {
            _serverPendingValue = honestValue;
            _serverPendingDuration = honestDuration;
            _serverPendingBreakdown = own.Breakdown;
            UpdateWalletDecorScanClientRpc(true, _serverPendingCassetteId, _serverPendingValue, _serverPendingTimestamp);
            Plugin.Log.LogWarning($"Film could not be merged so cassette in camera carries only its own segment, worth ${honestValue}.");
            return;
        }

        Plugin.Log.LogWarning($"Film could not be merged and no cassette matched '{closingFileName}' to correct.");
    }

    private void ServerRepointCassette(string closingFileName, string mergedFileName, int combinedValue, float totalDuration)
    {
        if (!IsServer || string.IsNullOrEmpty(closingFileName)) return;

        foreach (CassetteItem cassette in UnityEngine.Object.FindObjectsOfType<CassetteItem>())
        {
            if (cassette == null) continue;
            if (!string.Equals(cassette.FileName, closingFileName, StringComparison.OrdinalIgnoreCase)) continue;

            cassette.ServerSetMetadata(cassette.CassetteId, mergedFileName, cassette.Timestamp, totalDuration, combinedValue, cassette.BreakdownRaw, cassette.IsEncoding);
            Plugin.Log.LogDebug($"Re-pointed cassette '{cassette.CassetteId.ToUpperInvariant()}' to merged film '{mergedFileName}', worth ${combinedValue} over {totalDuration:0}s.");
            return;
        }

        if (string.Equals(_serverPendingFileName, closingFileName, StringComparison.OrdinalIgnoreCase))
        {
            _serverPendingFileName = mergedFileName;
            _serverPendingValue = Mathf.Max(1, combinedValue);
            _serverPendingDuration = totalDuration;
            UpdateWalletDecorScanClientRpc(true, _serverPendingCassetteId, _serverPendingValue, _serverPendingTimestamp);
            Plugin.Log.LogDebug($"Re-pointed cassette still in camera to merged film '{mergedFileName}'.");
            return;
        }

        Plugin.Log.LogWarning($"No cassette matched '{closingFileName}' so merged film was not re-pointed.");
    }

    private void RegisterMergedCassette(string mergedFileName, int combinedValue, float totalDuration)
    {
        string id = CassetteIdGenerator.GenerateId(mergedFileName);
        var entry = new CassetteLibrary.Entry
        {
            Id = id,
            FileName = mergedFileName,
            Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
            Duration = totalDuration,
            ScrapValue = Mathf.Max(0, combinedValue),
            Breakdown = string.Empty,
            SavedAt = DateTime.UtcNow,
        };
        CassetteLibrary.TrySave(entry);
        Plugin.Log.LogInfo($"Merged recording saved to library as {id.ToUpperInvariant()}, worth ${entry.ScrapValue} over {totalDuration:0}s.");
    }

    private static void CleanupChainSegmentFiles(List<SegmentRef> segs, string mergedFileName)
    {
        HashSet<string> referenced = CassetteLibrary.GetReferencedFiles(includeLibrary: false);

        foreach (SegmentRef seg in segs)
        {
            if (string.Equals(seg.File, mergedFileName, StringComparison.OrdinalIgnoreCase)) continue;
            if (referenced.Contains(seg.File)) continue;
            if (TerminalCassettePlayback.IsFileInUse(seg.File))
            {
                _skippedSegments.Add(seg.File);
                continue;
            }

            int deleted = CassetteLibrary.DeleteRecordingFiles(seg.File);
            if (deleted > 0)
            {
                Plugin.Log.LogInfo($"Removed {deleted} redundant segment files for '{seg.File}' (now merged into '{mergedFileName}').");
            }
        }
    }

    internal static void RetrySegmentCleanup()
    {
        if (_skippedSegments.Count == 0) return;
        HashSet<string> referenced = CassetteLibrary.GetReferencedFiles(includeLibrary: false);
        foreach (string file in _skippedSegments.ToList())
        {
            if (TerminalCassettePlayback.IsFileInUse(file)) continue;
            _skippedSegments.Remove(file);
            if (referenced.Contains(file)) continue;
            int deleted = CassetteLibrary.DeleteRecordingFiles(file);
            if (deleted > 0) Plugin.Log.LogDebug($"Removed {deleted} segment files for '{file}' after playback stopped.");
        }
    }

    [ClientRpc]
    private void DropMergedFilmClientRpc(string mergedFileName)
    {
        CassetteLibrary.DropMergedFilm(mergedFileName);
    }

    private static string? FindSegmentPath(ulong owner, string fileName)
    {
        ulong me = NetworkManager.Singleton.LocalClientId;

        if (owner == me)
        {
            string lobby = Path.Combine(Recorder.GetLobbyOutputDirectory(), fileName);
            if (File.Exists(lobby)) return lobby;
            string baseDir = Path.Combine(Recorder.GetOutputDirectory(), fileName);
            if (File.Exists(baseDir)) return baseDir;
            string unsaved = Path.Combine(Recorder.GetUnsavedOutputDirectory(), fileName);
            if (File.Exists(unsaved)) return unsaved;
            return null;
        }

        string received = Path.Combine(Recorder.GetOutputDirectory(), "Received", $"client{owner}_{fileName}");
        return File.Exists(received) ? received : null;
    }
}
