using UnityEngine;

namespace Operator.Cassette;

internal static class ScreenMaterial
{
    internal const string Name = "OperatorMonitorVideo";

    internal static readonly string[] ColorMapProperties = { "_BaseColorMap", "_BaseMap", "_UnlitColorMap", "_MainTex" };
    internal static readonly string[] EmissiveMapProperties = { "_EmissiveColorMap", "_EmissionMap" };
    internal static readonly string[] EmissiveColorProperties = { "_EmissiveColor", "_EmissionColor" };
    internal static readonly string[] BaseColorProperties = { "_BaseColor", "_UnlitColor", "_Color" };

    internal static Material CreateScreen(Material source, Texture texture)
    {
        Shader? unlit = Shader.Find("HDRP/Unlit");
        if (unlit == null)
        {
            Plugin.Log.LogDebug("HDRP/Unlit is missing, monitor falls back to lit screen.");
            Material lit = new Material(source) { name = Name };
            SetPicture(lit, texture);
            return lit;
        }

        Material screen = new Material(unlit) { name = Name };
        ApplyScreenTone(screen);
        SetPicture(screen, texture);
        return screen;
    }

    private static void ApplyScreenTone(Material screen)
    {
        foreach (string baseColor in BaseColorProperties)
        {
            if (screen.HasProperty(baseColor)) screen.SetColor(baseColor, Color.white);
        }

        foreach (string emissiveMap in EmissiveMapProperties)
        {
            if (screen.HasProperty(emissiveMap)) screen.SetTexture(emissiveMap, null);
        }

        foreach (string emissiveColor in EmissiveColorProperties)
        {
            if (screen.HasProperty(emissiveColor)) screen.SetColor(emissiveColor, Color.black);
        }
    }

    private static void SetPicture(Material material, Texture? texture)
    {
        material.mainTexture = texture;

        foreach (string colorMap in ColorMapProperties)
        {
            if (material.HasProperty(colorMap)) material.SetTexture(colorMap, texture);
        }
    }

    internal static void ApplyTexture(Material material, Texture? texture) => SetPicture(material, texture);

    internal static void RestoreTexture(Material material, Texture? liveTexture) => SetPicture(material, liveTexture);

    internal static void SetOn(Renderer renderer, int index, Material material)
    {
        Material[] materials = renderer.sharedMaterials;
        if (materials == null || index < 0 || index >= materials.Length) return;
        materials[index] = material;
        renderer.sharedMaterials = materials;
    }
}
