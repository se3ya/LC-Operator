using HarmonyLib;
using Operator.Cassette;

namespace Operator.Patches;

[HarmonyPatch(typeof(Terminal), nameof(Terminal.Awake))]
internal static class TerminalAwakePatch
{
    private static void Postfix() => TerminalCassettePlayback.OnTerminalAwake();
}

[HarmonyPatch(typeof(Terminal), nameof(Terminal.Start))]
internal static class TerminalStartPatch
{
    private static void Postfix(Terminal __instance)
    {
        TerminalCassettePlayback.RegisterKeywords(__instance);
        TerminalCassettePlayback.InitPose(__instance);
    }
}

[HarmonyPatch(typeof(Terminal), nameof(Terminal.ParsePlayerSentence))]
internal static class TerminalParsePatch
{
    private static void Prefix(Terminal __instance) => TerminalCassettePlayback.OnParsePlayerSentence(__instance);
}

[HarmonyPatch(typeof(Terminal), nameof(Terminal.LoadNewNode))]
internal static class TerminalNodePatch
{
    private static void Prefix(Terminal __instance, TerminalNode node)
        => TerminalCassettePlayback.OnLoadNewNode(__instance, node);
}

[HarmonyPatch(typeof(Terminal), nameof(Terminal.RunTerminalEvents))]
internal static class TerminalEventsPatch
{
    private static void Postfix(Terminal __instance, TerminalNode node)
        => TerminalCassettePlayback.OnRunTerminalEvent(__instance, node);
}

[HarmonyPatch(typeof(Terminal), nameof(Terminal.QuitTerminal))]
internal static class TerminalQuitPatch
{
    private static void Postfix(Terminal __instance) => TerminalCassettePlayback.OnQuitTerminal(__instance);
}

[HarmonyPatch(typeof(Terminal), nameof(Terminal.LoadTerminalImage))]
internal static class TerminalImagePatch
{
    private static bool Prefix(TerminalNode node)
        => !TerminalCassettePlayback.ShouldSkipImage(node);

    private static void Postfix(Terminal __instance, TerminalNode node)
        => TerminalCassettePlayback.OnLoadTerminalImage(__instance, node);
}
