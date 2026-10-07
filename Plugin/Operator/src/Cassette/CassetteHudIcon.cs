using GameNetcodeStuff;
using UnityEngine;
using UnityEngine.UI;

namespace Operator.Cassette;

internal static class CassetteHudIcon
{
    public static void ApplySlotIcon(PlayerControllerB player, int slot, GrabbableObject item)
    {
        if (player == null || !player.IsOwner) return;
        if (item is not CassetteItem cassette) return;

        Image? icon = GetSlotIcon(slot);
        if (icon == null) return;

        Sprite? baked = CassetteIconLibrary.GetIcon(cassette.CassetteId, cassette.scrapValue);
        if (baked == null) return;

        icon.sprite = baked;
    }

    public static void Refresh(CassetteItem cassette)
    {
        if (cassette == null) return;

        PlayerControllerB? local = GameNetworkManager.Instance != null
            ? GameNetworkManager.Instance.localPlayerController
            : null;
        if (local == null || cassette.playerHeldBy != local) return;

        GrabbableObject[] slots = local.ItemSlots;
        if (slots != null)
        {
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] == cassette) ApplySlotIcon(local, i, cassette);
            }
        }

        if (local.ItemOnlySlot == cassette)
        {
            ApplySlotIcon(local, Constants.Cassette.HudUtilitySlotIndex, cassette);
        }
    }

    private static Image? GetSlotIcon(int slot)
    {
        HUDManager hud = HUDManager.Instance;
        if (hud == null) return null;

        if (slot == Constants.Cassette.HudUtilitySlotIndex) return hud.itemOnlySlotIcon;

        Image[] icons = hud.itemSlotIcons;
        return icons != null && slot >= 0 && slot < icons.Length ? icons[slot] : null;
    }
}
