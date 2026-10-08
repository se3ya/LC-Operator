using System;
using System.Collections.Generic;
using GameNetcodeStuff;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;
using Operator.Cassette;
using Operator.Recording;

namespace Operator;

[DisallowMultipleComponent]
internal sealed partial class VideoCameraItem : GrabbableObject
{
    private static readonly Color DefaultPauseColor = new Color32(0xFF, 0xCC, 0x00, 0xFF);
    private static readonly int PauseMaterialBaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int PauseMaterialColorId = Shader.PropertyToID("_Color");

    private readonly NetworkVariable<bool> _recordingState = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<bool> _cameraMode = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);

    private readonly NetworkVariable<bool> _cameraInFactory = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);

    internal bool IsInside => _cameraInFactory.Value;

    private readonly NetworkVariable<bool> _nightVisionOn = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<int> _blanksRemaining = new(
        1,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<bool> _hasPendingCassette = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<bool> _sessionOpen = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<int> _crackStageFront = new(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<int> _crackStarMask = new(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<int> _crackStageBack = new(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<int> _zoomDir = new(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);

    private readonly NetworkVariable<int> _buttonPressPulse = new(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);

    private readonly NetworkVariable<double> _tapeClockBase = new(
        0.0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<float> _zoomFov = new(
        Constants.Camera.DefaultFov,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);

    private readonly NetworkVariable<float> _operatorFear = new(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);

    private readonly NetworkVariable<int> _batteryLevel = new(
        -1,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);

    private enum ButtonHoldPhase { Idle, Press, HeldPaused, Release }

    private enum CameraButton { Record, Flip, NightVision, ZoomIn, ZoomOut }

    private const int ButtonCount = 5;

    private enum ScreenPhase { Off, Live }

    private Camera? _camera;
    private Camera? _backCamera;
    private Transform? _frontCameraParent;
    private Vector3 _frontCameraLocalPos;
    private Quaternion _frontCameraLocalRot = Quaternion.identity;
    private Vector3 _frontCameraLocalScale = Vector3.one;
    private Transform? _backCameraAnchor;
    private bool _cameraAnchorsCaptured;
    private Renderer? _screenRenderer;
    private Material? _screenMaterial;
    private RenderTexture? _feedTexture;

    private Animation? _walletAnimation;
    private Animation? _circlesAnimation;
    private Animation? _shellAnimation;
    private Transform? _screenHinge;
    private Animation? _webcamAnimation;
    private readonly Animation?[] _buttonAnimations = new Animation?[ButtonCount];
    private Light? _redLight;
    private Light? _redLight2;
    private Color _redLightColor = Color.red;
    private bool _redLightCaptured;
    private bool _redLightUseTemperature;
    private float _redLightTemperature;
    private Renderer? _redLightRenderer;
    private Renderer? _redLight2Renderer;
    private Renderer? _tallyRenderer;
    private AudioSource? _audioSource;

    private Light? _screenGlowLight;
    private UnityEngine.Rendering.HighDefinition.HDAdditionalLightData? _screenGlowHdLight;
    private RenderTexture? _ambientProbeRt;
    private int _ambientProbeTopMip;
    private bool _ambientReadbackPending;
    private double _nextAmbientSampleTime;
    private bool _feedFresh;
    private Action<AsyncGPUReadbackRequest>? _ambientCallback;
    private Color _feedAmbientTarget = Color.black;
    private Color _feedAmbient = Color.black;
    private RenderTexture? _rawProbeRt;
    private int _rawProbeTopMip;
    private bool _rawReadbackPending;
    private Color _rawAmbientTarget = Color.black;
    private Color _rawAmbient = Color.black;
    private bool _rawAmbientReady;
    private bool _hintsBlocked;
    private double _nextRawSampleTime;
    private double _rawAmbientTime;
    private bool _screenGlowMissLogged;
    private bool _readbackUnsupportedLogged;

    private AudioSource? _handlingAudioSource;
    private AudioSource? _zoomAudioSource;
    private AudioSource? _recordingRunAudioSource;
    private AudioSource? _buttonPressAudioSource;
    private AudioSource? _buttonHoldAudioSource;
    private AudioSource? _cassetteEjectAudioSource;
    private AudioSource? _crackAudioSource;
    private AudioSource? _nvClickAudioSource;
    private ButtonHoldPhase _buttonHoldPhase = ButtonHoldPhase.Idle;
    private Vector3 _lastHandlingPos;
    private bool _lastHandlingPosValid;
    private int _zoomSfxDir;

    private int _lastCullingMask;
    private bool _excludeMaskParsed;
    private int _excludeMask;
    private bool _networkReady;
    private bool _wasHeldLastFrame;
    private bool _wasPocketedLastFrame;
    private bool _pausedByPocket;
    private bool _wasSeatedLastFrame;
    private bool _pausedBySeat;
    private bool _isHovered;
    private bool _inputSubscribed;
    private bool _shellOpen;
    private ScreenPhase _screenPhase;
    private ScreenPhase _appliedScreenPhase = ScreenPhase.Off;
    private double _shellCloseCutTime;
    private bool _screenInitialized;
    private bool _walletOpen;
    private double _walletBusyUntil;
    private float _currentFov = Constants.Camera.DefaultFov;
    private Coroutine? _equipDelayCoroutine;
    private Coroutine? _webcamRaiseCoroutine;
    private bool _flipApplied;
    private bool _batterySpinDown;

    private bool _renderSubscribed;
    private bool _occlusionMaskParsed;
    private int _occlusionMask;
    private float _occlusionProximity;
    private Material? _occlusionOverlayMaterial;
    private Texture2D? _occlusionBlackTexture;
    private Texture2D? _occlusionVignetteTexture;
    private RenderTexture? _occlusionBlurTempA;
    private RenderTexture? _occlusionBlurTempB;
    private float _bobPitch;
    private float _bobRoll;
    private float _lastBobPitch;
    private float _lastBobRoll;

    private Quaternion _cameraRestLocalRot = Quaternion.identity;
    private bool _cameraRestCaptured;
    private float _bobPhase;
    private float _jumpPitchKick;
    private bool _wasGroundedLastFrame = true;
    private float _smoothSpeed;

    private Recorder? _recorder;
    private ClipBroadcaster? _broadcaster;

    private EnemyBountyTracker? _bountyTracker;
    private DateTime _recordingStartTime;
    private bool _pauseOnStop;
    private bool _deadManActive;
    private PlayerControllerB? _lastHolder;
    private readonly Dictionary<string, (string Lobby, string Breakdown)> _ejectedTakes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, EnemyBountyTracker> _stoppedTrackers = new(StringComparer.OrdinalIgnoreCase);
    private bool _serverHasPendingCassette;
    private bool _serverTakeOpen;
    private float _serverTakeEnd;
    private bool _serverSessionWorthless;
    private ulong _serverSessionOwner;
    private double _serverHandoffTime = -1d;
    private string _serverPendingCassetteId = string.Empty;
    private string _serverPendingFileName = string.Empty;
    private string _serverPendingTimestamp = string.Empty;
    private float _serverPendingDuration;
    private int _serverPendingValue;
    private string _serverPendingBreakdown = string.Empty;
    private bool _serverPendingEncoding;
    private ulong _serverPendingOwner;
    private float _serverEjectedStart = float.MinValue;
    private StoredTake? _serverShadowTake;
    private Coroutine? _serverEjectCoroutine;
    private Coroutine? _walletEjectCoroutine;
    private double _walletOpenedAt;
    private double _nextEjectTime;
    private double _nextFeedRenderTime;
    private ScanNodeProperties? _cameraScanNode;
    private bool _cameraScanNodeSearched;
    private Transform? _walletTransform;
    private Transform? _ejectAnchor;
    private bool _ejectInputSubscribed;

    private GameObject? _walletDecor;
    private ScanNodeProperties? _walletDecorScan;
    private Vector3 _decorBase;
    private double _liftStart = -1.0;
    private bool _decorHidden;

    private readonly struct StoredTake
    {
        public readonly string Id;
        public readonly string File;
        public readonly string Timestamp;
        public readonly float Duration;
        public readonly int Value;
        public readonly string Breakdown;

        public StoredTake(string id, string file, string timestamp, float duration, int value, string breakdown)
        {
            Id = id;
            File = file;
            Timestamp = timestamp;
            Duration = duration;
            Value = value;
            Breakdown = breakdown;
        }
    }

    private void Awake()
    {
        CacheVisualReferences();
        SetupCameraTarget();
        ApplyClosedShellPose();
        ApplyRecordingVisuals(false);
    }

    private void OnEnable()
    {
        SubscribeEjectInput();
        if (_renderSubscribed) return;
        RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
        RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
        _renderSubscribed = true;
    }

    private void OnDisable()
    {
        UnsubscribeEjectInput();
        _serverEjectCoroutine = null;
        _walletEjectCoroutine = null;
        if (!_renderSubscribed) return;
        RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
        RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
        _renderSubscribed = false;
        ResetOcclusion();
        SetNightVisionLights(false);
        ResetCameraBob();
        _wasGroundedLastFrame = true;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _networkReady = true;
        _recordingState.OnValueChanged += OnRecordingStateChanged;
        _cameraMode.OnValueChanged += OnCameraModeChanged;
        _nightVisionOn.OnValueChanged += OnNightVisionChanged;
        _blanksRemaining.OnValueChanged += OnBlanksRemainingChanged;
        Spawned.Add(this);
        _hasPendingCassette.OnValueChanged += OnHasPendingCassetteChanged;
        _sessionOpen.OnValueChanged += OnSessionOpenChanged;
        _tapeClockBase.OnValueChanged += OnTapeClockChanged;
        _zoomDir.OnValueChanged += OnZoomDirChanged;
        _buttonPressPulse.OnValueChanged += OnButtonPressPulseChanged;
        _crackStageFront.OnValueChanged += OnCrackStageChanged;
        _crackStageBack.OnValueChanged += OnCrackStageChanged;
        _zoomFov.OnValueChanged += OnZoomFovChanged;
        _batteryLevel.OnValueChanged += OnBatteryLevelChanged;
        if (IsServer)
        {
            NetworkManager.OnClientDisconnectCallback += OnClientGone;
            StartCoroutine(SyncBlanksLoop());
            ServerRestockBlanks(orbit: false);
        }
        if (!IsOwner) ApplyBatteryLevel(_batteryLevel.Value);
        RefreshBlanks();
        ApplyRecordingVisuals(_recordingState.Value);
        ApplyCameraFlip();
        ApplyWebcamPose(_cameraMode.Value);
        ApplyToggle(CameraButton.Flip, _cameraMode.Value, animate: false);
        ApplyToggle(CameraButton.NightVision, _nightVisionOn.Value, animate: false);
        ApplyRecordButton(_recordingState.Value, animate: false);
        ApplyIrLamp(_nightVisionOn.Value);
        RefreshTapeReels();
        DisableCameras();
        ApplySyncedZoom();

        StartCoroutine(DrainBattery());
    }

    public override void OnNetworkDespawn()
    {
        _networkReady = false;
        Spawned.Remove(this);
        if (IsServer)
        {
            if (NetworkManager != null) NetworkManager.OnClientDisconnectCallback -= OnClientGone;
            ServerSyncBlanks();
        }
        _recordingState.OnValueChanged -= OnRecordingStateChanged;
        _cameraMode.OnValueChanged -= OnCameraModeChanged;
        _nightVisionOn.OnValueChanged -= OnNightVisionChanged;
        _blanksRemaining.OnValueChanged -= OnBlanksRemainingChanged;
        _hasPendingCassette.OnValueChanged -= OnHasPendingCassetteChanged;
        _sessionOpen.OnValueChanged -= OnSessionOpenChanged;
        _tapeClockBase.OnValueChanged -= OnTapeClockChanged;
        _zoomDir.OnValueChanged -= OnZoomDirChanged;
        _buttonPressPulse.OnValueChanged -= OnButtonPressPulseChanged;
        _crackStageFront.OnValueChanged -= OnCrackStageChanged;
        _crackStageBack.OnValueChanged -= OnCrackStageChanged;
        _zoomFov.OnValueChanged -= OnZoomFovChanged;
        _batteryLevel.OnValueChanged -= OnBatteryLevelChanged;
        base.OnNetworkDespawn();
    }

    public override void OnDestroy()
    {
        if (IsSpawned)
        {
            _recordingState.OnValueChanged -= OnRecordingStateChanged;
            _cameraMode.OnValueChanged -= OnCameraModeChanged;
            _nightVisionOn.OnValueChanged -= OnNightVisionChanged;
            _blanksRemaining.OnValueChanged -= OnBlanksRemainingChanged;
            _hasPendingCassette.OnValueChanged -= OnHasPendingCassetteChanged;
            _sessionOpen.OnValueChanged -= OnSessionOpenChanged;
            _tapeClockBase.OnValueChanged -= OnTapeClockChanged;
            _zoomDir.OnValueChanged -= OnZoomDirChanged;
            _buttonPressPulse.OnValueChanged -= OnButtonPressPulseChanged;
            _crackStageFront.OnValueChanged -= OnCrackStageChanged;
            _crackStageBack.OnValueChanged -= OnCrackStageChanged;
            _zoomFov.OnValueChanged -= OnZoomFovChanged;
            _batteryLevel.OnValueChanged -= OnBatteryLevelChanged;
        }

        UnsubscribeInput();
        TryStopRecording();
        if (_nearBlurShort) RestoreNearBlur();

        if (_camera != null) _camera.targetTexture = null;
        if (_backCamera != null) _backCamera.targetTexture = null;

        if (_feedTexture != null)
        {
            _feedTexture.Release();
            Destroy(_feedTexture);
            _feedTexture = null;
        }

        if (_ambientProbeRt != null)
        {
            _ambientProbeRt.Release();
            Destroy(_ambientProbeRt);
            _ambientProbeRt = null;
        }
        if (_rawProbeRt != null)
        {
            _rawProbeRt.Release();
            Destroy(_rawProbeRt);
            _rawProbeRt = null;
        }

        if (_occlusionBlurTempA != null) { _occlusionBlurTempA.Release(); Destroy(_occlusionBlurTempA); _occlusionBlurTempA = null; }
        if (_occlusionBlurTempB != null) { _occlusionBlurTempB.Release(); Destroy(_occlusionBlurTempB); _occlusionBlurTempB = null; }
        if (_occlusionBlackTexture != null) { Destroy(_occlusionBlackTexture); _occlusionBlackTexture = null; }
        _occlusionVignetteTexture = null;
        if (_occlusionOverlayMaterial != null) { Destroy(_occlusionOverlayMaterial); _occlusionOverlayMaterial = null; }
        if (_focusBlend != null) { Destroy(_focusBlend); _focusBlend = null; }

        ReleaseExposureResources();

        ReleaseRainRts();

        ReleaseCrackRts();
        if (_crackBakeMat != null) { Destroy(_crackBakeMat); _crackBakeMat = null; }
        if (_glassApplyMat != null) { Destroy(_glassApplyMat); _glassApplyMat = null; }

        ReleaseNightVision();
        if (_nvMat != null) { Destroy(_nvMat); _nvMat = null; }

        ReleaseWeatherHold();

        ReleaseCamcorderRts();
        if (_camcorderMat != null) { Destroy(_camcorderMat); _camcorderMat = null; }
        if (_screenMaterial != null) { Destroy(_screenMaterial); _screenMaterial = null; }

        if (_veilMat != null) { Destroy(_veilMat); _veilMat = null; }

        base.OnDestroy();
    }

    public override void LateUpdate()
    {
        base.LateUpdate();

        if (this == null)
        {
            return;
        }

        UpdateDecorLift();

        SyncInside();
        UpdateUnderwater();
        UpdateChainClose();
        CheckWorthless();
        UpdateAudioDuckGain();
        UpdateCrackDetection();
        UpdateNightVisionTransition();
        UpdateFear();
        UpdateFilmingVisuals();
        UpdateParts();
        UpdateNearBlur();

        bool isHeld = playerHeldBy != null;
        if (isHeld != _wasHeldLastFrame)
        {
            OnHeldChanged(isHeld);
            _wasHeldLastFrame = isHeld;
        }

        if (isHeld)
        {
            if (isPocketed != _wasPocketedLastFrame)
            {
                PauseOnPocket(isPocketed);
                CloseOnPocket(isPocketed);
                ApplyLedVisuals();
                _wasPocketedLastFrame = isPocketed;
            }

            bool seated = playerHeldBy != null && !isPocketed && IsInCruiser(playerHeldBy);
            if (seated != _wasSeatedLastFrame)
            {
                PauseOnSeat(seated);
                if (seated) Stow();
                CloseOnPocket(seated);
                ApplyLedVisuals();
                _wasSeatedLastFrame = seated;
            }
        }
        else
        {
            _wasPocketedLastFrame = false;
            _wasSeatedLastFrame = false;
        }

        bool isEquipped = isHeld && !isPocketed && !_wasSeatedLastFrame;
        bool localCapturing = _recorder != null && _recorder.IsActive && _recorder.IsCaptureEnabled
            && _recorder.CurrentFinalePhase == Recording.Recorder.FinalePhase.None;

        ApplyScreenPhase(GetScreenPhase());

        bool screenNeedsFeed = _screenPhase == ScreenPhase.Live && !isPocketed && !_wasSeatedLastFrame && IsPanelLit && IsScreenVisible();
        bool needFeed = localCapturing || screenNeedsFeed;
        bool screenAwake = _screenPhase != ScreenPhase.Off;
        FrameCapture? capture = localCapturing ? GetDrivingCapture() : null;
        bool renderFeedThisFrame = needFeed && (capture != null
            ? ShouldRenderCapture(capture)
            : ShouldRenderFeed(!localCapturing && _screenFar));
        UpdateWeatherHold(needFeed);
        if (_camera != null && _camera.enabled) _camera.enabled = false;

        bool glowOn = screenAwake && !isPocketed && !_wasSeatedLastFrame && ScreenOpenAmount > Constants.Visual.ScreenGlowOpenStart;
        if (_screenGlowLight != null && _screenGlowLight.enabled != glowOn)
        {
            _screenGlowLight.enabled = glowOn;
        }
        if (glowOn)
        {
            UpdateScreenGlow();
        }

        if (isEquipped)
        {
            UpdateZoom();
            UpdateHints();
            MirrorPlayerCamera();
            AlignBackCamera();
            UpdateCameraBob();
            ApplyRemoteFeedViewpoint();
            UpdateCameraOcclusion(renderFeedThisFrame);
            UpdateAutoFocus(renderFeedThisFrame);
            UpdateExposureAdaptation();
        }
        else
        {
            StopZoomSfx();
            _afBlur = 0f;
            _afAreaError = 0f;
            _afBreath = 0f;
            ResetExposure();
            if (!isHeld)
            {
                UpdateCameraOcclusion(renderFeedThisFrame);
                UpdateHoverState();
            }
        }

        if (renderFeedThisFrame)
        {
            FeedRenderer.Request(this);
        }

        UpdateHandlingSfx(isEquipped);
        UpdateRecordingRunSfx();
        UpdateSfx();

        if (_recordingState.Value && _bountyTracker != null && _recorder != null && _recorder.IsActive)
        {
            _bountyTracker.Update(Time.deltaTime);
        }
    }
}
