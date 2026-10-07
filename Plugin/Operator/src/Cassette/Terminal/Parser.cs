using System;
using UnityEngine;

namespace Operator.Cassette;

internal static partial class TerminalCassettePlayback
{
    internal static void OnParsePlayerSentence(Terminal terminal)
    {
        try
        {
            if (terminal == null || terminal.screenText == null) return;
            string fullText = terminal.screenText.text ?? string.Empty;
            int added = Mathf.Clamp(terminal.textAdded, 0, fullText.Length);
            if (added <= 0) { _lastTokens = Array.Empty<string>(); return; }

            string typed = fullText.Substring(fullText.Length - added).Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(typed)) { _lastTokens = Array.Empty<string>(); return; }

            _lastTokens = typed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogDebug($"Failed to read typed terminal line: {ex.Message}");
            _lastTokens = Array.Empty<string>();
        }
    }

    private static string? ArgAfterVerb()
    {
        return _lastTokens.Length >= 2 ? _lastTokens[1] : null;
    }

}
