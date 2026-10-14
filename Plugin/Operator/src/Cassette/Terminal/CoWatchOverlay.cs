using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace Operator.Cassette;

internal static partial class TerminalCassettePlayback
{
    private const string OverlayCanvasName = "VideoCamCoWatchCanvas";
    private const string OverlayBoxName = "VideoCamCoWatchBox";
    private const string OverlayImageName = "VideoCamCoWatchOverlay";
    private const string OverlayPlayerName = "VideoCamCoWatchOverlayPlayer";
    private const string OverlayStatusName = "VideoCamCoWatchStatus";

    private struct CanvasPose
    {
        public Transform? Parent;
        public Vector2 AnchorMin;
        public Vector2 AnchorMax;
        public Vector2 Pivot;
        public Vector2 SizeDelta;
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 Scale;
        public int SortingLayerId;
        public int SortingOrder;
        public AdditionalCanvasShaderChannels ShaderChannels;
    }

    private static CanvasPose? _pose;
    private static Canvas? _overlayCanvas;
    private static RawImage? _overlayBox;
    private static RawImage? _overlayImage;
    private static TextMeshProUGUI? _overlayStatus;
    private static VideoPlayer? _overlayPlayer;
    private static RenderTexture? _overlayTexture;
    private static string _overlayUrl = string.Empty;
    private static Vector2 _videoSize;
    private static bool _atTerminal;
    private static TMP_Text? _screenText;
    private static RectTransform? _textView;
    private static Vector2 _textTop;
    private static bool _textMoved;

    internal static void InitPose(Terminal terminal)
    {
        if (_pose != null) return;

        Canvas source = terminal.terminalUIScreen;
        var rect = (RectTransform)source.transform;
        _pose = new CanvasPose
        {
            Parent = rect.parent,
            AnchorMin = rect.anchorMin,
            AnchorMax = rect.anchorMax,
            Pivot = rect.pivot,
            SizeDelta = rect.sizeDelta,
            Position = rect.localPosition,
            Rotation = rect.localRotation,
            Scale = rect.localScale,
            SortingLayerId = source.sortingLayerID,
            SortingOrder = source.sortingOrder,
            ShaderChannels = source.additionalShaderChannels,
        };

        _screenText = terminal.screenText.textComponent;
        ScrollRect? scroll = terminal.screenText.GetComponentInParent<ScrollRect>(true);
        if (scroll != null)
        {
            _textView = (RectTransform)scroll.transform;
            _textTop = _textView.offsetMax;
        }
    }

    private static Canvas CreateOverlayCanvas(Terminal terminal)
    {
        if (_overlayCanvas != null) return _overlayCanvas;

        InitPose(terminal);
        CanvasPose pose = _pose!.Value;

        var canvasObject = new GameObject(OverlayCanvasName, typeof(RectTransform));
        canvasObject.transform.SetParent(pose.Parent, false);
        _overlayCanvas = canvasObject.AddComponent<Canvas>();
        _overlayCanvas.renderMode = RenderMode.WorldSpace;
        _overlayCanvas.sortingLayerID = pose.SortingLayerId;
        _overlayCanvas.sortingOrder = pose.SortingOrder + Constants.Terminal.OverlaySortingBoost;
        _overlayCanvas.additionalShaderChannels = pose.ShaderChannels;

        var canvasRect = (RectTransform)canvasObject.transform;
        canvasRect.anchorMin = pose.AnchorMin;
        canvasRect.anchorMax = pose.AnchorMax;
        canvasRect.pivot = pose.Pivot;
        canvasRect.sizeDelta = pose.SizeDelta;
        canvasRect.localPosition = pose.Position;
        canvasRect.localRotation = pose.Rotation;
        canvasRect.localScale = pose.Scale;

        var boxSize = new Vector2(Constants.Terminal.ScreenWidth, Constants.Terminal.ScreenHeight);
        var boxPosition = new Vector3(Constants.Terminal.ScreenCenter.x, Constants.Terminal.ScreenCenter.y, Constants.Terminal.OverlayDepth);
        _overlayBox = CreateImage(OverlayBoxName, canvasRect, boxSize, boxPosition);
        _overlayBox.color = Color.black;

        _overlayImage = CreateImage(OverlayImageName, _overlayBox.rectTransform, boxSize, Vector3.zero);
        _overlayImage.enabled = false;

        canvasObject.SetActive(false);
        return _overlayCanvas;
    }

    private static RawImage CreateImage(string name, Transform parent, Vector2 size, Vector3 position)
    {
        var imageObject = new GameObject(name, typeof(RectTransform));
        imageObject.transform.SetParent(parent, false);

        var image = imageObject.AddComponent<RawImage>();
        image.raycastTarget = false;

        RectTransform rect = image.rectTransform;
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        rect.anchoredPosition3D = position;
        return image;
    }

    private static TextMeshProUGUI GetStatusText(Terminal terminal)
    {
        CreateOverlayCanvas(terminal);
        if (_overlayStatus != null) return _overlayStatus;

        var go = new GameObject(OverlayStatusName, typeof(RectTransform));
        go.transform.SetParent(_overlayBox!.transform, false);
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
        Canvas canvas = CreateOverlayCanvas(terminal);
        TextMeshProUGUI status = GetStatusText(terminal);

        _lastStatusText = message;
        if (!_isPlaying) FitVideo(Constants.Terminal.OverlayFallbackWidth, Constants.Terminal.OverlayFallbackHeight);
        if (_overlayImage != null) _overlayImage.enabled = false;
        status.text = message;
        status.enabled = true;
        canvas.gameObject.SetActive(true);
    }

    internal static void HidePrepareStatus()
    {
        _lastStatusText = string.Empty;
        if (_overlayStatus != null) _overlayStatus.enabled = false;
        if (_overlayImage != null) _overlayImage.enabled = _isPlaying;
    }

    private static VideoPlayer GetOverlayPlayer(Terminal terminal)
    {
        CreateOverlayCanvas(terminal);
        if (_overlayPlayer != null) return _overlayPlayer;

        var playerObject = new GameObject(OverlayPlayerName);
        playerObject.transform.SetParent(terminal.transform, false);
        _overlayPlayer = playerObject.AddComponent<VideoPlayer>();
        _overlayPlayer.playOnAwake = false;
        _overlayPlayer.renderMode = VideoRenderMode.RenderTexture;
        _overlayPlayer.aspectRatio = VideoAspectRatio.Stretch;
        _overlayPlayer.source = VideoSource.Url;
        _overlayPlayer.isLooping = false;
        _overlayPlayer.skipOnDrop = true;
        return _overlayPlayer;
    }

    private static void UseTexture(int width, int height)
    {
        RenderTexture? texture = _overlayTexture;
        if (texture == null || !texture.IsCreated() || texture.width != width || texture.height != height)
        {
            if (texture != null)
            {
                texture.Release();
                UnityEngine.Object.Destroy(texture);
            }

            texture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32)
            {
                name = "CoWatchOverlayRT",
            };
            texture.Create();
            _overlayTexture = texture;

            if (_overlayPlayer != null) _overlayPlayer.targetTexture = texture;
            if (_overlayImage != null) _overlayImage.texture = texture;
        }

        TelevisionPlayback.ClearBlack(texture);
    }

    private static void FitVideo(float videoWidth, float videoHeight)
    {
        _videoSize = new Vector2(videoWidth, videoHeight);
        Layout();
    }

    internal static void UpdateLayout(Terminal terminal)
    {
        bool atTerminal = terminal.terminalInUse && _overlayCanvas != null && _overlayCanvas.gameObject.activeSelf;
        if (atTerminal == _atTerminal) return;
        _atTerminal = atTerminal;
        Layout();
    }

    private static void Layout()
    {
        bool console = _atTerminal && _textView != null;
        if (!console) RestoreText();
        if (_overlayBox == null || _overlayImage == null) return;

        float width = Constants.Terminal.ScreenWidth;
        float height = Constants.Terminal.ScreenHeight;
        Vector2 center = Constants.Terminal.ScreenCenter;
        RectTransform box = _overlayBox.rectTransform;

        if (!console)
        {
            (float fitWidth, float fitHeight) = Letterbox.Fit(_videoSize.x, _videoSize.y, width, height);
            box.sizeDelta = new Vector2(width, height);
            box.anchoredPosition = center;
            _overlayImage.rectTransform.sizeDelta = new Vector2(fitWidth, fitHeight);
            return;
        }

        (float videoWidth, float videoHeight) = Letterbox.Fit(_videoSize.x, _videoSize.y, width, height - Constants.Terminal.TextSpace);
        float top = center.y + (height / 2f);
        box.sizeDelta = new Vector2(width, videoHeight);
        box.anchoredPosition = new Vector2(center.x, top - (videoHeight / 2f));
        _overlayImage.rectTransform.sizeDelta = new Vector2(videoWidth, videoHeight);

        float textTop = top - videoHeight + (Constants.Terminal.HiddenLines * GetRowHeight()) + Constants.Terminal.TextMargin;
        _textView!.offsetMax = new Vector2(_textTop.x, textTop);
        _textMoved = true;
    }

    private static void RestoreText()
    {
        if (!_textMoved) return;
        _textMoved = false;
        if (_textView != null) _textView.offsetMax = _textTop;
    }

    private static float GetRowHeight()
    {
        if (_screenText == null || _screenText.font == null) return 0f;
        var face = _screenText.font.faceInfo;
        return face.pointSize > 0f ? _screenText.fontSize * face.lineHeight / face.pointSize : 0f;
    }

    private static void OnVideoPrepared(VideoPlayer vp)
    {
        if (!_isPlaying || vp != _overlayPlayer || _overlayUrl != _currentPlaybackUrl) return;

        (int width, int height) = Letterbox.PickSize(
            (int)vp.width,
            (int)vp.height,
            Constants.Terminal.OverlayMaxWidth,
            Constants.Terminal.OverlayMaxHeight,
            Constants.Terminal.OverlayFallbackWidth,
            Constants.Terminal.OverlayFallbackHeight);
        UseTexture(width, height);
        FitVideo(width, height);

        if (_pendingSeekSeconds >= 0.0)
        {
            vp.time = _pendingSeekSeconds;
            _pendingSeekSeconds = -1.0;
        }

        vp.Play();
    }

    private static void ShowOverlay()
    {
        if (_overlayCanvas == null) return;

        if (_overlayImage != null) _overlayImage.enabled = true;
        _overlayCanvas.gameObject.SetActive(true);
    }

    private static void HideOverlay()
    {
        if (_overlayImage != null) _overlayImage.enabled = false;
        if (_overlayCanvas != null) _overlayCanvas.gameObject.SetActive(false);
        _atTerminal = false;
        Layout();
    }

    private static void DestroyOverlay()
    {
        try
        {
            RestoreText();
            if (_overlayPlayer != null)
            {
                _overlayPlayer.loopPointReached -= OnVideoFinished;
                _overlayPlayer.errorReceived -= OnVideoError;
                _overlayPlayer.prepareCompleted -= OnVideoPrepared;
                UnityEngine.Object.Destroy(_overlayPlayer.gameObject);
            }
            if (_overlayCanvas != null)
            {
                UnityEngine.Object.Destroy(_overlayCanvas.gameObject);
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
        _overlayCanvas = null;
        _overlayBox = null;
        _overlayImage = null;
        _overlayTexture = null;
        _overlayStatus = null;
        _pose = null;
        _videoSize = Vector2.zero;
        _atTerminal = false;
        _screenText = null;
        _textView = null;
    }
}
