using Operator.Recording;
using UnityEngine;

namespace Operator;

internal enum RecordingBlock
{
    None,
    Disabled,
    NoCamera,
    Preparing,
    Unavailable,
}

internal sealed partial class VideoCameraItem
{
    private RecordingBlock _blockTipShown;
    private bool _lastRecordingVisual;

    private void TickFilmingVisuals()
    {
        bool filming = _recordingState.Value;
        if (filming == _lastRecordingVisual) return;

        _lastRecordingVisual = filming;
        ApplyRecordingVisuals(filming);
        RefreshTooltips();
    }

    private static RecordingBlock GetRecordingBlock(Camera? camera)
    {
        if (!VideoCameraConfig.RecordingEnabled.Value) return RecordingBlock.Disabled;
        if (camera == null || camera.targetTexture == null) return RecordingBlock.NoCamera;
        if (FfmpegLocator.IsReady) return RecordingBlock.None;
        return FfmpegLocator.GaveUp ? RecordingBlock.Unavailable : RecordingBlock.Preparing;
    }

    private void ShowRecordingBlockTip(RecordingBlock block)
    {
        if (block == RecordingBlock.None || block == _blockTipShown) return;
        if (playerHeldBy == null || !playerHeldBy.IsOwner || playerHeldBy.isPlayerDead) return;

        HUDManager? hud = HUDManager.Instance;
        if (hud == null) return;

        _blockTipShown = block;
        switch (block)
        {
            case RecordingBlock.Preparing:
                hud.DisplayTip("Standby", "Recorder is still preparing. Filming will be available shortly.");
                break;
            case RecordingBlock.Unavailable:
                hud.DisplayTip("Recorder offline", "Recording hardware could not be prepared. Restart the game to restore filming.", true);
                break;
            case RecordingBlock.Disabled:
                hud.DisplayTip("Recorder offline", "Recording is switched off in this machines configuration.", true);
                break;
            default:
                hud.DisplayTip("Recording failed!", "Camera could not begin a tape. Nothing is being filmed.", true);
                break;
        }
    }

    internal void ResetRecordingBlockTip() => _blockTipShown = RecordingBlock.None;
}
