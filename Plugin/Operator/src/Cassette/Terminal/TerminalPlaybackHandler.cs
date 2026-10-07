using UnityEngine;

namespace Operator.Cassette;

internal sealed class TerminalPlaybackHandler : MonoBehaviour
{
    private Terminal? _terminal;

    public void Bind(Terminal terminal) => _terminal = terminal;

    private double _lastFrameEnd;
    private double _nextHitchLog;

    private void LateUpdate()
    {
        if (_terminal == null)
        {
            Destroy(this);
            return;
        }

        double start = Time.realtimeSinceStartupAsDouble;

        TerminalCoWatch.Tick();
        MonitorPlayback.Tick();
        TickPendingReceive(_terminal);

        if (IsIdle())
        {
            Destroy(this);
            return;
        }

        RefreshPlayback(_terminal);
        ReportHitch(start);
    }

    private void ReportHitch(double start)
    {
        double end = Time.realtimeSinceStartupAsDouble;
        double elsewhere = _lastFrameEnd > 0.0 ? start - _lastFrameEnd : 0.0;
        _lastFrameEnd = end;

        if (elsewhere * 1000.0 < Constants.Terminal.PlaybackHitchThreshold) return;
        if (end < _nextHitchLog) return;
        _nextHitchLog = end + Constants.Terminal.PlaybackHitchLogCooldown;

        Plugin.Log.LogDebug(
            $"Playback hitch, {elsewhere * 1000.0:0} ms outside our work, {(end - start) * 1000.0:0.0} ms inside it, "
            + $"playing {TerminalCassettePlayback.IsPlaying}, monitor {MonitorPlayback.IsActive}, "
            + $"preparing {TerminalCoWatch.IsPreparingLocally}.");
    }

    private static void TickPendingReceive(Terminal terminal)
    {
        if (!TerminalCassettePlayback.IsWaitingForFile) return;
        TerminalCassettePlayback.TickPendingReceive(terminal);
    }

    private static bool IsIdle()
        => !TerminalCassettePlayback.IsPlaying
           && !TerminalCassettePlayback.IsWaitingForFile
           && !TerminalCoWatch.IsPreparingLocally
           && !TerminalCoWatch.IsPreparingForLobby
           && !MonitorPlayback.IsActive;

    private static void RefreshPlayback(Terminal terminal)
    {
        if (!TerminalCassettePlayback.IsPlaying) return;
        TerminalCassettePlayback.RefreshPlayback(terminal);
    }
}
