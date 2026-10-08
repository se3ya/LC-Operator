using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Operator.Cassette;

internal static partial class TerminalCassettePlayback
{
    internal static void OnTerminalAwake()
    {
        _keywordsRegistered = false;
        _ourNodes.Clear();
        _registeredMatchKeys.Clear();
        _genericTapeInfoNode = null;
        _activeTerminal = null;
        _playVerbKw = null;
        _saveVerbKw = null;
        _deleteVerbKw = null;
        _volumeVerbKw = null;
        _helpKeywordFallbackLogged = false;
        _pendingPlay = false;
        _isPlaying = false;
        _userPaused = false;
        _pendingSeekSeconds = -1.0;
        _sessionPlaybackVolume = 1.0f;
        _playbackAudioSource = null;
        DestroyOverlay();
    }

    internal static void RegisterKeywords(Terminal terminal)
    {
        if (terminal == null || terminal.terminalNodes == null) return;
        if (_keywordsRegistered) return;
        _keywordsRegistered = true;

        try
        {
            string helpKeyword = GetHelpKeyword();

            var pending = new List<(string Word, string EventId)>
            {
                (Constants.Terminal.KeywordPlay,   EvtPlay),
                (Constants.Terminal.KeywordStop,   EvtStop),
                (Constants.Terminal.KeywordSave,   EvtSave),
                (Constants.Terminal.KeywordDelete, EvtDelete),
                (Constants.Terminal.KeywordList,   EvtList),
                (helpKeyword, EvtHelp),
                (Constants.Terminal.KeywordSlow,   EvtSlow),
                (Constants.Terminal.KeywordNormal, EvtNormal),
                (Constants.Terminal.KeywordFast,   EvtFast),
                (Constants.Terminal.KeywordTurbo,  EvtTurbo),
                (Constants.Terminal.KeywordPause,  EvtPause),
                (Constants.Terminal.KeywordResume, EvtResume),
                (Constants.Terminal.KeywordVolume, EvtVolume),
            };

            if (ShipMonitors.Available)
            {
                pending.Add((Constants.Terminal.KeywordTransfer, EvtTransfer));
            }

            var existing = terminal.terminalNodes.allKeywords;
            int oldLen = existing.Length;

            var existingByWord = new Dictionary<string, TerminalKeyword>(StringComparer.OrdinalIgnoreCase);
            foreach (var k in existing)
            {
                if (k == null) continue;
                string w = k.word ?? string.Empty;
                if (!existingByWord.ContainsKey(w)) existingByWord[w] = k;
            }

            var newKws = new List<TerminalKeyword>();
            int readopted = 0;
            foreach (var (word, eventId) in pending)
            {
                if (existingByWord.TryGetValue(word, out var existingKw))
                {
                    bool isOurs = (existingKw.name ?? string.Empty)
                            .StartsWith("VideoCamCassetteKeyword_", StringComparison.Ordinal)
                        && existingKw.specialKeywordResult != null;
                    if (isOurs)
                    {
                        var adoptedNode = existingKw.specialKeywordResult!;
                        adoptedNode.terminalEvent = eventId;
                        _ourNodes[eventId] = adoptedNode;
                        if (eventId == EvtPlay) _playVerbKw = existingKw;
                        if (eventId == EvtSave) _saveVerbKw = existingKw;
                        if (eventId == EvtDelete) _deleteVerbKw = existingKw;
                        if (eventId == EvtVolume) _volumeVerbKw = existingKw;
                        readopted++;
                    }
                    else
                    {
                        Plugin.Log.LogWarning($"Terminal keyword '{word}' belongs to another mod, that cassette command is unavailable.");
                    }
                    continue;
                }

                var node = ScriptableObject.CreateInstance<TerminalNode>();
                node.name = eventId == EvtPlay
                    ? "VideoCamCassettePlayNode"
                    : "VideoCamCassetteNode_" + eventId;
                node.displayText = string.Empty;
                node.clearPreviousText = true;
                node.terminalEvent = eventId;

                bool isVerb = eventId == EvtPlay || eventId == EvtSave || eventId == EvtDelete || eventId == EvtVolume || eventId == EvtTransfer;

                var kw = ScriptableObject.CreateInstance<TerminalKeyword>();
                kw.name = "VideoCamCassetteKeyword_" + word;
                kw.word = word;
                kw.isVerb = isVerb;
                kw.accessTerminalObjects = false;
                kw.compatibleNouns = Array.Empty<CompatibleNoun>();
                if (isVerb)
                {
                    kw.defaultVerb = kw;
                    kw.specialKeywordResult = node;
                }
                else
                {
                    kw.defaultVerb = null;
                    kw.specialKeywordResult = node;
                }

                if (eventId == EvtPlay) _playVerbKw = kw;
                if (eventId == EvtSave) _saveVerbKw = kw;
                if (eventId == EvtDelete) _deleteVerbKw = kw;
                if (eventId == EvtVolume) _volumeVerbKw = kw;

                newKws.Add(kw);
                _ourNodes[eventId] = node;
                existingByWord[word] = kw;
            }

            if (newKws.Count > 0)
            {
                Array.Resize(ref existing, oldLen + newKws.Count);
                for (int i = 0; i < newKws.Count; i++) existing[oldLen + i] = newKws[i];
                terminal.terminalNodes.allKeywords = existing;
            }
            if (readopted > 0)
            {
                Plugin.Log.LogDebug($"Reused {readopted} cassette terminal keywords from previous session.");
            }

            if (_ourNodes.Count == 0)
            {
                Plugin.Log.LogError("Could not register any cassette terminal commands.");
                return;
            }

            _activeTerminal = terminal;
            RegisterVolumeNouns();
            _genericTapeInfoNode = ScriptableObject.CreateInstance<TerminalNode>();
            _genericTapeInfoNode.name = "VideoCamCassetteIdInfoNode";
            _genericTapeInfoNode.displayText =
                $"\n>CASSETTE ID RECOGNISED.\n" +
                $" Type '{Constants.Terminal.KeywordPlay} <ID>' to play it on terminal screen.\n" +
                $" Type '{Constants.Terminal.KeywordSave} <ID>' to save it to library.\n" +
                $" Type '{Constants.Terminal.KeywordDelete} <ID>' to remove it from library.\n\n";
            _genericTapeInfoNode.clearPreviousText = true;
            _genericTapeInfoNode.terminalEvent = string.Empty;

            if (_pendingMatchKeys.Count > 0)
            {
                foreach (string key in _pendingMatchKeys)
                {
                    RegisterMatchKey(key);
                }
                _pendingMatchKeys.Clear();
            }

            AddHelpEntry(terminal);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"Failed to register cassette terminal commands: {ex}");
        }
    }

    public static void RegisterCassetteId(string displayId)
    {
        string matchKey = CassetteIdGenerator.ToMatchKey(displayId);
        if (string.IsNullOrEmpty(matchKey)) return;
        if (_registeredMatchKeys.Contains(matchKey)) return;

        if (_activeTerminal == null || _activeTerminal.terminalNodes == null)
        {
            if (!_pendingMatchKeys.Contains(matchKey)) _pendingMatchKeys.Add(matchKey);
            return;
        }
        RegisterMatchKey(matchKey);
    }

    private static void RegisterMatchKey(string matchKey)
    {
        if (_registeredMatchKeys.Contains(matchKey)) return;
        if (_activeTerminal == null || _activeTerminal.terminalNodes == null) return;

        try
        {
            var allKeywords = _activeTerminal.terminalNodes.allKeywords;
            TerminalKeyword? nounKw = null;

            foreach (var existingKw in allKeywords)
            {
                if (existingKw != null && string.Equals(existingKw.word, matchKey, StringComparison.OrdinalIgnoreCase))
                {
                    nounKw = existingKw;
                    break;
                }
            }

            if (nounKw == null)
            {
                nounKw = ScriptableObject.CreateInstance<TerminalKeyword>();
                nounKw.name = "VideoCamCassetteIdKeyword_" + matchKey;
                nounKw.word = matchKey;
                nounKw.isVerb = false;
                nounKw.defaultVerb = null;
                nounKw.accessTerminalObjects = false;
                nounKw.specialKeywordResult = _genericTapeInfoNode;
                nounKw.compatibleNouns = Array.Empty<CompatibleNoun>();

                Array.Resize(ref allKeywords, allKeywords.Length + 1);
                allKeywords[allKeywords.Length - 1] = nounKw;
                _activeTerminal.terminalNodes.allKeywords = allKeywords;
            }

            if (_playVerbKw != null && _ourNodes.TryGetValue(EvtPlay, out var playResult))
            {
                AppendCompatibleNoun(_playVerbKw, nounKw, playResult);
            }
            if (_saveVerbKw != null && _ourNodes.TryGetValue(EvtSave, out var saveResult))
            {
                AppendCompatibleNoun(_saveVerbKw, nounKw, saveResult);
            }
            if (_deleteVerbKw != null && _ourNodes.TryGetValue(EvtDelete, out var deleteResult))
            {
                AppendCompatibleNoun(_deleteVerbKw, nounKw, deleteResult);
            }

            _registeredMatchKeys.Add(matchKey);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"Failed to register cassette terminal keyword: {ex}");
        }
    }

    private static void AppendCompatibleNoun(TerminalKeyword verb, TerminalKeyword noun, TerminalNode result)
    {
        var existing = verb.compatibleNouns ?? Array.Empty<CompatibleNoun>();
        foreach (var cn in existing)
        {
            if (cn != null && cn.noun == noun) return;
        }
        var appended = new CompatibleNoun(noun, result);
        Array.Resize(ref existing, existing.Length + 1);
        existing[existing.Length - 1] = appended;
        verb.compatibleNouns = existing;
    }

    private static void RegisterVolumeNouns()
    {
        if (_activeTerminal == null || _activeTerminal.terminalNodes == null) return;
        if (_volumeVerbKw == null) return;
        if (!_ourNodes.TryGetValue(EvtVolume, out var resultNode)) return;

        var allKeywords = _activeTerminal.terminalNodes.allKeywords;
        for (int percent = 0; percent <= 100; percent++)
        {
            string word = percent.ToString();
            TerminalKeyword? nounKeyword = null;
            foreach (var existingKeyword in allKeywords)
            {
                if (existingKeyword != null &&
                    string.Equals(existingKeyword.word, word, StringComparison.OrdinalIgnoreCase))
                {
                    nounKeyword = existingKeyword;
                    break;
                }
            }

            if (nounKeyword == null)
            {
                nounKeyword = ScriptableObject.CreateInstance<TerminalKeyword>();
                nounKeyword.name = "VideoCamCassetteVolumePercent_" + word;
                nounKeyword.word = word;
                nounKeyword.isVerb = false;
                nounKeyword.defaultVerb = null;
                nounKeyword.accessTerminalObjects = false;
                nounKeyword.specialKeywordResult = resultNode;
                nounKeyword.compatibleNouns = Array.Empty<CompatibleNoun>();

                Array.Resize(ref allKeywords, allKeywords.Length + 1);
                allKeywords[allKeywords.Length - 1] = nounKeyword;
            }

            AppendCompatibleNoun(_volumeVerbKw, nounKeyword, resultNode);
        }

        _activeTerminal.terminalNodes.allKeywords = allKeywords;
    }

    private static string KeywordOrDefault(string? configured, string fallback)
    {
        string s = (configured ?? string.Empty).Trim().ToLowerInvariant();
        return string.IsNullOrEmpty(s) ? fallback : s;
    }

    private static string GetHelpKeyword()
    {
        string configured = KeywordOrDefault(VideoCameraConfig.TerminalHelpKeyword.Value, DefaultHelpKeyword);
        if (!string.Equals(configured, "help", StringComparison.OrdinalIgnoreCase))
        {
            return configured;
        }

        if (!_helpKeywordFallbackLogged)
        {
            _helpKeywordFallbackLogged = true;
        }
        return DefaultHelpKeyword;
    }

    private static void AddHelpEntry(Terminal terminal)
    {
        TerminalNode? otherNode = FindOtherCommandsNode(terminal);
        if (otherNode == null)
        {
            return;
        }

        string helpKeyword = GetHelpKeyword();
        string commandLine = $">{helpKeyword.ToUpperInvariant()}";
        string descriptionLine = "To view cassette commands.";
        string text = otherNode.displayText ?? string.Empty;

        if (ContainsIgnoreCase(text, commandLine)
            || (ContainsIgnoreCase(text, helpKeyword)
                && (ContainsIgnoreCase(text, "operator cassette commands")
                    || ContainsIgnoreCase(text, "video camera cassette commands"))))
        {
            return;
        }

        if (!string.IsNullOrEmpty(text) && !text.EndsWith('\n'))
        {
            text += "\n";
        }

        if (!string.IsNullOrEmpty(text) && !text.EndsWith("\n\n", StringComparison.Ordinal))
        {
            text += "\n";
        }

        otherNode.displayText = text + commandLine + "\n" + descriptionLine + "\n";
    }

    private static TerminalNode? FindOtherCommandsNode(Terminal terminal)
    {
        var allKeywords = terminal.terminalNodes?.allKeywords;
        if (allKeywords != null)
        {
            foreach (TerminalKeyword? keyword in allKeywords)
            {
                if (keyword == null || keyword.specialKeywordResult == null)
                {
                    continue;
                }

                if (string.Equals(keyword.word, "other", StringComparison.OrdinalIgnoreCase))
                {
                    return keyword.specialKeywordResult;
                }
            }
        }

        var specialNodes = terminal.terminalNodes?.specialNodes;
        if (specialNodes != null)
        {
            foreach (TerminalNode? node in specialNodes)
            {
                if (node == null) continue;
                string text = node.displayText ?? string.Empty;
                if (ContainsIgnoreCase(text, "other commands"))
                {
                    return node;
                }
            }
        }

        return null;
    }

    private static bool ContainsIgnoreCase(string text, string value)
    {
        return text.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
