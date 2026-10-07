using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace Operator.Cassette;

internal static partial class TerminalCassettePlayback
{
    private const string OverlayImageName = "VideoCamCoWatchOverlay";
    private const string OverlayPlayerName = "VideoCamCoWatchOverlayPlayer";
    private const string OverlayStatusName = "VideoCamCoWatchStatus";

    private static VideoPlayer? _overlayPlayer;
    private static RenderTexture? _overlayTexture;
    private static RawImage? _overlayImage;
    private static TextMeshProUGUI? _overlayStatus;
    private static string _overlayUrl = string.Empty;

    private static TextMeshProUGUI? GetStatusText(Terminal terminal)
    {
        if (GetOverlayPlayer(terminal) == null || _overlayImage == null) return null;
        if (_overlayStatus != null) return _overlayStatus;

        var go = new GameObject(OverlayStatusName, typeof(RectTransform));
        go.transform.SetParent(_overlayImage.transform, false);
        _overlayStatus = go.AddComponent<TextMeshProUGUI>();

        TMP_Text? source = terminal.screenText != null ? terminal.screenText.textComponent : null;
        if (source != null && source.font != null)
        {
            _overlayStatus.font = source.font;
            _overlayStatus.fontSharedMaterial = source.fontSharedMaterial;
            _overlayStatus.color = source.color;
            _overlayStatus.fontStyle = source.fontStyle;
            _overlayStatus.characterSpacing = source.characterSpacing;
            _overlayStatus.fontSize = source.fontSize * Constants.Terminal.StatusFontScale;
        }
        else
        {
            _overlayStatus.fontSize = Constants.Terminal.StatusFallbackFontSize;
        }

        _overlayStatus.alignment = TextAlignmentOptions.Center;
        _overlayStatus.enableWordWrapping = true;
        _overlayStatus.overflowMode = TextOverflowModes.Overflow;
        _overlayStatus.raycastTarget = false;

        RectTransform rt = _overlayStatus.rectTransform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        return _overlayStatus;
    }

    internal static void ShowPrepareStatus(Terminal terminal, string message)
    {
        TextMeshProUGUI? t = GetStatusText(terminal);
        if (_overlayImage == null) return;

        _lastStatusText = message;
        _overlayImage.enabled = true;
        _overlayImage.color = Color.black;
        _overlayImage.transform.SetAsLastSibling();

        if (t != null)
        {
            t.text = message;
            t.enabled = true;
            t.transform.SetAsLastSibling();
        }
    }

    internal static void HidePrepareStatus()
    {
        _lastStatusText = string.Empty;
        if (_overlayStatus != null) _overlayStatus.enabled = false;
        if (_overlayImage != null) _overlayImage.color = Color.white;
    }

    private static VideoPlayer? GetOverlayPlayer(Terminal terminal)
    {
        if (terminal == null || terminal.terminalImage == null) return null;

        if (_overlayTexture == null || !_overlayTexture.IsCreated())
        {
            int width = terminal.videoTexture != null && terminal.videoTexture.width > 0 ? terminal.videoTexture.width : 1280;
            int height = terminal.videoTexture != null && terminal.videoTexture.height > 0 ? terminal.videoTexture.height : 720;
            _overlayTexture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32)
            {
                name = "CoWatchOverlayRT",
            };
            _overlayTexture.Create();
        }

        if (_overlayImage == null)
        {
            Transform parent = terminal.terminalImage.transform.parent != null
                ? terminal.terminalImage.transform.parent
                : terminal.terminalImage.transform;

            var imageObject = new GameObject(OverlayImageName, typeof(RectTransform));
            imageObject.transform.SetParent(parent, false);
            _overlayImage = imageObject.AddComponent<RawImage>();
            _overlayImage.raycastTarget = false;

            RectTransform src = terminal.terminalImage.rectTransform;
            RectTransform dst = _overlayImage.rectTransform;
            dst.anchorMin = src.anchorMin;
            dst.anchorMax = src.anchorMax;
            dst.pivot = src.pivot;
            dst.anchoredPosition = src.anchoredPosition;
            dst.sizeDelta = src.sizeDelta;
            dst.localScale = src.localScale;
            dst.localRotation = src.localRotation;

            var overlayCanvas = imageObject.AddComponent<Canvas>();
            overlayCanvas.overrideSorting = true;
            overlayCanvas.sortingOrder =
                (terminal.terminalUIScreen != null ? terminal.terminalUIScreen.sortingOrder : 0) + 10;

            _overlayImage.transform.SetAsLastSibling();
            _overlayImage.enabled = false;
        }

        _overlayImage.texture = _overlayTexture;

        if (_overlayPlayer == null)
        {
            var playerObject = new GameObject(OverlayPlayerName);
            playerObject.transform.SetParent(terminal.transform, false);
            _overlayPlayer = playerObject.AddComponent<VideoPlayer>();
            _overlayPlayer.playOnAwake = false;
            _overlayPlayer.renderMode = VideoRenderMode.RenderTexture;
            _overlayPlayer.targetTexture = _overlayTexture;
            _overlayPlayer.aspectRatio = VideoAspectRatio.Stretch;
            _overlayPlayer.source = VideoSource.Url;
            _overlayPlayer.isLooping = false;
            _overlayPlayer.skipOnDrop = true;
        }

        return _overlayPlayer;
    }

    private static void ShowOverlay()
    {
        if (_overlayImage != null) _overlayImage.enabled = true;
    }

    private static void HideOverlay()
    {
        if (_overlayImage != null) _overlayImage.enabled = false;
    }

    private static void DestroyOverlay()
    {
        try
        {
            if (_overlayPlayer != null)
            {
                _overlayPlayer.loopPointReached -= OnVideoFinished;
                _overlayPlayer.errorReceived -= OnVideoError;
                UnityEngine.Object.Destroy(_overlayPlayer.gameObject);
            }
            if (_overlayImage != null)
            {
                UnityEngine.Object.Destroy(_overlayImage.gameObject);
            }
            if (_overlayTexture != null)
            {
                _overlayTexture.Release();
                UnityEngine.Object.Destroy(_overlayTexture);
            }
        }
        catch (Exception)
        {
        }
        _overlayPlayer = null;
        _overlayUrl = string.Empty;
        _overlayImage = null;
        _overlayTexture = null;
        _overlayStatus = null;
    }
}
