using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Operator.Recording;

namespace Operator;

internal sealed class FeedRenderer : MonoBehaviour
{
    private static FeedRenderer? _instance;
    private static readonly List<VideoCameraItem> _pending = new();
    private static readonly List<FrameCapture> _captures = new();
    private static readonly List<FrameCapture> _captureScratch = new();
    private static bool _captureFailureLogged;

    internal static void Request(VideoCameraItem item)
    {
        if (item == null) return;
        Create();
        if (!_pending.Contains(item)) _pending.Add(item);
        LateHud.Request();
        LateCameras.Request();
    }

    internal static void AddCapture(FrameCapture capture)
    {
        if (capture == null) return;
        Create();
        if (!_captures.Contains(capture)) _captures.Add(capture);
    }

    internal static void RemoveCapture(FrameCapture capture) => _captures.Remove(capture);

    private static void Create()
    {
        if (_instance != null) return;
        var go = new GameObject("OperatorFeedRenderer") { hideFlags = HideFlags.HideAndDontSave };
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<FeedRenderer>();
    }

    private IEnumerator Start()
    {
        var endOfFrame = new WaitForEndOfFrame();
        while (true)
        {
            yield return endOfFrame;

            try
            {
                for (int i = 0; i < _pending.Count; i++)
                {
                    VideoCameraItem item = _pending[i];
                    if (item != null && item.isActiveAndEnabled) item.RenderPendingFeed();
                }
                _pending.Clear();

                RenderTexture? previousTarget = RenderTexture.active;
                _captureScratch.Clear();
                _captureScratch.AddRange(_captures);
                for (int i = 0; i < _captureScratch.Count; i++)
                {
                    FrameCapture capture = _captureScratch[i];
                    if (capture == null)
                    {
                        _captures.Remove(capture!);
                        continue;
                    }
                    try
                    {
                        capture.Tick();
                    }
                    catch (Exception ex)
                    {
                        if (_captureFailureLogged) continue;
                        _captureFailureLogged = true;
                        Plugin.Log.LogError($"Failed to capture recording frame: {ex}");
                    }
                }
                RenderTexture.active = previousTarget;
            }
            finally
            {
                try { LateCameras.Render(); }
                finally { LateHud.Render(); }
            }
        }
    }

    private void OnDestroy()
    {
        if (_instance == this) _instance = null;
        LateHud.Release();
        LateCameras.Release();
    }
}
