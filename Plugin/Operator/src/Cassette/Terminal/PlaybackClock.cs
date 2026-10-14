using System;

namespace Operator.Cassette;

internal sealed class PlaybackClock
{
    private const float MinSpeed = 0.1f;
    private const float MaxSpeed = 10f;

    private bool _active;
    private bool _running;
    private double _basePosition;
    private double _stamp;
    private float _speed = 1f;

    internal bool IsActive => _active;

    internal bool IsPaused => _active && !_running;

    internal float Speed => _speed;

    internal void Start(double now)
    {
        _active = true;
        _running = true;
        _basePosition = 0.0;
        _stamp = now;
        _speed = 1f;
    }

    internal void Stop()
    {
        _active = false;
        _running = false;
        _basePosition = 0.0;
        _speed = 1f;
    }

    internal void Restore(double position, double stamp, float speed, bool paused)
    {
        _active = true;
        _running = !paused;
        _basePosition = Math.Max(0.0, position);
        _stamp = stamp;
        _speed = ClampSpeed(speed);
    }

    internal double Position(double now)
    {
        if (!_active) return 0.0;
        if (!_running) return _basePosition;
        return _basePosition + (Math.Max(0.0, now - _stamp) * _speed);
    }

    internal void Seek(double position, double now)
    {
        if (!_active || double.IsNaN(position) || double.IsInfinity(position) || position < 0.0) return;
        _basePosition = position;
        _stamp = now;
    }

    internal void Pause(double now) => Retime(now, false);

    internal void Resume(double now) => Retime(now, true);

    internal void SetSpeed(float speed, double now)
    {
        if (!_active) return;
        _basePosition = Position(now);
        _stamp = now;
        _speed = ClampSpeed(speed);
    }

    private void Retime(double now, bool running)
    {
        if (!_active) return;
        _basePosition = Position(now);
        _stamp = now;
        _running = running;
    }

    private static float ClampSpeed(float speed)
    {
        if (float.IsNaN(speed) || float.IsInfinity(speed)) return 1f;
        return Math.Min(Math.Max(speed, MinSpeed), MaxSpeed);
    }
}
