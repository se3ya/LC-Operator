using Operator.Recording;
using UnityEngine;

namespace Operator.Cassette;

internal sealed class ClipWait
{
    private bool _active;
    private int _chunks = -1;
    private double _lastMoved;
    private double _lastBusy;

    internal void Start()
    {
        _active = true;
        _chunks = -1;
        _lastMoved = Time.realtimeSinceStartupAsDouble;
        _lastBusy = _lastMoved;
    }

    internal void Clear()
    {
        _active = false;
        _chunks = -1;
        _lastMoved = 0d;
        _lastBusy = 0d;
    }

    internal bool IsNothingComing()
    {
        if (!_active) return false;

        double now = Time.realtimeSinceStartupAsDouble;
        ClipBroadcaster? broadcaster = ClipBroadcaster.Instance;
        if ((broadcaster != null && broadcaster.HasIncoming) || VideoCameraItem.IsStitching || CassetteItem.AnyEncoding)
        {
            _lastBusy = now;
            return false;
        }

        return now - _lastBusy > Constants.Terminal.MissingFileGrace;
    }

    internal bool IsStalled()
    {
        if (!_active) return false;

        ClipBroadcaster? broadcaster = ClipBroadcaster.Instance;
        int received = broadcaster != null ? broadcaster.ProgressChunks() : 0;

        if (received > _chunks)
        {
            _lastMoved = Time.realtimeSinceStartupAsDouble;
        }
        _chunks = received;

        if (VideoCameraItem.IsMerging || CassetteItem.AnyEncoding)
        {
            _lastMoved = Time.realtimeSinceStartupAsDouble;
            return false;
        }

        return Time.realtimeSinceStartupAsDouble - _lastMoved > Constants.Terminal.TransferSilenceTimeout;
    }
}
