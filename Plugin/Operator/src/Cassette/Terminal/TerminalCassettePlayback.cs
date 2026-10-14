using System;
using System.Collections.Generic;
using UnityEngine;

namespace Operator.Cassette;

internal static partial class TerminalCassettePlayback
{
    private static readonly string EvtPlay = Constants.Terminal.EventPlay;
    private static readonly string EvtStop = Constants.Terminal.EventStop;
    private static readonly string EvtSave = Constants.Terminal.EventSave;
    private static readonly string EvtDelete = Constants.Terminal.EventDelete;
    private static readonly string EvtList = Constants.Terminal.EventList;
    private static readonly string EvtHelp = Constants.Terminal.EventHelp;
    private static readonly string EvtSlow = Constants.Terminal.EventSlow;
    private static readonly string EvtNormal = Constants.Terminal.EventNormal;
    private static readonly string EvtFast = Constants.Terminal.EventFast;
    private static readonly string EvtTurbo = Constants.Terminal.EventTurbo;
    private static readonly string EvtPause = Constants.Terminal.EventPause;
    private static readonly string EvtResume = Constants.Terminal.EventResume;
    private static readonly string EvtVolume = Constants.Terminal.EventVolume;
    private static readonly string EvtTransfer = Constants.Terminal.EventTransfer;
    private const string DefaultHelpKeyword = "ohelp";
    private const string AudioName = "VideoCamTerminalPlaybackAudio";
    private static bool _helpKeywordFallbackLogged;

    private static bool _keywordsRegistered;
    private static readonly Dictionary<string, TerminalNode> _ourNodes = new();

    private static readonly HashSet<string> _registeredMatchKeys = new(StringComparer.Ordinal);
    private static TerminalNode? _genericTapeInfoNode;
    private static Terminal? _activeTerminal;
    private static readonly List<string> _pendingMatchKeys = new();

    private static TerminalKeyword? _playVerbKw;
    private static TerminalKeyword? _saveVerbKw;
    private static TerminalKeyword? _deleteVerbKw;
    private static TerminalKeyword? _volumeVerbKw;

    private static string[] _lastTokens = Array.Empty<string>();

    private static bool _isPlaying;

    internal static bool IsPlaying => _isPlaying;

    private static bool _joinedLate;

    private static bool _pendingPlay;

    private static bool _userPaused;

    private static string _currentPlaybackUrl = string.Empty;

    private static double _pendingSeekSeconds = -1.0;

    private static void SetupHandler(Terminal terminal)
    {
        if (terminal == null) return;
        TerminalPlaybackHandler? driver = terminal.GetComponent<TerminalPlaybackHandler>();
        if (driver == null)
        {
            driver = terminal.gameObject.AddComponent<TerminalPlaybackHandler>();
        }
        driver.Bind(terminal);
    }

    internal static void SetupHandlerForMonitor()
    {
        Terminal? terminal = GetActiveTerminal();
        if (terminal != null) SetupHandler(terminal);
    }

    private static float _sessionPlaybackVolume = 1.0f;
    private static AudioSource? _playbackAudioSource;

    private static string _pendingReceiveFileName = string.Empty;
    private static readonly ClipWait _pendingWait = new();
    private static string _lastStatusText = string.Empty;

    internal static bool IsWaitingForFile => _pendingReceiveFileName.Length > 0;
}
