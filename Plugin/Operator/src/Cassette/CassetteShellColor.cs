using System;
using UnityEngine;

namespace Operator.Cassette;

internal enum CassetteTier
{
    Common,
    Uncommon,
    Rare,
    Legendary,
    Gold,
    Holo,
}

internal static class CassetteShellColor
{
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int BaseMapStId = Shader.PropertyToID("_BaseColorMap_ST");
    private static readonly int MetallicId = Shader.PropertyToID("_Metallic");
    private static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");
    private static readonly MaterialPropertyBlock Block = new();

    public static void Apply(Transform root, string cassetteId, int value)
    {
        Renderer? target = FindColorRenderer(root);
        if (target == null) return;

        string id = cassetteId ?? string.Empty;
        CassetteTier tier = GetTier(value);
        bool misprint = IsMisprint(id);
        SetBand(target, tier, misprint, GetColor(id, value));

        CassetteShimmer? shimmer = target.GetComponent<CassetteShimmer>();
        if (tier == CassetteTier.Holo)
        {
            if (shimmer == null) shimmer = target.gameObject.AddComponent<CassetteShimmer>();
            shimmer.Init(misprint, (Mix(Fnv1a(id)) & 0xFFFF) / 65536f);
        }
        else if (shimmer != null)
        {
            shimmer.enabled = false;
        }
    }

    public static void Clear(Transform root)
    {
        Renderer? target = FindColorRenderer(root);
        if (target == null) return;

        CassetteShimmer? shimmer = target.GetComponent<CassetteShimmer>();
        if (shimmer != null) shimmer.enabled = false;
        target.SetPropertyBlock(null);
    }

    internal static void SetBand(Renderer target, CassetteTier tier, bool misprint, Color color)
    {
        int rows = Constants.Cassette.StripeRows;
        float inset = Constants.Cassette.StripeRowInset;
        float shift = 0f;
        if (misprint)
        {
            shift = tier == CassetteTier.Common ? -Constants.Cassette.MisprintRowShift : Constants.Cassette.MisprintRowShift;
        }

        var st = new Vector4(1f, (1f - (2f * inset)) / rows, 0f, (rows - 1 - (int)tier + inset + shift) / rows);
        target.GetPropertyBlock(Block);
        Block.SetColor(BaseColorId, color);
        Block.SetColor(ColorId, color);
        Block.SetVector(BaseMapStId, st);
        Block.SetFloat(MetallicId, tier switch
        {
            CassetteTier.Gold => Constants.Cassette.GoldMetallic,
            CassetteTier.Holo => Constants.Cassette.HoloMetallic,
            _ => 0f,
        });
        Block.SetFloat(SmoothnessId, tier switch
        {
            CassetteTier.Gold => Constants.Cassette.GoldSmoothness,
            CassetteTier.Holo => Constants.Cassette.HoloSmoothness,
            _ => Constants.Cassette.StripeSmoothness,
        });
        target.SetPropertyBlock(Block);
    }

    private static Renderer? FindColorRenderer(Transform root)
    {
        if (root == null) return null;
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (child != null && string.Equals(child.name, Constants.Cassette.ColorObjectName, StringComparison.OrdinalIgnoreCase))
            {
                return child.GetComponent<Renderer>();
            }
        }
        return null;
    }

    private static int HueBucket(string cassetteId)
        => (int)(Fnv1a(cassetteId ?? string.Empty) % (uint)Constants.Cassette.ColorHueBuckets);

    public static bool IsMisprint(string cassetteId)
        => Mix(Fnv1a(cassetteId ?? string.Empty)) % (uint)Constants.Cassette.MisprintChance == 0;

    public static CassetteTier GetTier(int value)
    {
        if (value >= Constants.Cassette.ColorHoloValue) return CassetteTier.Holo;
        if (value >= Constants.Cassette.ColorGoldValue) return CassetteTier.Gold;
        if (value >= Constants.Cassette.ColorLegendaryValue) return CassetteTier.Legendary;
        if (value >= Constants.Cassette.ColorRareValue) return CassetteTier.Rare;
        if (value >= Constants.Cassette.ColorUncommonValue) return CassetteTier.Uncommon;
        return CassetteTier.Common;
    }

    public static string GetVariantKey(string cassetteId, int value)
    {
        if (IsMisprint(cassetteId)) return Constants.Cassette.IconMisprintKey;

        switch (GetTier(value))
        {
            case CassetteTier.Holo: return Constants.Cassette.IconHoloKey;
            case CassetteTier.Gold: return Constants.Cassette.IconGoldKey;
            case CassetteTier.Legendary: return Constants.Cassette.IconLegendaryKey;
            case CassetteTier.Rare: return "r" + HueBucket(cassetteId).ToString("00");
            case CassetteTier.Uncommon: return "u" + HueBucket(cassetteId).ToString("00");
            default: return "c" + HueBucket(cassetteId).ToString("00");
        }
    }

    private static Color GetColor(string cassetteId, int value)
    {
        float hue = HueBucket(cassetteId) / (float)Constants.Cassette.ColorHueBuckets;
        return GetTier(value) switch
        {
            CassetteTier.Rare => Color.HSVToRGB(hue, 0.95f, 0.95f),
            CassetteTier.Uncommon => Color.HSVToRGB(hue, 0.7f, 0.85f),
            CassetteTier.Common => Color.HSVToRGB(hue, 0.35f, 0.6f),
            _ => Color.white,
        };
    }

    private static uint Mix(uint hash)
    {
        hash ^= hash >> 16;
        hash *= 0x85ebca6bu;
        hash ^= hash >> 13;
        return hash;
    }

    private static uint Fnv1a(string text)
    {
        uint hash = 2166136261u;
        for (int i = 0; i < text.Length; i++)
        {
            hash ^= text[i];
            hash *= 16777619u;
        }
        return hash;
    }
}
