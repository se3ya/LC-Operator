using System;
using System.Collections.Generic;
using UnityEngine;
using Operator.Cassette;

namespace Operator;

internal sealed partial class VideoCameraItem
{
    private static StartOfRound? _warnedRound;

    internal int SaveHandle;
    private CameraSaveData? _loaded;

    public override int GetItemDataToSave()
    {
        try
        {
            if (!IsServer || !CameraPersistence.IsAvailable) return 0;
            CameraSaveData data = CreateSaveData();
            if (SaveHandle == 0)
            {
                if (data.IsDefault()) return 0;
                SaveHandle = CassettePersistence.AllocateHandle();
                if (SaveHandle == 0) return 0;
            }
            CameraPersistence.Store(SaveHandle, data);
            return SaveHandle;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"Failed to save camera state: {ex}");
            return 0;
        }
    }

    public override void LoadItemSaveData(int saveData)
    {
        if (saveData <= 0 || IsSpawned) return;
        SaveHandle = saveData;
        try
        {
            _loaded = LoadSaved(saveData);
        }
        catch (Exception ex)
        {
            _loaded = null;
            Plugin.Log.LogError($"Failed to load camera state for handle {saveData}: {ex}");
        }
    }

    private static CameraSaveData? LoadSaved(int handle)
    {
        if (!CameraPersistence.IsAvailable)
        {
            if (_warnedRound != StartOfRound.Instance)
            {
                _warnedRound = StartOfRound.Instance;
                Plugin.Log.LogWarning($"Camera saves are not available, so camera handle {handle} starts fresh.");
            }
            return null;
        }

        if (CameraPersistence.TryLoad(handle, out CameraSaveData data)) return data;
        Plugin.Log.LogDebug($"No saved state for camera handle {handle}, so it starts fresh.");
        return null;
    }

    private CameraSaveData CreateSaveData()
    {
        var data = new CameraSaveData
        {
            Charge = GetSavedCharge(),
            CrackFront = _crackStageFront.Value,
            CrackBack = _crackStageBack.Value,
            CrackStar = _crackStarMask.Value,
        };
        if (_serverHasPendingCassette && _serverPendingFileName.Length > 0)
        {
            data.HasTape = true;
            data.TapeId = _serverPendingCassetteId;
            data.TapeFileName = _serverPendingFileName;
            data.TapeTimestamp = _serverPendingTimestamp;
            data.TapeDuration = _serverPendingDuration;
            data.TapeValue = _serverPendingValue;
            data.TapeBreakdown = _serverPendingBreakdown;
        }
        if (HasStoredTape())
        {
            foreach (ChainSegment part in _serverStored)
            {
                data.Parts.Add(new CameraTapePart
                {
                    Owner = (long)part.OwnerClientId,
                    File = part.FileName,
                    Duration = part.Duration,
                    Value = part.Value,
                    Breakdown = part.Breakdown ?? string.Empty,
                    Priced = part.Breakdown != null,
                });
                data.TapeTime += part.Duration;
            }
            data.Lobby = CameraPersistence.GetLobbyStamp();
        }
        return data.Normalized();
    }

    private float GetSavedCharge()
    {
        if (itemProperties == null || !itemProperties.requiresBattery || insertedBattery == null) return 1f;
        return insertedBattery.empty ? 0f : insertedBattery.charge;
    }

    private void ApplyLoaded()
    {
        CameraSaveData? data = _loaded;
        _loaded = null;
        if (data == null || !IsServer) return;
        try
        {
            ApplySaved(data);
            Plugin.Log.LogDebug($"Restored camera {NetworkObjectId} from handle {SaveHandle}.");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"Failed to restore camera {NetworkObjectId} from handle {SaveHandle}: {ex}");
        }
    }

    private void ApplySaved(CameraSaveData data)
    {
        if (data.HasTape)
        {
            _serverHasPendingCassette = true;
            _serverPendingCassetteId = data.TapeId;
            _serverPendingFileName = data.TapeFileName;
            _serverPendingTimestamp = data.TapeTimestamp;
            _serverPendingDuration = CassetteLimits.ClampDuration(data.TapeDuration, OwnerClientId);
            _serverPendingValue = CassetteLimits.ClampStoredValue(Mathf.Max(1, data.TapeValue));
            _serverPendingBreakdown = data.TapeBreakdown;
            _serverPendingEncoding = false;
            _serverPendingOwner = 0;
            _hasPendingCassette.Value = true;
            ApplyStoredParts(data);
        }

        if (itemProperties != null && itemProperties.requiresBattery && insertedBattery != null)
        {
            bool empty = data.Charge <= CameraSaveData.EmptyCharge;
            insertedBattery.charge = empty ? 0f : data.Charge;
            insertedBattery.empty = empty;
            TrySetBatteryLevel(GetBatteryLevel());
        }

        _crackStageFront.Value = data.CrackFront;
        _crackStageBack.Value = data.CrackBack;
        _crackStarMask.Value = data.CrackStar;
    }

    private void ApplyStoredParts(CameraSaveData data)
    {
        _serverStored.Clear();
        if (data.Parts.Count == 0) return;

        bool sameLobby = data.Lobby == CameraPersistence.GetLobbyStamp();
        foreach (CameraTapePart part in data.Parts)
        {
            if (sameLobby || part.Owner == 0) continue;
            Plugin.Log.LogDebug($"Camera {NetworkObjectId} keeps its tape but will not continue it, a client recorded part of it in another lobby.");
            return;
        }

        float now = Time.realtimeSinceStartup;
        foreach (CameraTapePart part in data.Parts)
        {
            _serverStored.Add(new ChainSegment((ulong)part.Owner, part.File, true, part.Duration, part.Value, part.Priced ? part.Breakdown : null, now));
        }
        if (data.TapeTime > 0f) _tapeClockBase.Value = data.TapeTime;
    }

    internal static void AddPendingFiles(HashSet<string> into)
    {
        foreach (VideoCameraItem camera in Spawned)
        {
            if (camera == null) continue;
            foreach (ChainSegment part in camera._serverStored) into.Add(part.FileName);
            if (!camera._serverHasPendingCassette) continue;
            if (camera._serverPendingFileName.Length > 0) into.Add(camera._serverPendingFileName);
        }
    }

    private void SyncPendingTint()
    {
        if (IsServer)
        {
            if (_serverHasPendingCassette) ApplyWalletDecor(true, _serverPendingCassetteId, _serverPendingValue);
        }
        else if (_hasPendingCassette.Value)
        {
            RequestPendingServerRpc();
        }
    }
}
