using System;
using System.Collections;
using Unity.Netcode;
using UnityEngine;
using Operator.Compat;

namespace Operator;

internal sealed partial class VideoCameraItem
{
    private bool HasUsableBattery()
    {
        if (itemProperties == null || !itemProperties.requiresBattery)
        {
            return true;
        }

        return insertedBattery != null
               && !insertedBattery.empty
               && insertedBattery.charge > 0.001f;
    }

    private float CurrentCharge()
    {
        return insertedBattery?.charge ?? 0f;
    }

    internal bool IsPowered =>
        playerHeldBy != null && !isPocketed && !_wasSeatedLastFrame && HasUsableBattery();

    private int GetBatteryLevel()
    {
        if (!HasUsableBattery()) return 0;
        return Mathf.CeilToInt(CurrentCharge() * 1000f);
    }

    private void TrySetBatteryLevel(int level)
    {
        if (!IsOwner || !IsSpawned
            || NetworkObject == null || !NetworkObject.IsSpawned)
        {
            return;
        }
        if (_batteryLevel.Value == level) return;
        try { _batteryLevel.Value = level; }
        catch (Exception) { }
    }

    private void OnBatteryLevelChanged(int previous, int current)
    {
        if (!IsOwner)
        {
            ApplyBatteryLevel(current);
            if (previous > 0 && current == 0) PlaySpinDown();
        }
        if ((previous == 0) != (current == 0)) RefreshTooltips();
    }

    private void ApplyBatteryLevel(int level)
    {
        if (level < 0 || insertedBattery == null) return;
        if (itemProperties == null || !itemProperties.requiresBattery) return;
        insertedBattery.charge = level / 1000f;
        insertedBattery.empty = level == 0;
    }

    private float GetBatteryLife()
    {
        float baseline = itemProperties != null ? itemProperties.batteryUsage : 0f;
        return Mathf.Max(1f, baseline) * Constants.Battery.CapacityScale;
    }

    private float CurrentDrainMultiplier()
    {
        float multiplier = 0f;
        bool recording = _recordingState.Value;
        if (IsPowered || recording) multiplier += Mathf.Max(0f, VideoCameraConfig.IdleBatteryDrain.Value);
        if (recording) multiplier += Constants.Battery.RecordDrainMultiplier;
        if (_nightVisionOn.Value) multiplier += Mathf.Max(0f, Constants.NightVision.BatteryDrainMultiplier);
        return multiplier;
    }

    private int BatteryMinutesRemaining()
    {
        if (itemProperties == null || !itemProperties.requiresBattery) return -1;

        float charge = CurrentCharge();
        if (charge <= 0f) return 0;

        float multiplier = CurrentDrainMultiplier();
        if (multiplier <= 0f) return -1;

        float perSecond = ApplyLithiumBatteriesRate(multiplier / GetBatteryLife());
        if (perSecond <= 0f) return -1;

        int minutes = Mathf.CeilToInt(charge / perSecond / 60f);
        return Mathf.Clamp(minutes, 0, Constants.Osd.BatteryMinutesCap);
    }

    private IEnumerator DrainBattery()
    {
        float interval = Constants.Battery.DrainInterval;
        WaitForSeconds wait = new(interval);

        while (true)
        {
            yield return wait;

            if (!IsSpawned || !_networkReady) continue;
            if (NetworkObject == null || !NetworkObject.IsSpawned) continue;
            if (itemProperties == null || !itemProperties.requiresBattery) continue;
            if (insertedBattery == null) continue;

            bool recording = _recordingState.Value;
            bool nightVision = _nightVisionOn.Value;

            isBeingUsed = false;
            if (!IsOwner) continue;

            if (!insertedBattery.empty)
            {
                try
                {
                    float multiplier = CurrentDrainMultiplier();
                    if (multiplier > 0f)
                    {
                        float drain = ApplyLithiumBatteriesRate((interval / GetBatteryLife()) * multiplier);
                        insertedBattery.charge = Mathf.Max(0f, insertedBattery.charge - drain);
                    }

                    if (!HasUsableBattery())
                    {
                        insertedBattery.charge = 0f;
                        insertedBattery.empty = true;
                    }
                }
                catch (Exception)
                {
                }
            }

            int level = GetBatteryLevel();
            if (level == 0 && _batteryLevel.Value > 0)
            {
                PlaySpinDown();
                if (recording) RequestStopRecording();
                if (nightVision) RequestLightsOff();
            }
            TrySetBatteryLevel(level);
        }
    }

    private void RequestLightsOff()
    {
        if (IsServer)
        {
            if (_nightVisionOn.Value) TrySetNightVision(false);
            return;
        }

        if (!IsSpawned || NetworkObject == null || !NetworkObject.IsSpawned) return;

        try { RequestLightsOffServerRpc(); }
        catch (Exception ex) { Plugin.Log.LogDebug($"Failed to request lights off: {ex.Message}"); }
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestLightsOffServerRpc()
    {
        if (_nightVisionOn.Value) TrySetNightVision(false);
    }

    private static float ApplyLithiumBatteriesRate(float drain) => MoreShipUpgradesCompat.ApplyLithiumBatteriesRate(drain);
}
