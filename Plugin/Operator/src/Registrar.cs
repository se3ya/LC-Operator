using Dawn;
using Unity.Netcode;
using UnityEngine;
using Operator.Recording;

namespace Operator;

internal static class VideoCameraRegistrar
{
    private static readonly NamespacedKey<DawnItemInfo> ItemKey =
        NamespacedKey<DawnItemInfo>.From(Constants.PluginMetadata.ContentNamespace, Constants.PluginMetadata.ItemKey);

    public static void Register()
    {
        if (!AssetsLoader.TryLoadBundle())
        {
            return;
        }

        Item? item = AssetsLoader.CameraItem;
        if (item == null)
        {
            return;
        }

        item.floorYOffset = Constants.Camera.FloorYOffset;

        if (item.spawnPrefab == null)
        {
            Plugin.Log.LogError("Camcorder item has no spawnPrefab. Camcorder is disabled.");
            return;
        }

        RegisterPrefab(item.spawnPrefab);

        _ = DawnLib.DefineItem(ItemKey, item, builder =>
        {
            builder.DefineShop(shopBuilder =>
            {
                shopBuilder.OverrideCost(new Dawn.FuncProvider<int>(() => StoreCostSync.Cost));
            });
        });

        Plugin.Log.LogInfo("Registered Camcorder item.");
    }

    private static void RegisterPrefab(GameObject itemPrefab)
    {
        if (itemPrefab.TryGetComponent(out NetworkObject networkObject))
        {
            networkObject.AutoObjectParentSync = false;
        }
        else
        {
            Plugin.Log.LogWarning("Camcorder prefab has no NetworkObject.");
        }

        DawnLib.RegisterNetworkPrefab(itemPrefab);
    }
}
