using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Operator;

internal sealed partial class VideoCameraItem
{
    private DepthOfField? _nearBlur;
    private bool _nearBlurShort;

    private void UpdateNearBlur()
    {
        var local = StartOfRound.Instance != null ? StartOfRound.Instance.localPlayerController : null;
        bool holding = local != null && playerHeldBy == local && !isPocketed && !local.isPlayerDead;
        if (!holding)
        {
            if (_nearBlurShort) RestoreNearBlur();
            return;
        }

        HUDManager hud = HUDManager.Instance;
        if (hud == null || hud.playerGraphicsVolume == null) return;
        if (_nearBlur == null)
        {
            VolumeProfile profile = hud.playerGraphicsVolume.sharedProfile;
            if (profile == null) return;
            if (!profile.TryGet(out DepthOfField found)) return;
            _nearBlur = found;
        }

        _nearBlurShort = true;
        if (_nearBlur.nearFocusEnd.value > Constants.Camera.HeldNearBlurEnd)
        {
            hud.SetNearDepthOfFieldEnabled(false);
        }
    }

    private void RestoreNearBlur()
    {
        _nearBlurShort = false;
        var local = StartOfRound.Instance != null ? StartOfRound.Instance.localPlayerController : null;
        if (local != null && local.IsInspectingItem) return;
        if (HUDManager.Instance != null) HUDManager.Instance.SetNearDepthOfFieldEnabled(true);
    }
}
