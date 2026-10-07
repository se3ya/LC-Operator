using System;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace Operator.Recording;

internal sealed class FrameCapture : MonoBehaviour
{
    private static readonly int MaxDupBurst = Constants.Recording.MaxDupBurst;
    private static readonly int FallbackDeficit = Constants.Recording.FallbackDeficit;

    private Camera? _target;
    private RenderTexture? _readbackTexture;
    private VideoPipe? _pipe;
    private Func<bool>? _shouldCapture;
    private Func<Recorder.FinalePhase>? _finalePhase;
    private Func<double>? _activeTime;
    private Action? _onTick;
    private int _maxQueued;
    private int _readbackWidth;
    private int _readbackHeight;
    private double _interval;
    private long _emittedFrames;
    private volatile bool _stopped;
    private bool _queueWasFull;
    private int _drivenFrame = -1;
    private int _renderedFrame = -1;
    private double _horizon;
    private double _lastTick;
    private uint _staticRng = 0x9E3779B9u;

    public void Initialize(Camera target, int fps, int maxQueued, int readbackWidth, int readbackHeight, VideoPipe pipe, Func<bool> shouldCapture, Func<Recorder.FinalePhase> finalePhase, Func<double> activeTime, Action onTick)
    {
        _target = target;
        _readbackWidth = readbackWidth;
        _readbackHeight = readbackHeight;
        _interval = 1.0 / Mathf.Max(1, fps);
        _maxQueued = Mathf.Max(1, maxQueued);
        _pipe = pipe;
        _shouldCapture = shouldCapture;
        _finalePhase = finalePhase;
        _activeTime = activeTime;
        _onTick = onTick;
        _emittedFrames = 0;
    }

    public void Stop() => _stopped = true;

    internal double Interval => _interval;

    internal bool Stalled => _pipe != null && _pipe.Count >= _maxQueued;

    internal bool TryDrive(double ahead)
    {
        _drivenFrame = Time.frameCount;
        _horizon = (_activeTime?.Invoke() ?? 0.0) + ahead;
        return IsDue(_horizon);
    }

    internal void OnRendered() => _renderedFrame = Time.frameCount;

    private bool IsDue(double horizon)
    {
        if (_stopped || _target == null || _pipe == null)
        {
            return false;
        }

        if (_target.targetTexture == null)
        {
            return false;
        }

        if ((_finalePhase?.Invoke() ?? Recorder.FinalePhase.None) != Recorder.FinalePhase.None)
        {
            return false;
        }

        if (_shouldCapture != null && !_shouldCapture())
        {
            return false;
        }

        if ((long)(horizon / _interval) - _emittedFrames <= 0)
        {
            return false;
        }

        return _pipe.Count < _maxQueued;
    }

    private void OnEnable() => FeedRenderer.AddCapture(this);

    private void OnDisable() => FeedRenderer.RemoveCapture(this);

    internal void Tick()
    {
        if (_stopped || _target == null || _pipe == null)
        {
            return;
        }

        double now = Time.realtimeSinceStartupAsDouble;
        double frameTime = _lastTick > 0.0 ? now - _lastTick : 0.0;
        _lastTick = now;

        _onTick?.Invoke();
        RecordingStats.OnFrame(_pipe.Count);

        RenderTexture? rt = _target.targetTexture;
        if (rt == null)
        {
            return;
        }

        Recorder.FinalePhase phase = _finalePhase?.Invoke() ?? Recorder.FinalePhase.None;

        if (phase == Recorder.FinalePhase.None && _shouldCapture != null && !_shouldCapture())
        {
            return;
        }

        bool driven = phase == Recorder.FinalePhase.None && _drivenFrame == Time.frameCount;
        bool fresh = driven && _renderedFrame == Time.frameCount;
        double active = _activeTime?.Invoke() ?? 0.0;
        long expected = (long)(active / _interval);
        long deficit = expected - _emittedFrames;
        if (fresh && deficit <= 0)
        {
            deficit = (long)(_horizon / _interval) - _emittedFrames;
        }
        if (deficit <= 0)
        {
            return;
        }

        if (_pipe.Count >= _maxQueued)
        {
            if (!_queueWasFull)
            {
                _queueWasFull = true;
                RecordingStats.OnQueueFull();
                Plugin.Log.LogDebug($"Video frame queue full at {_pipe.Count} frames, deferring capture.");
            }
            return;
        }
        if (_queueWasFull)
        {
            _queueWasFull = false;
            RecordingStats.OnQueueDrained();
        }

        if (driven && !fresh && deficit < FallbackDeficit)
        {
            return;
        }

        int burst = Math.Max(MaxDupBurst, (int)Math.Ceiling(frameTime / _interval) + MaxDupBurst - 1);
        int repeats = (int)Math.Min(deficit, burst);
        if (repeats < 1)
        {
            return;
        }
        _emittedFrames += repeats;
        RecordingStats.OnFramesEmitted(repeats);

        if (phase != Recorder.FinalePhase.None)
        {
            EnqueueFinaleFrame(_readbackWidth, _readbackHeight, phase, repeats);
            return;
        }

        float requestedAt = Time.realtimeSinceStartup;
        AsyncGPUReadback.Request(GetReadbackTexture(rt), 0, TextureFormat.RGBA32, request => OnReadback(request, repeats, requestedAt));
    }

    private RenderTexture GetReadbackTexture(RenderTexture source)
    {
        if (source.width == _readbackWidth && source.height == _readbackHeight)
        {
            return source;
        }

        if (_readbackTexture == null)
        {
            RenderTextureDescriptor descriptor = source.descriptor;
            descriptor.width = _readbackWidth;
            descriptor.height = _readbackHeight;
            descriptor.depthBufferBits = 0;
            _readbackTexture = new RenderTexture(descriptor) { name = "OPERATOR_Readback" };
            _readbackTexture.Create();
        }

        Graphics.Blit(source, _readbackTexture);
        return _readbackTexture;
    }

    private void EnqueueFinaleFrame(int width, int height, Recorder.FinalePhase phase, int repeats)
    {
        if (_pipe == null)
        {
            return;
        }

        int length = width * height * 4;
        if (length <= 0)
        {
            return;
        }

        byte[] buffer = _pipe.RentFrameBuffer(length);
        if (phase == Recorder.FinalePhase.Dead)
        {
            Array.Clear(buffer, 0, length);
            for (int i = 3; i < length; i += 4) buffer[i] = 255;
        }
        else
        {
            uint s = _staticRng;
            for (int i = 0; i < length; i += 4)
            {
                s ^= s << 13; s ^= s >> 17; s ^= s << 5;
                byte v = (byte)(s & 0xFF);
                buffer[i] = v; buffer[i + 1] = v; buffer[i + 2] = v; buffer[i + 3] = 255;
            }
            _staticRng = s;
        }

        _pipe.Enqueue(buffer, repeats);
    }

    private void OnReadback(AsyncGPUReadbackRequest request, int repeats, float requestedAt)
    {
        RecordingStats.OnReadback((Time.realtimeSinceStartup - requestedAt) * 1000f);

        if (_stopped || _pipe == null)
        {
            return;
        }

        if (request.hasError)
        {
            _emittedFrames -= repeats;
            RecordingStats.OnReadbackFailed();
            return;
        }

        if ((_finalePhase?.Invoke() ?? Recorder.FinalePhase.None) != Recorder.FinalePhase.None)
        {
            return;
        }

        NativeArray<byte> data = request.GetData<byte>();
        int length = data.Length;
        if (length <= 0)
        {
            _emittedFrames -= repeats;
            return;
        }

        byte[] buffer = _pipe.RentFrameBuffer(length);
        data.CopyTo(buffer);
        _pipe.Enqueue(buffer, repeats);
    }

    private void OnDestroy()
    {
        Stop();
        if (_readbackTexture != null)
        {
            _readbackTexture.Release();
            Destroy(_readbackTexture);
            _readbackTexture = null;
        }
    }
}
