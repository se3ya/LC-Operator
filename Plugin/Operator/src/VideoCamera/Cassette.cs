using System;
using System.Collections.Generic;
using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using Operator.Cassette;
using Operator.Recording;

namespace Operator;

internal sealed partial class VideoCameraItem
{

    private void OnRecordingFinalized(Recorder.RecordingResult result)
    {
        string fileName = string.IsNullOrEmpty(result.OutputPath) ? string.Empty : System.IO.Path.GetFileName(result.OutputPath);

        if (fileName.Length > 0 && _ejectedTakes.TryGetValue(fileName, out (string Lobby, string Breakdown) ejected))
        {
            _ejectedTakes.Remove(fileName);
            FinishEjectedTake(result, fileName, ejected.Lobby, ejected.Breakdown);
            return;
        }

        EnemyBountyTracker? tracker;
        if (fileName.Length > 0 && _stoppedTrackers.TryGetValue(fileName, out EnemyBountyTracker stashed))
        {
            _stoppedTrackers.Remove(fileName);
            tracker = stashed;
        }
        else
        {
            tracker = _bountyTracker;
            _bountyTracker = null;
        }

        if (!result.FileOk || string.IsNullOrEmpty(result.OutputPath))
        {
            HUDManager.Instance?.DisplayTip("Tape blank!", "Your last take could not be written. No cassette was produced.", true);
            ReportRecordingFailure(result.EndedByHandoff);
            return;
        }

        if (tracker == null)
        {
            ReportRecordingSegment(fileName, result.EndedByHandoff, (float)result.Duration, string.Empty, result.SegmentStartRealtime);
            return;
        }

        var totals = tracker.Finalize();
        string breakdown = totals.Serialize();

        string timestamp = _recordingStartTime != default
            ? _recordingStartTime.ToString("yyyy-MM-dd HH:mm")
            : DateTime.Now.ToString("yyyy-MM-dd HH:mm");

        Recorder.RetainForLobby(fileName);

        string cassetteId = CassetteIdGenerator.GenerateId(fileName);

        SubmitPendingCassette(
            cassetteId,
            fileName,
            timestamp,
            (float)result.Duration,
            EnemyBountyTracker.GetValue(totals.Rows),
            breakdown,
            Time.realtimeSinceStartup - result.SegmentStartRealtime);

        ReportRecordingSegment(fileName, result.EndedByHandoff, (float)result.Duration, breakdown, result.SegmentStartRealtime);
    }

    private void FinishEjectedTake(Recorder.RecordingResult result, string fileName, string lobby, string breakdown)
    {
        if (result.FileOk) Recorder.RetainForLobby(fileName, lobby);
        if (this == null || !IsSpawned) return;

        if (!result.FileOk)
        {
            HUDManager.Instance?.DisplayTip("Tape blank!", "Your last take could not be written, so tape came out blank.", true);
        }

        float duration = (float)result.Duration;
        if (IsServer) ServerFinishEncode(NetworkManager.LocalClientId, fileName, result.FileOk, duration);
        else SubmitEncodeResultServerRpc(fileName, result.FileOk, duration);

        if (result.FileOk) ReportRecordingSegment(fileName, result.EndedByHandoff, duration, breakdown, result.SegmentStartRealtime);
        else ReportRecordingFailure(result.EndedByHandoff);
    }

    private void SubmitPendingCassette(
        string cassetteId,
        string fileName,
        string timestamp,
        float duration,
        int value,
        string breakdown,
        float age)
    {
        if (IsServer)
        {
            ServerStoreTake(cassetteId, fileName, timestamp, duration, value, breakdown, age);
        }
        else
        {
            SubmitPendingCassetteServerRpc(
                cassetteId ?? string.Empty,
                fileName ?? string.Empty,
                timestamp ?? string.Empty,
                duration, value,
                breakdown ?? string.Empty,
                age);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void SubmitPendingCassetteServerRpc(
        string cassetteId,
        string fileName,
        string timestamp,
        float duration,
        int value,
        string breakdown,
        float age)
    {
        ServerStoreTake(cassetteId, fileName, timestamp, duration, value, breakdown, age);
    }

    private void ServerStoreTake(string cassetteId, string fileName, string timestamp, float duration, int value, string breakdown, float age)
    {
        if (!IsServer) return;
        ServerCloseTake();

        float start = Time.realtimeSinceStartup - Mathf.Clamp(age, 0f, MaxSegmentAge);
        if (start < _serverEjectedStart)
        {
            ServerSetShadow(cassetteId, fileName, timestamp, duration, breakdown);
            return;
        }

        if (!ServerStorePendingCassette(cassetteId, fileName, timestamp, duration, value, breakdown)) return;
        PlayWalletAnimationClientRpc(opening: true);
    }

    private void ServerSetShadow(string cassetteId, string fileName, string timestamp, float duration, string breakdown)
    {
        if (string.IsNullOrEmpty(fileName) || !ClipName.IsSafe(fileName)) return;

        List<EnemyBountyTracker.EnemyRow> rows = EnemyBountyTracker.SetDanger(
            EnemyBountyTracker.Parse(breakdown ?? string.Empty),
            EnemyBountyTracker.GetDanger());
        breakdown = EnemyBountyTracker.Serialize(rows);
        int value = _serverSessionWorthless ? 0 : EnemyBountyTracker.GetValue(rows);
        _serverShadowTake = new StoredTake(
            cassetteId ?? string.Empty,
            fileName,
            timestamp ?? string.Empty,
            CassetteLimits.ClampDuration(duration, OwnerClientId),
            CassetteLimits.ClampValue(value, OwnerClientId, "Cassette value"),
            breakdown);
    }

    private static int _shipBlanks;
    private static StartOfRound? _blanksRound;

    private static int BlankMax => Mathf.Clamp(
        VideoCameraConfig.BlankCassettesPerOrbit.Value,
        Constants.ConfigRanges.CassettesPerOrbitMin,
        Constants.ConfigRanges.CassettesPerOrbitMax);

    private static int GetShipBlanks()
    {
        StartOfRound? round = StartOfRound.Instance;
        if (_blanksRound != round)
        {
            _blanksRound = round;
            _shipBlanks = BlankMax;
        }
        return _shipBlanks;
    }

    private static void SetShipBlanks(int count)
    {
        GetShipBlanks();
        _shipBlanks = Mathf.Clamp(count, 0, BlankMax);
        SyncShipBlanks();
    }

    private bool NeedsBlank => !_serverHasPendingCassette
        && (_recordingState.Value || _sessionOpen.Value
            || (_serverTakeOpen && Time.realtimeSinceStartup - _serverTakeEnd < Constants.Bounty.OpenTakeGrace));

    private static readonly List<VideoCameraItem> Spawned = new();

    internal static void GetCarried(GameNetcodeStuff.PlayerControllerB player, List<VideoCameraItem> into)
    {
        foreach (VideoCameraItem camera in Spawned)
        {
            if (camera != null && camera.playerHeldBy == player) into.Add(camera);
        }
    }

    private static void SyncShipBlanks()
    {
        int pool = GetShipBlanks();
        List<VideoCameraItem> cameras = Spawned;
        int open = 0;
        foreach (VideoCameraItem camera in cameras)
        {
            if (camera != null && camera.IsServer && camera.IsSpawned && camera.NeedsBlank) open++;
        }

        foreach (VideoCameraItem camera in cameras)
        {
            if (camera == null || !camera.IsServer || !camera.IsSpawned) continue;
            bool own = camera.NeedsBlank;
            int shown = Mathf.Max(0, pool - (open - (own ? 1 : 0)));
            if (own && pool > 0) shown = Mathf.Max(shown, 1);
            if (camera._blanksRemaining.Value != shown) camera._blanksRemaining.Value = shown;
        }
    }

    private IEnumerator SyncBlanksLoop()
    {
        var wait = new WaitForSeconds(1f);
        while (IsSpawned)
        {
            yield return wait;
            Spawned.RemoveAll(c => c == null);
            if (Spawned.Count > 0 && Spawned[0] == this && VideoCameraConfig.BlanksPerShip.Value) SyncShipBlanks();
        }
    }

    private void OnClientGone(ulong clientId)
    {
        if (!IsServer || !IsSpawned) return;
        if (clientId != _serverSessionOwner && clientId != OwnerClientId) return;
        if (!_recordingState.Value && !_sessionOpen.Value && !_serverTakeOpen) return;
        TrySetRecordingState(false);
        TrySetSessionOpen(false);
        _serverTakeOpen = false;
        ServerSyncBlanks();
    }

    private void ServerCloseTake()
    {
        if (!_recordingState.Value && !_sessionOpen.Value) _serverTakeOpen = false;
        ServerSyncBlanks();
    }

    private void ServerSyncBlanks()
    {
        if (IsServer && VideoCameraConfig.BlanksPerShip.Value) SyncShipBlanks();
    }

    private void ServerTakeBlank()
    {
        if (VideoCameraConfig.BlanksPerShip.Value) SetShipBlanks(GetShipBlanks() - 1);
        else _blanksRemaining.Value -= 1;
    }

    public void ServerRestockBlanks(bool orbit = true)
    {
        if (!IsServer || !IsSpawned) return;
        bool hadLiveSession = _sessionOpen.Value;
        _sessionOpen.Value = false;
        _serverHandoffTime = -1d;
        if (!hadLiveSession) ServerResetTapeClock();
        if (!_recordingState.Value) _serverTakeOpen = false;
        if (!VideoCameraConfig.BlanksPerShip.Value) _blanksRemaining.Value = BlankMax;
        else if (orbit) SetShipBlanks(BlankMax);
        else
        {
            _blanksRemaining.Value = GetShipBlanks();
            SyncShipBlanks();
        }
    }

    private void OnBlanksRemainingChanged(int _, int __)
    {
        RefreshBlanks();
        RefreshTooltips();
    }

    private void OnHasPendingCassetteChanged(bool _, bool __)
    {
        RefreshBlanks();
        ApplyLedVisuals();
        RefreshTooltips();
    }

    private void RefreshBlanks()
    {
        RefreshTapeReels();
        RefreshWalletScan();
        UpdateCameraScanText();
        UpdateWalletDecorVisibility();
    }

    private void RefreshWalletScan()
    {
        if (_walletDecorScan == null) return;

        bool showScan = _hasPendingCassette.Value && playerHeldBy == null;
        if (_walletDecorScan.gameObject.activeSelf != showScan)
        {
            _walletDecorScan.gameObject.SetActive(showScan);
        }
        if (showScan)
        {
            PrimeScanNode(_walletDecorScan, _walletDecor != null ? _walletDecor.transform : _walletDecorScan.transform, 0, "Recorded Cassette", "Press X to eject");
            _walletDecorScan.headerText = "Recorded Cassette";
            _walletDecorScan.requiresLineOfSight = true;
        }
    }

    private void UpdateCameraScanText()
    {
        if (!_cameraScanNodeSearched)
        {
            _cameraScanNodeSearched = true;
            Transform? wallet = GetWallet();
            foreach (ScanNodeProperties node in GetComponentsInChildren<ScanNodeProperties>(true))
            {
                if (node == null) continue;
                if (wallet != null && node.transform.IsChildOf(wallet)) continue;
                _cameraScanNode = node;
                ForceToolScanNode(node);
                break;
            }
        }

        if (_cameraScanNode == null) return;
        bool walletScanShown = _walletDecorScan != null && _walletDecorScan.gameObject.activeSelf;
        if (_cameraScanNode.gameObject.activeSelf == walletScanShown)
        {
            _cameraScanNode.gameObject.SetActive(!walletScanShown);
        }
        _cameraScanNode.subText = $"Cassettes left: {Mathf.Max(0, _blanksRemaining.Value)}";
    }

    private static void ForceToolScanNode(ScanNodeProperties node)
    {
        node.nodeType = 0;
        node.scrapValue = 0;
        node.creatureScanID = -1;
        node.enabled = true;
        Collider? col = node.GetComponent<Collider>();
        if (col != null)
        {
            col.enabled = true;
            col.isTrigger = true;
        }
    }

    private void UpdateWalletDecorVisibility()
    {
        FindWalletDecor();
        if (_walletDecor == null) return;
        bool show = !_decorHidden && (_hasPendingCassette.Value || _blanksRemaining.Value > 0);
        if (_walletDecor.activeSelf != show)
        {
            _walletDecor.SetActive(show);
        }
    }

    private bool ServerStorePendingCassette(string cassetteId, string fileName, string timestamp, float duration, int value, string breakdown)
    {
        if (!IsServer) return false;

        if (!string.IsNullOrEmpty(fileName) && !ClipName.IsSafe(fileName))
        {
            Plugin.Log.LogWarning($"Cassette from client {OwnerClientId} rejected, recording name is not plain mp4 file.");
            return false;
        }

        int available = VideoCameraConfig.BlanksPerShip.Value ? GetShipBlanks() : _blanksRemaining.Value;
        if (!_serverHasPendingCassette && available <= 0)
        {
            return false;
        }

        breakdown ??= string.Empty;
        if (_serverSessionWorthless)
        {
            value = 0;
        }
        else
        {
            bool sameTake = _serverHasPendingCassette && string.Equals(_serverPendingFileName, fileName, StringComparison.OrdinalIgnoreCase);
            List<EnemyBountyTracker.EnemyRow> rows = EnemyBountyTracker.Claim(
                EnemyBountyTracker.SetDanger(EnemyBountyTracker.Parse(breakdown), EnemyBountyTracker.GetDanger()),
                sameTake ? EnemyBountyTracker.Parse(_serverPendingBreakdown, trusted: true) : null);
            breakdown = EnemyBountyTracker.Serialize(rows);
            value = EnemyBountyTracker.GetValue(rows);
        }
        value = CassetteLimits.ClampValue(value, OwnerClientId, "Cassette value");
        Plugin.Log.LogDebug($"Tape from client {OwnerClientId} is worth ${value}: {breakdown}");
        duration = CassetteLimits.ClampDuration(duration, OwnerClientId);

        if (!_serverHasPendingCassette)
        {
            ServerTakeBlank();
        }
        _serverHasPendingCassette = true;
        _hasPendingCassette.Value = true;
        ServerSyncBlanks();
        _serverPendingCassetteId = cassetteId ?? string.Empty;
        _serverPendingFileName = fileName ?? string.Empty;
        _serverPendingTimestamp = timestamp ?? string.Empty;
        _serverPendingDuration = duration;
        _serverPendingValue = Mathf.Max(1, value);
        _serverPendingBreakdown = breakdown;
        _serverPendingEncoding = false;
        _serverPendingOwner = 0;

        UpdateWalletDecorScanClientRpc(true, _serverPendingCassetteId, _serverPendingValue, _serverPendingTimestamp);
        return true;
    }

    [ClientRpc]
    private void PlayWalletAnimationClientRpc(bool opening)
    {
        if (opening)
        {
            _walletBusyUntil = Math.Max(_walletBusyUntil, Time.unscaledTimeAsDouble + Constants.Camera.PocketOpenSettle);
            PlayWalletAnimation(opening: true);
            return;
        }

        if (_walletEjectCoroutine != null) StopCoroutine(_walletEjectCoroutine);
        _walletEjectCoroutine = null;

        if (IsPocketSettled() || !isActiveAndEnabled)
        {
            EjectPocket();
            return;
        }
        _walletEjectCoroutine = StartCoroutine(EjectAfterOpen());
    }

    private IEnumerator EjectAfterOpen()
    {
        float settle = Constants.Camera.PocketOpenSettle;
        _walletBusyUntil = Time.unscaledTimeAsDouble + settle
            + GetClipLength(_walletAnimation, Constants.Assets.PocketEjectAnimationName);
        if (!_walletOpen) PlayWalletAnimation(opening: true);
        yield return new WaitForSeconds(settle);

        _walletEjectCoroutine = null;
        EjectPocket();
    }

    private void EjectPocket()
    {
        PlayWalletAnimation(opening: false, eject: true);
        PlayCassetteEjectSfx();
    }

    private bool IsPocketSettled()
    {
        if (!_walletOpen || !_hasPendingCassette.Value || playerHeldBy != null) return false;
        double now = Time.unscaledTimeAsDouble;
        return now >= _walletBusyUntil && now - _walletOpenedAt >= Constants.Camera.PocketOpenSettle;
    }

    private void RequestEjectCassette()
    {
        if (!_networkReady) return;

        if (_recorder != null && _recorder.IsActive)
        {
            EjectActiveTake();
            return;
        }

        Vector3 spawnPos = GetEjectPosition();
        if (IsServer) ServerBeginEject(spawnPos);
        else RequestEjectCassetteServerRpc(spawnPos);
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestEjectCassetteServerRpc(Vector3 spawnWorldPosition)
    {
        ServerBeginEject(spawnWorldPosition);
    }

    private void EjectActiveTake()
    {
        Recorder recorder = _recorder!;
        string fileName = recorder.OutputFileName;
        if (fileName.Length == 0 || _ejectedTakes.ContainsKey(fileName)) return;

        if (!_hasPendingCassette.Value && _blanksRemaining.Value <= 0)
        {
            HUDManager.Instance?.DisplayTip("No tape", "You are out of blank cassettes. More arrive next orbit.");
            return;
        }

        EnemyBountyTracker? tracker = _bountyTracker;
        _bountyTracker = null;
        string breakdown = string.Empty;
        int value = 0;
        if (tracker != null)
        {
            EnemyBountyTracker.BountyTotals totals = tracker.Finalize();
            breakdown = totals.Serialize();
            value = EnemyBountyTracker.GetValue(totals.Rows);
        }
        string timestamp = (_recordingStartTime != default ? _recordingStartTime : DateTime.Now)
            .ToString("yyyy-MM-dd HH:mm");
        float duration = (float)recorder.ActiveTime;
        float age = Time.realtimeSinceStartup - recorder.SegmentStartRealtime;
        Vector3 spawnPos = GetEjectPosition();

        _ejectedTakes[fileName] = (Recorder.GetLobbyOutputDirectory(), breakdown);

        if (_recordingState.Value) RequestStopRecording();
        TryStopRecording();

        string cassetteId = CassetteIdGenerator.GenerateId(fileName);
        if (IsServer)
        {
            ServerEjectTake(NetworkManager.LocalClientId, cassetteId, fileName, timestamp, duration, value, breakdown, age, spawnPos);
        }
        else
        {
            EjectTakeServerRpc(cassetteId, fileName, timestamp, duration, value, breakdown, age, spawnPos);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void EjectTakeServerRpc(
        string cassetteId,
        string fileName,
        string timestamp,
        float duration,
        int value,
        string breakdown,
        float age,
        Vector3 spawnPosition,
        ServerRpcParams rpcParams = default)
    {
        ServerEjectTake(rpcParams.Receive.SenderClientId, cassetteId, fileName, timestamp, duration, value, breakdown, age, spawnPosition);
    }

    private void ServerEjectTake(
        ulong owner,
        string cassetteId,
        string fileName,
        string timestamp,
        float duration,
        int value,
        string breakdown,
        float age,
        Vector3 spawnPosition)
    {
        if (!IsServer || string.IsNullOrEmpty(fileName)) return;
        if (CassetteItem.FindByFile(fileName) != null) return;
        if (_serverHasPendingCassette && _serverPendingEncoding
            && string.Equals(_serverPendingFileName, fileName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!ServerStorePendingCassette(cassetteId, fileName, timestamp, duration, value, breakdown)) return;

        _serverTakeOpen = false;
        _serverPendingEncoding = true;
        _serverPendingOwner = owner;
        _serverEjectedStart = Mathf.Max(_serverEjectedStart, Time.realtimeSinceStartup - Mathf.Clamp(age, 0f, MaxSegmentAge));
        ServerBeginEject(spawnPosition);
    }

    private void ServerBeginEject(Vector3 spawnPosition)
    {
        if (!IsServer || !_serverHasPendingCassette || _serverEjectCoroutine != null) return;

        bool settled = IsPocketSettled();
        bool active = isActiveAndEnabled;
        PlayWalletAnimationClientRpc(opening: false);
        if (!active)
        {
            ServerTryEjectCassette(spawnPosition);
            return;
        }
        float delay = Constants.Camera.EjectLiftTime + (settled ? 0f : Constants.Camera.PocketOpenSettle);
        _serverEjectCoroutine = StartCoroutine(SpawnAfterOpen(spawnPosition, delay));
    }

    private IEnumerator SpawnAfterOpen(Vector3 spawnPosition, float delay)
    {
        yield return new WaitForSeconds(delay);
        _serverEjectCoroutine = null;
        ServerTryEjectCassette(spawnPosition);
    }

    [ServerRpc(RequireOwnership = false)]
    private void SubmitEncodeResultServerRpc(string fileName, bool ok, float duration, ServerRpcParams rpcParams = default)
    {
        ServerFinishEncode(rpcParams.Receive.SenderClientId, fileName, ok, duration);
    }

    private void ServerFinishEncode(ulong sender, string fileName, bool ok, float duration)
    {
        if (!IsServer || string.IsNullOrEmpty(fileName)) return;

        CassetteItem? cassette = CassetteItem.FindByFile(fileName);
        if (cassette != null)
        {
            if (!ok && cassette.IsEncodingBy(sender) && _serverShadowTake.HasValue)
            {
                StoredTake shadow = _serverShadowTake.Value;
                _serverShadowTake = null;
                Plugin.Log.LogWarning($"Cassette '{cassette.CassetteId.ToUpperInvariant()}' could not be written, so it carries earlier take instead.");
                cassette.ServerSetMetadata(cassette.CassetteId, shadow.File, shadow.Timestamp, shadow.Duration, shadow.Value, shadow.Breakdown, encoding: false);
                return;
            }
            if (cassette.ServerFinishEncode(sender, ok, duration))
            {
                _serverShadowTake = null;
                if (!ok) ServerRefundBlank();
            }
            return;
        }

        if (!_serverHasPendingCassette || !_serverPendingEncoding || _serverPendingOwner != sender) return;
        if (!string.Equals(_serverPendingFileName, fileName, StringComparison.OrdinalIgnoreCase)) return;

        _serverPendingEncoding = false;
        _serverPendingDuration = CassetteLimits.ClampDuration(duration, sender);
        if (ok)
        {
            _serverShadowTake = null;
            return;
        }

        if (_serverShadowTake.HasValue)
        {
            StoredTake shadow = _serverShadowTake.Value;
            _serverShadowTake = null;
            _serverPendingFileName = shadow.File;
            _serverPendingTimestamp = shadow.Timestamp;
            _serverPendingDuration = shadow.Duration;
            _serverPendingValue = Mathf.Max(1, shadow.Value);
            _serverPendingBreakdown = shadow.Breakdown;
            UpdateWalletDecorScanClientRpc(true, _serverPendingCassetteId, _serverPendingValue, _serverPendingTimestamp);
            Plugin.Log.LogWarning("Take could not be written, so cassette in camera carries earlier take instead.");
            return;
        }

        _serverPendingFileName = string.Empty;
        _serverPendingValue = 0;
        _serverPendingBreakdown = string.Empty;
        ServerRefundBlank();
    }

    private void ServerRefundBlank()
    {
        if (!IsServer || !IsSpawned) return;
        if (VideoCameraConfig.BlanksPerShip.Value) SetShipBlanks(GetShipBlanks() + 1);
        else _blanksRemaining.Value = Mathf.Min(_blanksRemaining.Value + 1, BlankMax);
    }

    private Vector3 GetEjectPosition()
    {
        var sor = StartOfRound.Instance;
        var local = sor != null ? sor.localPlayerController : null;

        if (this != null && playerHeldBy != null
            && local != null && playerHeldBy == local)
        {
            Vector3 held = local.transform.position
                         + local.transform.forward * 0.30f
                         + Vector3.up * 0.60f;
            return held;
        }

        Vector3 fallback = transform.position + Vector3.up * 1.0f;
        return fallback;
    }

    private void ServerTryEjectCassette(Vector3 spawnWorldPosition)
    {
        if (!IsServer) return;
        if (!_serverHasPendingCassette) return;

        if (!CassetteLimits.IsSaneSpawn(spawnWorldPosition, transform.position))
        {
            spawnWorldPosition = transform.position + Vector3.up;
        }

        if (CassetteRegistrar.CassettePrefab == null)
        {
            Plugin.Log.LogError("Could not eject cassette, cassette prefab is not registered.");
            return;
        }

        string cassetteId = _serverPendingCassetteId;
        string fileName = _serverPendingFileName;
        string timestamp = _serverPendingTimestamp;
        float duration = _serverPendingDuration;
        int value = _serverPendingValue;
        string breakdown = _serverPendingBreakdown;
        bool encoding = _serverPendingEncoding;
        ulong owner = _serverPendingOwner;
        _serverHasPendingCassette = false;
        _hasPendingCassette.Value = false;
        if (_recordingState.Value) _serverTakeOpen = true;
        ServerSyncBlanks();
        if (!_recordingState.Value) TrySetSessionOpen(false);
        _serverPendingCassetteId = string.Empty;
        _serverPendingFileName = string.Empty;
        _serverPendingTimestamp = string.Empty;
        _serverPendingDuration = 0f;
        _serverPendingValue = 0;
        _serverPendingBreakdown = string.Empty;
        _serverPendingEncoding = false;
        _serverPendingOwner = 0;

        if (!TryGetEjectPose(out Vector3 pos, out Quaternion rot))
        {
            Transform? ejectAnchor = GetEjectAnchor();
            if (ejectAnchor == null) ejectAnchor = GetWallet();
            pos = ejectAnchor != null ? ejectAnchor.position : spawnWorldPosition;
            rot = ejectAnchor != null ? ejectAnchor.rotation : transform.rotation;
        }
        var holder = playerHeldBy;

        Transform? scrapContainer = GetScrapContainer(holder);
        GameObject go = UnityEngine.Object.Instantiate(CassetteRegistrar.CassettePrefab, pos, rot);
        if (!go.TryGetComponent(out NetworkObject netObj))
        {
            Plugin.Log.LogError("Cassette prefab has no NetworkObject, ejected cassette was destroyed.");
            Destroy(go);
            return;
        }

        netObj.Spawn(destroyWithScene: true);

        CassetteItem? cassette = go.GetComponent<CassetteItem>();
        if (cassette != null)
        {
            cassette.ServerSetMetadata(cassetteId, fileName, timestamp, duration, value, breakdown, encoding);
            if (encoding) cassette.ServerWatchEncode(owner);
            cassette.isInElevator = holder != null ? holder.isInElevator : isInElevator;
            cassette.isInShipRoom = holder != null ? holder.isInHangarShipRoom : isInShipRoom;
        }

        try
        {
            if (scrapContainer != null)
            {
                go.transform.SetParent(scrapContainer, worldPositionStays: true);
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"Failed to parent ejected cassette '{cassetteId}' to scrap container: {ex}");
        }

        if (cassette != null)
        {
            try { cassette.Start(); }
            catch (Exception ex) { Plugin.Log.LogWarning($"Failed to start ejected cassette '{cassetteId}': {ex}"); }

            cassette.transform.SetPositionAndRotation(pos, rot);
            cassette.startFallingPosition = cassette.transform.position;
            var castParent = cassette.transform.parent;
            if (castParent != null)
            {
                cassette.startFallingPosition = castParent.InverseTransformPoint(cassette.startFallingPosition);
            }

            try { cassette.EnableItemMeshes(true); }
            catch (Exception ex) { Plugin.Log.LogWarning($"Failed to show ejected cassette '{cassetteId}': {ex}"); }

            bool launched = false;
            try { launched = TryLaunchCassette(cassette, pos, rot); }
            catch (Exception ex) { Plugin.Log.LogDebug($"Failed to launch ejected cassette '{cassetteId}': {ex}"); }

            if (!launched)
            {
                try { cassette.FallToGround(); }
                catch (Exception ex) { Plugin.Log.LogWarning($"Failed to drop ejected cassette '{cassetteId}' to ground: {ex}"); }
            }

            var scans = go.GetComponentsInChildren<ScanNodeProperties>(includeInactive: true);
            if (scans != null && scans.Length > 0)
            {
                string scanSubText = BuildSubText(cassetteId, value);
                for (int i = 0; i < scans.Length; i++)
                {
                    var scanNode = scans[i];
                    if (scanNode == null) continue;
                    PrimeScanNode(scanNode, go.transform, value, "Cassette", scanSubText);
                }
            }
        }

        UpdateWalletDecorScanClientRpc(false, string.Empty, 0, string.Empty);
    }

    private bool TryLaunchCassette(CassetteItem cassette, Vector3 release, Quaternion rotation)
    {
        Vector3 dir = Vector3.ProjectOnPlane(-transform.forward, Vector3.up);
        if (dir.sqrMagnitude < 0.0001f) dir = Vector3.ProjectOnPlane(transform.up, Vector3.up);
        if (dir.sqrMagnitude < 0.0001f) dir = Vector3.forward;
        dir.Normalize();

        Vector3 side = Vector3.Cross(Vector3.up, dir);
        float spread = Constants.Camera.EjectSpread;
        Vector3 offset = dir * Constants.Camera.EjectDistance + side * UnityEngine.Random.Range(-spread, spread);
        float distance = offset.magnitude;
        Vector3 heading = offset / distance;
        if (Physics.SphereCast(release, Constants.Camera.EjectProbeRadius, heading, out RaycastHit wall, distance,
                Constants.Cassette.FloorRaycastMask, QueryTriggerInteraction.Ignore))
        {
            distance = Mathf.Max(Constants.Camera.EjectWallGap, wall.distance - Constants.Camera.EjectWallGap);
        }
        Vector3 land = release + heading * distance;

        if (!TryFindFloor(land, Mathf.Max(release.y, land.y) + 1f, out float floorY)) return false;
        if (floorY > release.y && !TryFindFloor(land, release.y, out floorY)) return false;
        float verticalOffset = cassette.itemProperties != null ? cassette.itemProperties.verticalOffset : 0f;
        land.y = floorY + verticalOffset + Constants.Cassette.RestHeight;

        Vector3 axis = Vector3.ProjectOnPlane(rotation * Vector3.right, Vector3.up);
        if (axis.sqrMagnitude < 0.0001f) axis = dir;
        float jitter = Constants.Camera.EjectSpinJitter;
        float yaw = Mathf.Atan2(axis.x, axis.z) * Mathf.Rad2Deg
                    + Constants.Camera.EjectSpin + UnityEngine.Random.Range(-jitter, jitter);

        return cassette.ServerStartArc(this, land, yaw, Constants.Camera.EjectUpSpeed);
    }

    private static bool TryFindFloor(Vector3 at, float fromY, out float floorY)
    {
        Vector3 origin = new(at.x, fromY, at.z);
        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, (fromY - at.y) + 80f,
                Constants.Cassette.FloorRaycastMask, QueryTriggerInteraction.Ignore))
        {
            floorY = hit.point.y;
            return true;
        }
        floorY = 0f;
        return false;
    }

    private static Transform? GetScrapContainer(GameNetcodeStuff.PlayerControllerB? holder)
    {
        var sor = StartOfRound.Instance;
        var rm = RoundManager.Instance;
        bool inShip = holder != null && holder.isInHangarShipRoom;

        if (inShip && sor != null && sor.elevatorTransform != null)
        {
            return sor.elevatorTransform;
        }
        if (rm != null && rm.spawnedScrapContainer != null)
        {
            return rm.spawnedScrapContainer;
        }
        if (sor != null && sor.elevatorTransform != null)
        {
            return sor.elevatorTransform;
        }
        return null;
    }

    private Transform? GetWallet()
    {
        if (_walletTransform != null) return _walletTransform;
        _walletTransform = FindDeepChild(transform, Constants.Assets.PocketObjectName);
        return _walletTransform;
    }

    private Transform? GetEjectAnchor()
    {
        if (_ejectAnchor != null) return _ejectAnchor;
        _ejectAnchor = FindDeepChild(transform, Constants.Assets.EjectAnchorObjectName);
        return _ejectAnchor;
    }

    private bool TryBindDecor(Transform wallet)
    {
        Transform? existingDecor = null;
        foreach (Transform child in wallet.GetComponentsInChildren<Transform>(includeInactive: true))
        {
            if (child == null || child == wallet) continue;
            if (child.name.IndexOf("cassette", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                existingDecor = child;
                break;
            }
        }
        if (existingDecor == null) return false;

        _walletDecor = existingDecor.gameObject;
        _decorBase = existingDecor.localPosition;
        StripDecorInteractables(_walletDecor);
        _walletDecorScan = _walletDecor.GetComponentInChildren<ScanNodeProperties>(includeInactive: true);
        if (_walletDecorScan == null)
        {
            var scanGo = new GameObject("VideoCamWalletScan");
            scanGo.transform.SetParent(_walletDecor.transform, worldPositionStays: false);
            scanGo.transform.localPosition = Vector3.zero;
            _walletDecorScan = scanGo.AddComponent<ScanNodeProperties>();
        }

        PrimeScanNode(_walletDecorScan, _walletDecor.transform, 0, "Recorded Cassette", string.Empty);
        _walletDecorScan.headerText = "Recorded Cassette";
        _walletDecorScan.subText = string.Empty;
        _walletDecorScan.gameObject.SetActive(false);
        return true;
    }

    private static void StripDecorInteractables(GameObject decor)
    {
        if (decor == null) return;

        foreach (var col in decor.GetComponentsInChildren<Collider>(includeInactive: true))
        {
            if (col != null) col.enabled = false;
        }

        foreach (var t in decor.GetComponentsInChildren<Transform>(includeInactive: true))
        {
            if (t == null) continue;
            try
            {
                if (t.CompareTag("PhysicsProp")) t.tag = "Untagged";
            }
            catch { }
        }
    }

    private void FindWalletDecor()
    {
        if (_walletDecor != null) return;
        Transform? wallet = GetWallet();
        if (wallet == null) return;

        TryBindDecor(wallet);
    }

    private static void ActivateScanHierarchy(Transform leaf, Transform root)
    {
        Transform? current = leaf;
        while (current != null)
        {
            if (!current.gameObject.activeSelf)
            {
                current.gameObject.SetActive(true);
            }
            if (current == root) break;
            current = current.parent;
        }
    }

    private static void PrimeScanNode(ScanNodeProperties scanNode, Transform root, int value, string header, string subText)
    {
        ActivateScanHierarchy(scanNode.transform, root);

        int scanLayer = LayerMask.NameToLayer("ScanNode");
        if (scanLayer < 0) scanLayer = 22;
        if (scanLayer >= 0)
        {
            scanNode.gameObject.layer = scanLayer;
        }

        scanNode.enabled = true;
        Collider? col = scanNode.GetComponent<Collider>();
        if (col == null)
        {
            var box = scanNode.gameObject.AddComponent<BoxCollider>();
            box.size = new Vector3(0.28f, 0.28f, 0.28f);
            col = box;
        }
        col.enabled = true;
        col.isTrigger = true;

        if (string.IsNullOrWhiteSpace(scanNode.headerText)) scanNode.headerText = header;
        scanNode.scrapValue = Mathf.Max(0, value);
        scanNode.subText = subText;
        scanNode.nodeType = 2;
        scanNode.requiresLineOfSight = true;
        scanNode.minRange = Constants.Cassette.MinScanRange;
        scanNode.maxRange = Constants.Cassette.MaxScanRange;
    }

    private static string BuildSubText(string cassetteId, int value)
    {
        string idShown = string.IsNullOrWhiteSpace(cassetteId)
            ? "?"
            : cassetteId.Trim().ToUpperInvariant();
        return $"ID: {idShown} | Value: ${Mathf.Max(0, value)}";
    }

    private void ApplyWalletDecor(bool enable, string id, int value)
    {
        FindWalletDecor();
        if (_walletDecor != null)
        {
            if (enable) CassetteShellColor.Apply(_walletDecor.transform, id, value);
            else CassetteShellColor.Clear(_walletDecor.transform);
        }

        RefreshBlanks();
        RefreshTooltips();
    }

    [ClientRpc]
    private void UpdateWalletDecorScanClientRpc(bool enable, string id, int value, string timestamp)
    {
        ApplyWalletDecor(enable, id ?? string.Empty, value);
    }

    private void OnEjectCassetteInput(InputAction.CallbackContext _)
    {
        if (Time.unscaledTimeAsDouble < _nextEjectTime)
        {
            return;
        }
        _nextEjectTime = Time.unscaledTimeAsDouble + 0.25d;

        if (!_networkReady)
        {
            return;
        }

        if (CanUse())
        {
            RequestEjectCassette();
            return;
        }

        var sor = StartOfRound.Instance;
        var local = sor != null ? sor.localPlayerController : null;
        if (local == null)
        {
            return;
        }
        if (this == null || playerHeldBy != null)
        {
            return;
        }
        if (!_isHovered)
        {
            return;
        }
        if (local.isPlayerDead || local.inTerminalMenu || local.isTypingChat)
        {
            return;
        }

        RequestEjectCassette();
    }

}
