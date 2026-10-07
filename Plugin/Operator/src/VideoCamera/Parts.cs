using System;
using System.Collections.Generic;
using UnityEngine;

namespace Operator;

internal sealed partial class VideoCameraItem
{
    private Animation? _lensCoverAnimation;
    private Renderer[] _lensCoverRenderers = Array.Empty<Renderer>();
    private readonly List<Renderer> _hiddenLensCover = new();
    private Transform? _focusRing;
    private Transform? _focusWheel;
    private Transform? _zoomRing;
    private Transform? _needle;
    private Renderer? _ringRenderer;
    private Renderer? _irLamp;
    private Renderer[] _tapeReels = Array.Empty<Renderer>();
    private Renderer[]? _decorRenderers;
    private Quaternion _focusRingRest = Quaternion.identity;
    private Quaternion _focusWheelRest = Quaternion.identity;
    private Quaternion _zoomRingRest = Quaternion.identity;
    private Quaternion _needleRest = Quaternion.identity;
    private float _needleAngle = Constants.Parts.NeedleEmptyAngle;
    private bool _partsCached;

    private void CacheParts()
    {
        if (_partsCached) return;
        _partsCached = true;

        Transform? cover = FindDeepChild(transform, Constants.Assets.LensCoverObjectName);
        if (cover != null)
        {
            _lensCoverAnimation = cover.GetComponent<Animation>();
            _lensCoverRenderers = cover.GetComponentsInChildren<Renderer>(true);
        }

        _focusRing = FindDeepChild(transform, Constants.Assets.FocusRingObjectName);
        _focusWheel = FindDeepChild(transform, Constants.Assets.FocusWheelObjectName);
        _zoomRing = FindDeepChild(transform, Constants.Assets.ZoomRingObjectName);
        _needle = FindDeepChild(transform, Constants.Assets.NeedleObjectName);
        if (_focusRing != null) _focusRingRest = _focusRing.localRotation;
        if (_focusWheel != null) _focusWheelRest = _focusWheel.localRotation;
        if (_zoomRing != null)
        {
            _zoomRingRest = _zoomRing.localRotation;
            _ringRenderer = _zoomRing.GetComponent<Renderer>();
        }
        if (_needle != null) _needleRest = _needle.localRotation;

        Transform? lamp = FindDeepChild(transform, Constants.Assets.IrLampObjectName);
        if (lamp != null) _irLamp = lamp.GetComponent<Renderer>();

        var reels = new List<Renderer>();
        foreach (string name in Constants.Assets.TapeReelObjectNames)
        {
            Transform? reel = FindDeepChild(transform, name);
            Renderer? reelRenderer = reel != null ? reel.GetComponent<Renderer>() : null;
            if (reelRenderer != null) reels.Add(reelRenderer);
        }
        _tapeReels = reels.ToArray();

    }

    private void HideLensCover()
    {
        CacheParts();
        foreach (Renderer cover in _lensCoverRenderers)
        {
            if (cover == null || cover.forceRenderingOff) continue;
            cover.forceRenderingOff = true;
            _hiddenLensCover.Add(cover);
        }
    }

    private void ShowLensCover()
    {
        foreach (Renderer cover in _hiddenLensCover)
        {
            if (cover != null) cover.forceRenderingOff = false;
        }
        _hiddenLensCover.Clear();
    }

    private void ApplyLensCover(bool open, bool animate)
    {
        CacheParts();
        if (_lensCoverAnimation == null) return;
        string clipName = open ? Constants.Assets.LensCoverOpenAnimationName : Constants.Assets.LensCoverCloseAnimationName;
        if (animate) PlayClip(_lensCoverAnimation, clipName);
        else SampleClipEnd(_lensCoverAnimation, clipName);
    }

    private void ApplyToggle(CameraButton button, bool on, bool animate)
    {
        PlayStateClip(button, on ? Constants.Assets.ButtonOnClipSuffix : Constants.Assets.ButtonOffClipSuffix, animate);
    }

    private void ApplyRecordButton(bool down, bool animate)
    {
        PlayStateClip(CameraButton.Record, down ? Constants.Assets.ButtonDownClipSuffix : Constants.Assets.ButtonUpClipSuffix, animate);
    }

    private void PlayStateClip(CameraButton button, string suffix, bool animate)
    {
        CacheVisualReferences();
        Animation? animation = _buttonAnimations[(int)button];
        string? clipName = FindClip(animation, suffix);
        if (clipName == null) return;
        if (animate) PlayClip(animation, clipName, reverseOfCurrent: true);
        else SampleClipEnd(animation, clipName);
    }

    private bool HasStateClips(CameraButton button)
    {
        Animation? animation = _buttonAnimations[(int)button];
        return button switch
        {
            CameraButton.Record => FindClip(animation, Constants.Assets.ButtonDownClipSuffix) != null,
            CameraButton.Flip or CameraButton.NightVision => FindClip(animation, Constants.Assets.ButtonOnClipSuffix) != null,
            _ => false,
        };
    }

    private void ApplyIrLamp(bool on)
    {
        CacheParts();
        if (_irLamp == null) return;
        Material? material = on ? AssetsLoader.LedIrLightMaterial : AssetsLoader.LedBlackLightMaterial;
        if (material != null) ApplyLedMaterial(_irLamp, material);
    }

    private void RefreshTapeReels()
    {
        CacheParts();
        bool loaded = _recordingState.Value || _sessionOpen.Value || _blanksRemaining.Value > 0;
        SetShown(_tapeReels, loaded && !IsHidden());
    }

    private void RefreshDecor()
    {
        if (_walletDecor == null) return;
        _decorRenderers ??= _walletDecor.GetComponentsInChildren<Renderer>(true);
        SetShown(_decorRenderers, !IsHidden());
    }

    private bool IsHidden()
    {
        return _ringRenderer != null ? !_ringRenderer.enabled : isPocketed;
    }

    private static void SetShown(Renderer[] parts, bool shown)
    {
        foreach (Renderer part in parts)
        {
            if (part != null && part.enabled != shown) part.enabled = shown;
        }
    }

    private void UpdateParts()
    {
        CacheParts();
        RefreshTapeReels();
        RefreshDecor();
        if (_ringRenderer != null && _ringRenderer.isVisible) UpdateLensRings();
        UpdateNeedle();
    }

    private void UpdateLensRings()
    {
        if (_zoomRing != null)
        {
            float min = VideoCameraConfig.MinFov.Value;
            float max = Mathf.Max(VideoCameraConfig.MaxFov.Value, min + 1f);
            float zoom = Mathf.InverseLerp(max, min, _currentFov);
            _zoomRing.localRotation = _zoomRingRest * Quaternion.Euler(0f, 0f, zoom * Constants.Parts.RingSweep);
        }

        float focus = Mathf.Clamp01(_afLensDiopter / Constants.Parts.FocusNearDiopter);
        if (_focusRing != null)
        {
            _focusRing.localRotation = _focusRingRest * Quaternion.Euler(0f, 0f, focus * Constants.Parts.RingSweep);
        }
        if (_focusWheel != null)
        {
            _focusWheel.localRotation = _focusWheelRest * Quaternion.Euler(0f, 0f, -focus * Constants.Parts.RingSweep * Constants.Parts.FocusWheelRatio);
        }
    }

    private void UpdateNeedle()
    {
        if (_needle == null) return;

        float charge = HasUsableBattery() ? Mathf.Clamp01(CurrentCharge()) : 0f;
        float target = Mathf.Lerp(Constants.Parts.NeedleEmptyAngle, Constants.Parts.NeedleFullAngle, charge);
        _needleAngle = Mathf.Lerp(_needleAngle, target, 1f - Mathf.Exp(-Constants.Parts.NeedleSpeed * Time.deltaTime));

        float tremble = 0f;
        if (_recordingState.Value)
        {
            float noise = Mathf.PerlinNoise(Time.time * 3f, 0.37f);
            tremble = Mathf.Sin(Time.time * Constants.Parts.NeedleTrembleRate) * Constants.Parts.NeedleTremble * (0.6f + 0.4f * noise);
        }
        _needle.localRotation = _needleRest * Quaternion.Euler(_needleAngle + tremble, 0f, 0f);
    }
}
