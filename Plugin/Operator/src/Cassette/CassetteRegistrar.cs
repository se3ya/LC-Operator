using Dawn;
using Unity.Netcode;
using UnityEngine;

namespace Operator.Cassette;

internal static class CassetteRegistrar
{
    private static readonly NamespacedKey<DawnItemInfo> ItemKey =
        NamespacedKey<DawnItemInfo>.From(Constants.PluginMetadata.ContentNamespace, Constants.Cassette.ItemKey);

    public static Item? CassetteItemSO { get; private set; }

    public static GameObject? CassettePrefab { get; private set; }

    public static void Register()
    {
        if (!AssetsLoader.TryLoadBundle())
        {
            return;
        }

        Item? item = AssetsLoader.LoadOptionalAsset<Item>(Constants.Cassette.ItemAssetName);
        if (item == null)
        {
            Plugin.Log.LogError($"Cassette item '{Constants.Cassette.ItemAssetName}' is missing from the bundle. Cassettes are disabled.");
            return;
        }

        GameObject? prefab = item.spawnPrefab;
        if (prefab == null)
        {
            prefab = AssetsLoader.LoadOptionalAsset<GameObject>(Constants.Cassette.PrefabAssetName);
            if (prefab != null)
            {
                item.spawnPrefab = prefab;
            }
        }

        if (prefab == null)
        {
            Plugin.Log.LogError("Cassette spawn prefab is missing. Cassettes are disabled.");
            return;
        }

        if (prefab.TryGetComponent(out NetworkObject networkObject))
        {
            networkObject.AutoObjectParentSync = false;
        }
        else
        {
            Plugin.Log.LogWarning("Cassette prefab has no NetworkObject.");
        }

        int propsLayer = LayerMask.NameToLayer("Props");
        if (propsLayer >= 0)
        {
            prefab.layer = propsLayer;
        }

        item.isScrap = true;
        item.minValue = Mathf.Max(1, VideoCameraConfig.FlatBaseValue.Value);
        item.maxValue = Mathf.Max(item.minValue + 1, VideoCameraConfig.FlatBaseValue.Value * 10);
        item.saveItemVariable = true;
        item.canBeGrabbedBeforeGameStart = true;

        _ = DawnLib.DefineItem(ItemKey, item, _ => { });
        DawnLib.RegisterNetworkPrefab(prefab);

        CassetteItemSO = item;
        CassettePrefab = prefab;
        Plugin.Log.LogInfo("Registered Cassette item.");
    }
}
