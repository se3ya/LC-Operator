using UnityEngine;

namespace Operator;

internal static class Constants
{
    internal static class PluginMetadata
    {
        public const string ContentNamespace = "seeya";
        public const string ItemKey = "video_camera";
        public const string Description = "Camcorder to record, rewatch and sell cassettes.";
    }

    internal static class Assets
    {
        public const string BundleFileName = "operatorassets";
        public const string ItemAssetName = "VideoCameraItem";
        public const string HoldClipName = "CameraHoldPose";
        public const string HoldLeftClipName = "CameraHoldLeft";
        public const string NightVisionShaderName = "NightVision";
        public const string MaterialRedLightName = "RedLight";
        public const string MaterialBlackLightName = "BlackLight";
        public const string MaterialPauseLightName = "PauseMaterial";
        public const string PrefabScreenTextureName = "CamScreenTexture";

        // prefab names
        public const string PocketObjectName = "PocketHinge";
        public const string PocketOpenAnimationName = "PocketOpen";
        public const string PocketCloseAnimationName = "PocketClose";
        public const string PocketEjectAnimationName = "PocketEject";
        public const string ScreenShellObjectName = "ScreenGroup";
        public const string ScreenShellAnimationName = "ScreenOpening";
        public const string ScreenShellCloseAnimationName = "ScreenClosing";
        public const string ScreenHingeObjectName = "ScreenHinge";
        public const string ScreenRendererObjectName = "screenplane";
        public const string ScreenLightObjectName = "ScreenLight";
        public const string FilmObjectName = "FilmGroup";
        public const string FilmSpinAnimationName = "FilmSpin";
        public const string RedLightObjectName = "RedLight";
        public const string RedLight2ObjectName = "RedLight2";
        public const string RecordLampObjectName = "recordlamp";
        public const string WebcamLampObjectName = "webcamlamp";
        public const string TallyLampObjectName = "tallyFront";
        public const string WebcamObjectName = "WebcamGroup";
        public const string WebcamGlassObjectName = "webcamglass";
        public const string WebcamOpenAnimationName = "WebcamOpen";
        public const string WebcamCloseAnimationName = "WebcamClose";
        public const string RecordButtonObjectName = "playbutton";
        public const string FlipButtonObjectName = "flipbutton";
        public const string NightVisionButtonObjectName = "nvbutton";
        public const string ZoomInButtonObjectName = "zoombuttonplus";
        public const string ZoomOutButtonObjectName = "zoombuttonminus";
        public const string ButtonPressClipSuffix = "Press";
        public const string ButtonDownClipSuffix = "Down";
        public const string ButtonUpClipSuffix = "Up";
        public const string EjectAnchorObjectName = "EjectAnchor";
        public const string LensCoverObjectName = "LensCover";
        public const string LensCoverOpenAnimationName = "LensCoverOpen";
        public const string LensCoverCloseAnimationName = "LensCoverClose";
        public const string FocusRingObjectName = "focusring";
        public const string FocusWheelObjectName = "focuswheel";
        public const string ZoomRingObjectName = "zoomring";
        public const string NeedleObjectName = "NeedlePivot";
        public const string IrLampObjectName = "irlamp";
        public const string CoilObjectName = "dcCoil";
        public const string ButtonOnClipSuffix = "On";
        public const string ButtonOffClipSuffix = "Off";
        public static readonly string[] TapeReelObjectNames = { "reeltape1", "reeltape2" };

        public const string CameraHandlingClipName = "camerahandling";
        public const string ZoomClipName = "zoom";
        public const string RecordingRunClipName = "filmcamerarunning";
        public const string ButtonPressClipName = "buttonpress";
        public const string ButtonHoldClipName = "buttonhold";
        public const string CassetteEjectClipName = "cassetteeject";
        public const string NightVisionOffClipName = "shutterclick";
        public static readonly string[] LensBreakClipNames = { "lensbreak", "lensbreak2" };

        // beep beep
        public const string RecordingStartClipName = "ComputerBeep3";
        public const string RecordingPauseClipName = "ComputerBeep1";
        public const string RecordingResumeClipName = "ComputerBeep6";
    }

    internal static class Network
    {
        // bump on any wire change, rpc changes drop silently, netvar changes misframe and bounty rows corrupt without either
        public const int NetcodeVersion = 11;
        public const string NetcodeVersionField = "opnet:";
    }

    internal static class ClipTransfer
    {
        public const int ChunkSize = 32768; // wire format, change needs netcode bump
        public const int MaxChunks = 65536; // 2 gib at 32 kib chunks
        public const int MaxResendChunks = 256; // receiver rejects more
        public const int MaxActiveClips = 16;
        public const int MaxEvictWaits = 3;

        public const int SendRate = 10; // nominal, waitforseconds rounds up to frame
        public const int FlowWindowChunks = 8; // 256 kib in flight, steam reliable buffer is about 512 kib
        public const int AckIntervalChunks = 4;
        public const float FlowPollInterval = 0.05f;
        public const float FlowStallTimeout = 30f;
        public const float SenderCacheTime = 180f;

        public const float RetryDelay = 2f;
        public const int MaxRetries = 8;
        public const float RecoveryTimeout = 120f; // silence timeout, resets on each chunk
        public const float MinRecoveryRate = 3f;
    }

    internal static class ConfigRanges
    {
        public const int StoreCostMin = 20;
        public const int StoreCostMax = 9999;

        public const float CameraMinFovMin = 10f;
        public const float CameraMinFovMax = 70f;
        public const float CameraMaxFovMin = 10f;
        public const float CameraMaxFovMax = 90f;
        public const float CameraZoomSpeedMin = 5f;
        public const float CameraZoomSpeedMax = 120f;
        public const float CameraBobIntensityMin = 0f;
        public const float CameraBobIntensityMax = 3f;
        public const float ScreenEmissionMin = 0f;
        public const float ScreenEmissionMax = 10f;
        public const float IdleBatteryDrainMin = 0f;
        public const float IdleBatteryDrainMax = 2f;

        public const int FeedRenderFpsMin = 24;
        public const int FeedRenderFpsMax = 60;
        public const int FpsMin = 12;
        public const int FpsMax = 60;
        public const int CrfMin = 16;
        public const int CrfMax = 32;
        public const int AudioBitrateMin = 16;
        public const int AudioBitrateMax = 320;
        public const int MaxMinutesMin = 1;
        public const int MaxMinutesMax = 60;
        public const int MaxQueuedFramesMin = Recording.RetainedFrameBuffers;
        public const int MaxQueuedFramesMax = 600;

        public const int FlatBaseValueMin = 0;
        public const int FlatBaseValueMax = 100;
        public const int DeadBodyPenaltyMin = 0;
        public const int DeadBodyPenaltyMax = 100;
        public const float MinBountyTimeMin = 0f;
        public const float MinBountyTimeMax = 15f;
        public const int CassettesPerOrbitMin = 1;
        public const int CassettesPerOrbitMax = 10;

        public const float TelevisionLightMin = 0f;
        public const float TelevisionLightMax = 5f;
        public const float MonitorLightMin = 0f;
        public const float MonitorLightMax = 5f;
    }

    internal static class Parts
    {
        public const float RingSweep = 79f;
        public const float FocusNearDiopter = 1.25f;
        public const float FocusWheelRatio = 3.48f;
        public const float NeedleEmptyAngle = 50f;
        public const float NeedleFullAngle = -50f;
        public const float NeedleSpeed = 3f;
        public const float NeedleTremble = 0.9f;
        public const float NeedleTrembleRate = 17f;
        public const float CoilStiffness = 140f;
        public const float CoilDamping = 5f;
        public const float CoilMaxOffset = 0.012f;
        public static readonly Color IrLampColor = new(0.5f, 0f, 0f, 1f);
    }

    internal static class Camera
    {
        public const float ShellOpenDelay = 0.5f;
        public const float SlotOpenDelay = 0.3f;
        public const int ClipMatchSteps = 12;
        public const int ClipMatchPasses = 3;
        public const float ClipMatchAngleWeight = 0.0002f;
        public const float ShellCloseCut = 0.63f; // closing clip turns panel away here
        public const float DefaultFov = 65f;
        public const float HeldNearBlurEnd = 0.2f;

        public const float ScreenAspect = 16f / 9f;
        public static readonly Color ScreenOffAlbedo = new(0.020f, 0.023f, 0.021f, 1f);
        public static readonly Color ScreenLitBaseTint = new(0.03f, 0.03f, 0.03f, 1f);

        public const float HoldBlendTime = 0.25f;
        public const float HoldReleaseTime = 0.06f;
        public const ushort HoldSortingOrder = 500;
        public const ushort HoldLeftSortingOrder = 501;
        public const float HoldLeftReleaseTime = 0.12f;
        public const float HoldSteadiness = 0.8f;
        public const float HoldSteadyFade = 0.15f;
        public const float HoldSteadyReach = 0.35f;
        public const float HoldRestRate = 0.2f;
        public const float HoldRestDelay = 0.5f;
        public const float HoldRestSpeed = 0.3f;
        public const float HoldRestHeight = -0.294f;
        public const float HoldRestDepth = 0.585f;
        public const float ScreenOpenAngle = 110f;
        public const int FloorYOffset = -90;

        public const float MaxEjectDistance = 8f;
        public const float PocketOpenSettle = 0.25f;

        public const float EjectLift = 0.06f;
        public const float EjectLiftTime = 0.10f;
        public const float EjectCrossfade = 0.06f;
        public static readonly Vector3 DecorTurn = new(0f, 180f, 0f);
        public const float EjectUpSpeed = 2.4f;
        public const float EjectClearance = 0.05f;
        public const float EjectDistance = 0.45f;
        public const float EjectSpread = 0.08f;
        public const float EjectProbeRadius = 0.08f;
        public const float EjectWallGap = 0.1f;
        public const float EjectSpin = 90f;
        public const float EjectSpinJitter = 30f;
        public const float EjectHop = 0.035f;
        public const float EjectHopTime = 0.12f;
        public const float EjectRock = 4f;
        public const float EjectRockTime = 0.2f;
        public const float EjectPinTimeout = 1.0f;
        public const float EjectPinDistance = 0.005f;
        public const float EjectPinAngle = 0.5f;
    }

    internal static class Battery
    {
        public const float DrainInterval = 0.25f;
        public const float IdleDrainMultiplier = 0.4f;
        public const float RecordDrainMultiplier = 0.4f;
        public const float CapacityScale = 2f; // 360 s pack to 12 min, outlasts 10 min tape
    }

    internal static class Visual
    {
        public const int FeedRenderFps = 30;
        public const float FeedFarClipPlane = 300f;
        public const float FarScreenSize = 8f;
        public const float FarScreenRate = 4f;
        public const float HiddenFogMeanFreePath = 1000000f; // near zero density while staying registered
        // never add EnemiesNotRendered, fpv body layer
        public const string PreviewExcludeLayerNames = "ScanNode,LineOfSight,MapRadar,HelmetVisor,UI";
        public const float WaterRescanInterval = 5f; // level volumes static, flood keeps trigger as it rises
        public const float UnderwaterFade = 10f;
        public const float UnderwaterFadeFloor = 0.05f;

        public const float RemoteLensForward = 1.02f;
        public const float RemoteLensRight = 0.467f;
        public const float RemoteLensDown = 0.185f;
        public const float RemoteLensReach = 3f; // rejects nonsense socket only

        public const float BarrelMax = 0.2f;
        public const int CamcorderIdleReleaseFrames = 120;

        // veiling glare
        public const float GlareThresholdCalm = 0.9f;
        public const float GlareThresholdAfraid = 0.9f;
        public const float GlareKnee = 0.1f;
        public const float GlareOverRange = 0f;
        public const float GlareHaloCalm = 0.2f;
        public const float GlareHaloAfraid = 0.3f;
        public const float GlareVeilCalm = 0.01f;
        public const float GlareVeilAfraid = 0.03f;
        public const float GlareSmearCalm = 0.05f; // interline ccd smear
        public const float GlareSmearAfraid = 0.10f;
        public const float GlareNearStep = 0.6f;
        public const float GlareMidStep = 1f;
        public const float GlareFarStep = 1f;
        public const float GlareSmearStep = 1f;
        public const float GlareSkipBelow = 0.02f;

        public const float ScreenGlowRange = 1.3f;
        public const float ScreenGlowSpotAngle = 150f;
        public const float ScreenGlowInnerSpot = 0.1f;
        public const float ScreenGlowWhite = 60f;
        public const float ScreenGlowFloor = 0.1f;
        public const float ScreenGlowDarkPeak = 0.0001f;
        public const float ScreenGlowOpenStart = 0.6f;
        public const float ScreenGlowSelfieScale = 0.5f;
        public const float ScreenGlowSelfieMax = 3f;
        public const float ScreenGlowSampleInterval = 0.04f;
        public static readonly Vector3 ScreenGlowOffset = new(0.01f, 0f, 0f);
        public static readonly Vector3 ScreenGlowTurn = new(0f, 90f, 0f);

        public const int ScreenGlowProbeSize = 64;
        public const float ScreenGlowResponseSpeed = 10f;

        public const int RawProbeSize = 16;
        public const float RawSampleInterval = 0.08f;
        public const float RawResponseSpeed = 6f;

        public const float LampIntensity = 0.1f;
        public const float LampRange = 0.02f;
        public static readonly Color LampColor = new(1f, 0.05f, 0.02f);

        public const string OcclusionLayerNames = "Default,Room,Terrain,Colliders,InteractableObject,DecalStickableSurface";
        public const float OcclusionProbeRadius = 0.05f;
        public const float OcclusionSelfHitEpsilon = 0.02f;
        public const float OcclusionFadeDistance = 1f;
        public const float OcclusionNightVisionOnset = 0.5f;
        public const float OcclusionGrazeFloor = 0.15f;
        public const float OcclusionNightVisionSlew = 16f;
        public const float OcclusionNearPlaneEngage = 0.01f;
        public const float OcclusionNearPlaneRelease = 0.04f;
        public const float OcclusionDroppedReach = 0.78f;
        public const string OcclusionVignetteAsset = "OcclusionVignette";
        public const string OsdFontAsset = "OsdFont";

        public static readonly bool BobEnabled = true;
        public const float BobWalkFrequency = 5.5f;
        public const float BobRunFrequency = 8.5f;
        public const float BobWalkPitch = 1.2f;
        public const float BobRunPitch = 4.0f;
        public const float BobWalkRoll = 0.7f;
        public const float BobRunRoll = 2.4f;

        // weather
        public const float WeatherScanInterval = 2f; // rescans outdoors only
        public const float WeatherRetryDelay = 3f;
        public const float CoverCheckInterval = 0.75f;
        public const float CoverCheckDistance = 100f;
        public const float MeteorSpriteDistance = 380f; // vanilla MeteorShowers parks distant sprites at 380
        public const int WeatherCameraLayer = 31;
    }

    internal static class Exposure
    {
        public const float ApplyEpsilon = 0.01f; // skip pass when multiplier is this close to 1
        public const float RelaxSpeed = 2.5f;
        public const float MaxDeltaTime = 0.05f;
        public const float EvFloor = 0.0001f; // linear luma, about -13 ev
        public const float MaxSlewRate = 10f;
        public const float SettleTime = 0.5f;

        public const float RealTauRise = 0.55f;
        public const float RealTauFall = 0.45f;
        public const float RealDeadOver = 0.5f;
        public const float RealDeadUnder = 0.75f;
        public const float RealMaxOver = 2f;
        public const float RealMaxUnder = 2f;

        public const float CasualTauRise = 0.3f;
        public const float CasualTauFall = 0.25f;
        public const float CasualDeadOver = 0.75f;
        public const float CasualDeadUnder = 1f;
        public const float CasualMaxOver = 1f;
        public const float CasualMaxUnder = 1f;
    }

    internal static class Focus
    {
        public const float AutoFocusSpeed = 6f;
        public const float RackSpeed = 2.4f;
        public const float DarkRackMultiplier = 0.45f;
        public const float ReactionDelay = 0.22f;
        public const float RetargetThreshold = 0.18f;
        public const float DriftSpeed = 0.35f;
        public const float AreaOffset = 0.1f;
        public const float AreaSmoothing = 0.15f;
        public const float AreaRackSmoothing = 0.03f;
        public const float BlurFade = 0.4f;

        public const float HuntDuration = 0.9f;
        public const float HuntCycles = 2.2f;
        public const float HuntAmplitude = 0.55f;
        public const float ZoomSettleTime = 0.15f;
        public const float MinIdleHuntInterval = 20f;
        public const float MaxIdleHuntInterval = 50f;
        public const float DarkHuntTimeScale = 0.4f;
        public const float DarkLumaThreshold = 0.16f;

        public const float ManualRackSpeed = 1.4f;
        public const float ManualNearLimit = 0.3f;
        public const float ManualFarLimit = 120f;

        public const float BlurPerDiopter = 0.75f;
        public const float BlurStepMax = 0.9f; // half res texels
        public const float ZoomBlurPower = 0.5f;
        public const float MaxZoomBlur = 4f;

        // focus breathing
        public const float BreathScale = 0.0022f;
        public const float BreathMax = 0.008f;
        public const float BreathResponse = 6f;
    }

    internal static class NightVision
    {
        public const float SwitchTime = 0.05f;
        public const float HoldTime = 0.30f;
        public const float SettleTime = 0.60f; // sweep duration
        public const float DisableDelay = 0.12f;
        public const float BloomStrength = 0.5f;
        public const float BandWidth = 0.30f;
        public const float BandDarkness = 0.05f; // multiplier
        public const float ShakeAmplitude = 0.006f;
        public const float ShakeFrequency = 55f; // rad/s
        public const float OffClickVolume = 0.8f;
        public const float OffClickStart = 0.150f; // skip clip lead in

        public static readonly bool UseFeedVolume = true;
        public const float VolumePriority = 10000f;
        public const float LayerRetryInterval = 2f;
        public const float FixedExposure = 1f; // ev100 game pins player camera to
        public const float SelfieGainMultiplier = 0.85f;

        public static readonly float[] SlabYaws = { 0f, 72f, -72f };
        public const float SlabIntensity = 0.68f;
        // sqrt(1 - 0.42^2) * (1 + 2 cos 72), recompute when yaws or base down change
        public const float SlabFaceOnScale = 1.468f;
        public const float SlabRange = 52f; // backset plus 30 m of reach
        public const float SlabBackset = 22f; // short backset shows origin planes as triangle
        public const float SlabCrossSection = 160f;
        public const float SlabBaseDownY = 0.42f;
        public const float SlabPitchFollow = 0.55f;
        public const float SlabMinDownY = -0.30f;
        public const float SlabMaxDownY = 0.78f;

        public const float NearLift = 1.6f;
        public const float NearLiftDistance = 0.5f;
        public const float NearShapeRadius = 0.15f;
        public const float NearRange = 6f;
        public static readonly Vector3 NearOffset = new(0.04f, -0.03f, 0.06f);

        public const float BatteryDrainMultiplier = 1f;
        public const float LowBatteryDimStart = 0.25f;
        public const float LowBatteryFloor = 0.35f;
        public const float LowBatteryFlicker = 0.12f;

        public static readonly bool AutoGain = true;
        public const float AgcTargetLuma = 0.20f;
        public const float AgcMeanWeight = 0.60f;
        public const float AgcAttackTime = 0.10f;
        public const float AgcReleaseTime = 1.6f;
        public const float AgcSampleInterval = 0.04f;
        public const float GrainGainQuiet = 0.30f;
        public const float GrainGainStraining = 1.8f;

        public static readonly Color Tint = new(0.70f, 1f, 0.44f);
        public const float ChannelHeadroom = 1.6f;
        public const float DarkLift = 0.58f; // gamma on luma
        public const float ToneKnee = 0.35f;
        public const float ToneScale = 1.12f;
        public const float GrainAmount = 0.17f;
        public const float NoiseFloor = 0.03f;
        public const float GrainFps = 12f;
        public const float Dsnu = 0.22f; // dark signal non uniformity
        public const float HotPixelRate = 0.0006f;
        public const float HaloStrength = 0.35f;
        public const float HaloThreshold = 0.62f;
        public const float HaloMip = 2f;
        public const float SmearThreshold = 0.97f;
        public const float SmearStrength = 1.2f;
        public const float SmearLod = 4f; // log2 of 16 shader taps
        public const float Persistence = 0.35f;
        public const float VignetteMultiplier = 1f;
    }

    internal static class Cracks
    {
        public const string BakeShaderAssetName = "CrackBake";
        public const string ApplyShaderAssetName = "CrackApply";
        public const int ShaderRevision = 4;
        public const float MinFallHeight = 5f;
        public const int HeavyDamage = 45;
        public const float HitCooldown = 0.2f;
        public const float DropGrace = 1.5f;
        public const float EffectIntensity = 1f;
        public const float FaceDepth = 1f;

        public const float BakeFlipY = 0f;
        public const int MaskBakeWidth = 1280; // supersample target
        public const int MaskSupersampleMax = 4;
        public const float CrushRadius = 0.030f;
        public const float CrushStrength = 0.85f;

        public const float Warp = 0.40f;
        public const float ChromaticAberration = 0.05f;
        public const float Darken = 0.62f;
        public const float Glow = 0.90f;
        public const float WetGlintBoost = 0.25f;
        public const float SpecLo = 0.34f;
        public const float SpecHi = 0.90f;
        public const float Milk = 0.18f; // powdered look in crush pits

        public const float LampRescan = 2f;
        public const float LampRank = 0.2f;
        public const float LampFade = 4f;
        public const float LampNear = 0.6f;
        public const float LampRef = 600f;
        public const float LampGain = 8f;
        public const float LampFloor = 0.01f;
        public const float LampSoftCap = 1.5f;
        public const float LampSize = 0.15f;
        public const float LampMinRadius = 0.5f;
        public const float LampMaxRadius = 4f;
        public const float SunRadius = 0.27f;
        public const float SunRef = 2f;
        public const float SunGain = 1.2f;
    }

    internal static class RainFeed
    {
        public const float Amount = 0.85f;
        public const float Wetness = 0.6f; // drop spawn density scale
        public const float WetRampSpeed = 0.4f;
        public const float DryTime = 20f;
        public const float LookUpBaseDensity = 0.85f;

        public const float DropSize = 2f;
        public const float SlideSpeed = 1f;
        public const float LookUpBaseSpeed = 0.6f;
        public const float Inertia = 1f;
        public const float WindGain = 10.0f;
        public const float WindMax = 35.0f;
        public const float WindTau = 0.10f;
        public const float PitchFallGain = 0.06f;
        public const float PitchFallClamp = 0.15f;

        public const int FusionIterations = 1;
        // photoshop levels on 0 to 255 height
        public const float LevelInBlack = 90f;
        public const float LevelInWhite = 140f;
        public const float LevelOutBlack = 0f;
        public const float LevelOutWhite = 255f;

        // dropplets
        public const float Distortion = 0.5f;
        public const float RefractGain = 0.55f;
        public const float SinContactAngle = 0.94f; // sin 70 deg
        public const float MipMax = 5f;
        public const float RimDark = 0.55f;
        public const float Fresnel = 0.12f;
        public const float Specular = 0.15f;
    }

    internal static class Osd
    {
        public const float ReferenceHeight = 180f; // rt height per font scale step
        public const float Alpha = 0.92f;
        public static readonly bool FlipVertical = false;
        public const float RecBlinkRate = 1f;
        public const float ZoomShowTime = 1.5f;

        public static readonly Color White = new(0.93f, 0.95f, 0.94f, 1f);
        public static readonly Color Grey = new(0.72f, 0.76f, 0.74f, 1f);
        public static readonly Color Red = new(1f, 0.23f, 0.16f, 1f);
        public static readonly Color Amber = new(1f, 0.8f, 0.25f, 1f);
        public static readonly Color NightshotGreen = new(0.35f, 1f, 0.45f, 1f);
        public static readonly Color FocusGreen = new(0.35f, 1f, 0.45f, 1f);
        public static readonly Color MeterFill = new(0.35f, 0.95f, 0.75f, 1f);
        public static readonly Color BatteryFill = new(0.55f, 1f, 0.35f, 1f);
        public static readonly Color TrackBack = new(0f, 0f, 0f, 0.45f);

        public const float MeterFallSpeed = 2.2f;
        public const float MeterFloor = 40f; // meters span -40 db to full scale like recorders own

        public const float LowBatteryLevel = 0.10f;
        public const int TapeLowMinutes = 1;
        public const int BatteryMinutesCap = 999;
        public const string BatteryMinutesUnknown = "----";

        public const float AfReticleThreshold = 0.04f;
        public const float AfReticleRelease = 0.02f;
        public const float AfReticleHold = 0.25f;
        public const float AfReticleHalf = 14f;
    }

    internal static class Sfx
    {
        public const float RecordingRunStart = 1f;
        public const float RecordingRunEnd = 40f;
        public const float ButtonPressEnd = 0.350f;
        public const float ButtonHoldPressEnd = 0.150f;
        public const float ButtonHoldReleaseEnd = 0.500f;
        public const float CassetteEjectEnd = 0.800f;
        public const float HandlingMoveSpeedThreshold = 0.6f;
        public const float BatteryDeathSpinTime = 0.9f;
        public const float BatteryDeathEndPitch = 0.25f;

        public const float HandlingOwnVolume = 0.35f;
        public const float HandlingBystanderVolume = 0.25f;
        public const float ZoomOwnVolume = 0.2f;
        public const float ZoomBystanderVolume = 0.25f;
        public const float RecordingRunOwnVolume = 0.3f;
        public const float RecordingRunBystanderVolume = 0.2f;
        public const float ButtonPressOwnVolume = 0.3f;
        public const float ButtonPressBystanderVolume = 0.15f;
        public const float ButtonHoldOwnVolume = 0.2f;
        public const float ButtonHoldBystanderVolume = 0.1f;
        public const float CassetteEjectOwnVolume = 0.5f;
        public const float CassetteEjectBystanderVolume = 0.4f;
        public const float LensBreakOwnVolume = 0.7f;
        public const float LensBreakBystanderVolume = 0.55f;
    }

    internal static class Recording
    {
        public const string UnsavedFolderName = "Unsaved";

        public const float MinDuration = 0.5f;
        public const double MinDurationGrace = 15.0;
        public const long MinOutputSize = 1024;
        public const float EncoderFinalizeTimeout = 120f;
        public const float EncoderStallTimeout = 15f; // output stopped growing
        public const int StderrEchoLimit = 8;
        public const double MergeShortfall = 0.5; // ffmpeg complaining is only fatal if footage is missing

        public const int RetainedFrameBuffers = 48;
        public const float MinPaybackSlack = 0.04f;
        public const float MaxPaybackSlack = 0.15f;
        public const float JitterLimit = 0.25f;
        public const float IdleWakeInterval = 0.01f;
        public const int PrewarmedFrameBuffers = 24;
        public const int MaxDupBurst = 3; // big bursts repaying deficit freeze picture
        public const float MaxEndFill = 5f;
        public const float MaxLookahead = 0.05f;
        public const int FallbackDeficit = 2;

        public const float DiagnosticsWindow = 5f;
        public const float DiagnosticsStallThreshold = 100f;
        public const long SlowSpawnThreshold = 100;

        public const string FfmpegDownloadUrl =
            "https://github.com/GyanD/codexffmpeg/releases/download/7.0.2/ffmpeg-7.0.2-essentials_build.zip";
        public const int FfmpegDownloadAttempts = 3;
        public const int FfmpegValidateTimeout = 10000;
        public const int FfmpegValidateTimeoutWine = 30000;
        public const int PipeConnectTimeout = 15000;
        public const int PipeConnectTimeoutWine = 45000;
        public const string InputProbeArgs = "-analyzeduration 0 -probesize 32";
        public const string OutputSyncArg = "-fps_mode cfr";
        public const string ShortestArg = "-shortest -shortest_buf_duration 0.5";
        public const string ShortestPlainArg = "-shortest";
        public const string OutputFormatArg = "-f mp4"; // merge is staged as .part

        // nvenc rejects smaller frames
        public const int EncoderProbeMinWidth = 256;
        public const int EncoderProbeMinHeight = 144;
        public const int EncoderProbeTimeout = 8000;
        public const int EncoderRetryDelay = 2000;
        public const int EncoderErrorLines = 20;
        public const double AmfBaseRate = 700000;
        public const double AmfBaseArea = 426 * 240;
        public const double AmfAreaPower = 0.85;
        public const int AmfQpOffset = 5;
        public const int MaxQp = 51;
        public const int WarmupFrameCount = 3;

        public const int StitchProbeTimeout = 10000;
        public const string SeamFade = "0.025";
        public const int SeamDelay = 25; // must match SeamFade
    }

    internal static class Audio
    {
        public const float BleedGain = 0.1f;
        public const float DuckSmoothing = 8f;
        public const float UnderwaterCutoff = 700f;
        public const float UnderwaterGlideRate = 10f;

        public const int ShadowMaxSources = 64;
        public const float ShadowRadius = 45f;
        public const float ShadowScanInterval = 0.25f;
        public const int ShadowAdmitPerScan = 4; // each admit decodes pcm on main thread
        public const float ShadowMinClipLength = 0.8f; // non looping only
        public const float ShadowMinResumeLength = 3f; // one shots this long resume mid clip at record start
        public const float ShadowOrphanTime = 12f; // min hold for loop game muted on operators side
        public const float ShadowPeakDecay = 0.5f; // per second

        public const int ShadowVoiceCount = 96; // software mixed, not unity voices
        public const int ShadowResyncFrames = 4800; // clip frames, 0.1 s at 48 khz
        public const float ShadowFreezeDelay = 0.075f;
        public const long ShadowClipCacheSize = 201326592L; // 192 mib
        public const int ShadowClipReadAttempts = 8; // sound mod clips are often stilll loading
        public const float ShadowLiveRefreshInterval = 0.4f; // mirage writes its clip while it plays
        public const float ShadowCurveRefreshInterval = 5f; // jittered to 2x, reading curves per frame crashes client
        public const float ShadowRolloffScale = 1f;

        public const float ShadowSfxGroupGain = 1.2589f; // +2 db, game sfx mixer group
        public const float ShadowVoiceGroupGain = 1.5849f; // +4 db, game voice player groups
        public const float ShadowCentreGain = 0.70710678f; // 1/sqrt(2), measured on flat mono
        public const float ShadowSpreadFlatGain = 0.5f; // measured, any spread drops another 3 db
        public const float ShadowWeatherIndoorGain = 0.1f;
        public const float ShadowOwnCameraGain = 0.5f; // louder and mic agc parks on motor

        // occlusion, game OccludeAudio values
        public const float ShadowOccludeCheckInterval = 0.5f;
        public const float ShadowOccludeOpenCutoff = 10000f;
        public const float ShadowOccludeMinCutoff = 900f;
        public const float ShadowOccludeMaxCutoff = 4000f;
        public const float ShadowOccludeScale = 2500f;
        public const float ShadowOccludeGlide = 8f;
        public const float ShadowOccludeRangeSqr = 12100f; // 110 m squared
        public const float ShadowOccludeBypassCutoff = 22000f;
        public const float ShadowOccludeGain = 0.5f;
        public const float ShadowOccludeCloseTime = 0.1f;
        public const float ShadowOccludeClearTime = 1f;
        public const float ShadowOccludeSelfRadius = 0.1f;

        public const float ShadowVoiceScanInterval = 1f;
        public const float ShadowVoiceLevel = 1f;
        public const float HeldRoomDistance = 1.2f;
        public const float NearReference = 0.6f;
        public const float NearExponent = 0.4f;
        public const float NearFloor = 0.5f;
        public const float VoiceRange = 50f;
        public const float VoiceSpread = 30f;
        public const int MicQueueLimit = 12000;
        public const float MicPreRollTime = 0.04f;
        public const float MicFadeTime = 0.01f;
        public const float VoiceGateTime = 0.1f;
        public const float ShadowVoiceFleaCutoff = 500f;
        public const float ShadowVoiceMaskCutoff = 300f;
        public const float ShadowVoiceUnderwaterCutoff = 600f;
        public const float ShadowVoiceDirectivityLow = -4f;
        public const float ShadowVoiceDirectivityHigh = -20f;
        public const float ShadowVoiceDirectivityCorner = 2500f;
        public const float ShadowVoiceEchoDistance = 60f; // slapback beyond this
        public const float ShadowVoiceEchoDelay = 0.5f;
        public const float ShadowVoiceEchoDry = 0.5f;
        public const float ShadowVoiceEchoWet = 0.35f;
        public const float ShadowVoiceEchoFeedback = 0.3f;
        public const float ShadowVoiceEchoRise = 0.2f;
        public const float ShadowVoiceEchoFall = 1f;

        public const float EchoIndoorDistance = 6f; // direct and room equal at this distance
        public const float EchoOutdoorDistance = 40f;
        public const float EchoIndoorDecay = 1.8f;
        public const float EchoOutdoorDecay = 0.6f;
        public const float ShadowReverbDecayInside = 1.8f;
        public const float ShadowReverbDamping = 0.35f;
        public const float ShadowReverbPreDelay = 0.02f;
        public const float ShadowReverbOutputGain = 0.048f; // measured, 8 combs and 4 allpasses add 26 db

        public const float ShadowReportInterval = 5f;
        public const float ShadowCensusInterval = 5f;

        // camcorder mic
        public const float MicHighPass = 120f;
        public const float MicLowPass = 15000f;
        public const float MicPresence = 3200f;
        public const float MicPresenceGain = 4f;
        public const float MicSideHighPass = 5716f; // c / 2d for 3 cm capsule spacing
        public const float MicStereoWidth = 1f;
        public const float MicNominalLevel = 0.12589f; // -18 dbfs
        public const float MicAttackTime = 0.0011f;
        public const float MicHoldTime = 0.25f;
        public const float MicNoiseFloorLevel = 0.000251f; // -72 dbfs, agc release gate
        public const float MicLimitThreshold = 0.1841f; // -14.7 dbfs
        public const float MicLimitAttackTime = 0.001f;
        public const float MicLimitHoldTime = 0.03f;
        public const float MicLimitReleaseTime = 0.15f;
        public const float MicNoiseAmplitude = 0.0006f;
        public const float MicClipDrive = 1.05f;
        public const float MicEchoHeadroom = 8f;
    }

    internal static class Cassette
    {
        public const string ItemAssetName = "CassetteItem";
        public const string PrefabAssetName = "Cassette";
        public const string ItemKey = "video_cassette";
        public const int HudUtilitySlotIndex = 50; // PlayerControllerB uses 50 for utility slot
        public const float ReconcileDelay = 3f; // saved tapes spawn first or reconcile deletes their footage
        public const float EncodeTimeout = 150f;
        public const int MaxSaveNameLength = 64;

        public const int MaxScanRange = 13; // vanilla grabbable scan node
        public const int MinScanRange = 1;
        public const float MinSoundDistance = 1f;
        public const float MaxSoundDistance = 25f;

        public const int FloorRaycastMask = 268437760; // vanilla GetItemFloorPosition mask, layers 8 11 28
        public const float EjectRestHeightOffset = 0.1f;
        public const float RestHeight = 0.016f;

        public const string ColorObjectName = "cassettecolor";
        public const int ColorHueBuckets = 12; // must match baked icons per tier
        public const string IconAssetPrefix = "CassetteIcon_";
        public const string IconLegendaryKey = "l00";
        public const string IconGoldKey = "g00";
        public const string IconHoloKey = "h00";
        public const string IconMisprintKey = "x00";
        public const int ColorUncommonValue = 30;
        public const int ColorRareValue = 80;
        public const int ColorLegendaryValue = 150;
        public const int ColorGoldValue = 250;
        public const int ColorHoloValue = 400;

        public const int StripeRows = 8; // stripe atlas rows, tier enum order
        public const float StripeRowInset = 0.04f; // keeps bilinear taps inside a row
        public const float StripeSmoothness = 0.4f;
        public const float GoldMetallic = 0.45f;
        public const float GoldSmoothness = 0.65f;
        public const float HoloMetallic = 0.35f;
        public const float HoloSmoothness = 0.8f;
        public const float HoloShimmerSpeed = 0.08f;
        public const float HoloShimmerSaturation = 0.25f;
        public const int MisprintChance = 128;
        public const float MisprintRowShift = 0.3f;
    }

    internal static class Bounty
    {
        public const string ConfigSection = "9. Bounties";
        public const int MinBounty = 0;
        public const int MaxBounty = 500;
        public const float RateToCredits = 30f;
        public const float DefaultRate = 1f;
        public const float PowerLevelScale = 0.5f;
        public const float SampleRate = 4f;

        public const float MaxDistance = 45f;
        public const float FramingBonus = 0.25f;
        public const float SharedShotBonus = 0.15f;
        public const float KillBonus = 1f;
        public const float KillWindow = 2.5f;
        public const float FirstFootageBonus = 1f;
        public static readonly Vector2[] FramingKeys =
        {
            new(0f, 0f),
            new(0.05f, 0.3f),
            new(0.15f, 0.7f),
            new(0.4f, 1f),
            new(0.7f, 0.7f),
            new(1f, 0.35f),
        };
        public const int LowPowerCredits = 15;
        public const float OpenTakeGrace = 120f;

        public const float DangerReference = 575f;
        public const float MinDanger = 0.75f;
        public const float MaxDanger = 2f;

        // server prices tape from client sent rows bound both before summing
        public const int MaxRows = 256;
        public const float MaxRowTime = 3600f;
        public const int MaxValue = 250000;
        public const float ValueCeilingHeadroom = 2f;
    }

    internal static class Terminal
    {
        public const string KeywordPlay = "play";
        public const string KeywordStop = "stop";
        public const string KeywordSave = "save";
        public const string KeywordDelete = "delete";
        public const string KeywordList = "cassettes";
        public const string KeywordSlow = "slow";
        public const string KeywordNormal = "normal";
        public const string KeywordFast = "fast";
        public const string KeywordTurbo = "turbo";
        public const string KeywordPause = "pause";
        public const string KeywordResume = "resume";
        public const string KeywordVolume = "volume";
        public const string KeywordTransfer = "transfer";

        public const string EventPlay = "vc_cassette_play";
        public const string EventStop = "vc_cassette_stop";
        public const string EventSave = "vc_cassette_save";
        public const string EventDelete = "vc_cassette_delete";
        public const string EventList = "vc_cassette_list";
        public const string EventHelp = "vc_cassette_help";
        public const string EventSlow = "vc_cassette_speed_slow";
        public const string EventNormal = "vc_cassette_speed_normal";
        public const string EventFast = "vc_cassette_speed_fast";
        public const string EventTurbo = "vc_cassette_speed_turbo";
        public const string EventPause = "vc_cassette_pause";
        public const string EventResume = "vc_cassette_resume";
        public const string EventVolume = "vc_cassette_volume";
        public const string EventTransfer = "vc_cassette_transfer";

        public const float StatusFontScale = 1.6f;
        public const float StatusFallbackFontSize = 28f;
        public const int PlaybackAnswerOffset = 17;
        public const float PlaybackAudioMaxDistance = 18f;
        public const double SiblingCheckInterval = 0.25; // raising image rebuilds canvas, never every frame
        public const double PlaybackHitchThreshold = 100.0;
        public const double PlaybackHitchLogCooldown = 1.0;

        // no total deadline on wait, big clip is slow not broken, must clear ClipTransfer.FlowStallTimeout, healthy sender may send nothing for that long
        public const float TransferSilenceTimeout = 120f;
        public const float MissingFileGrace = 10f; // clips send when take ends, nothing incoming or stitching means never
        public const double FileCheckInterval = 0.25; // resolving name scans received folders
    }

    internal static class Monitor
    {
        public const int TransferIndex = 0;
        public const int ScanLimit = 64;
        public const double PrepareTimeout = 8.0;

        public const double ReapplyInterval = 0.25;
        public const double ReapplyLogCooldown = 5.0;
        public const double StateReportDelay = 2.0;

        public const string ScreenLightPrefabName = "OperatorScreenLight";
        public const string ScreenLightAnchorName = "MonitorWall";
        public static readonly Vector3 ScreenLightLocalPosition = new(-0.175134778f, -1.89715993f, -2.14900017f);
        public static readonly Quaternion ScreenLightLocalRotation = new(0f, -0.596198857f, 0f, 0.802836776f);
        public const float ScreenLightMaxOffset = 0.5f;
        public static readonly Vector3 VanillaScreenLocalCenter = new(-0.1885279f, 0.9308863f, -0.0623014f);
        public static readonly Vector3 VanillaScreenLocalExtents = new(0.2259891f, 0.7443714f, 0.5985336f);

        public const float LightMidLuma = 0.22f;
        public const float LightContrast = 0.75f;
        public const float LightMin = 0.25f;
        public const float LightMax = 3f;
        public const float LightResponseSpeed = 12f;
        public const float LightMaxSaturation = 0.3f;
        public const float LightDarkLumaThreshold = 0.05f;
        public const float LightFlickerAmount = 0.04f;
        public const float LightFlickerSpeed = 10f;
        public const int LightProbeSize = 64; // mipped down to one texel, so this is real coverage
        public const float LightSampleInterval = 0.08f;
    }

    internal static class Television
    {
        public const string DeckPrefabAssetName = "CassetteDeck";
        public const float AttachPollInterval = 1.0f;
        public static readonly Vector3 DeckLocalPosition = new(0.49f, 0.95f, -0.27f);
        public static readonly Vector3 DeckLocalEuler = new(0f, 0f, 0f);
        public static readonly Vector3 DeckLocalScale = new(1f, 1f, 1f);
        public const int ScreenMaterialIndex = 1;
        public const string ScreenBaseColor = "_BaseColor";
        public const string ScreenEmissiveColor = "_EmissiveColor";
        public const float ScreenGlow = 4f;
        public const float OutlineScale = 1.04f;
        public const float PlaybackAudioMaxDistance = 22f;

        // deck prefab names
        public const string TriggerInsert = "insert";
        public const string TriggerStartStop = "startstop";
        public const string TriggerPauseResume = "pasres";
        public const string TriggerSpeedUp = "speedu";
        public const string TriggerSpeedDown = "speedd";
        public const string TriggerVolume = "volume";
        public const string StateStop = "stopb";
        public const string StateStopUp = "stopbUp";
        public const string StateResume = "resumeb";
        public const string StateResumeUp = "resumebUp";
        public const string StateSpeedDown = "speeddown";
        public const string StateSpeedUp = "speedup";
        public const string StateEject = "ejectb";
        public const string StateLoadIn = "LoadIn";
        public const string StateLoadOut = "LoadOut";
        public const string StateReelSpin = "ReelSpin";
        public const string ReelSpeedParameter = "ReelSpeed";
        public const float LocalPressHold = 0.75f;
        public const float EndGrace = 0.75f;
        public const string Frame1ObjectName = "frame1";
        public const string Frame2ObjectName = "frame2";
        public const string VolumeKnobObjectName = "volumescroll";
        public const string VolumeArrowObjectName = "arrow";
        public const string DisplayObjectName = "display";
        public const string DisplayEmissiveMap = "_EmissiveColorMap";
        public const int DisplayWidth = 840;
        public const int DisplayHeight = 160;
        public const float DisplayRedrawInterval = 0.25f;
        public const string LampObjectName = "lampLens";
        public const string LampEmissiveColor = "_EmissiveColor";
        public const string IdleClock = "12:00";
        public const float ClockBlinkInterval = 0.5f;
        public const string KeySoundObjectName = "DeckKeys";
        public const string MechSoundObjectName = "DeckMech";
        public const string MotorSoundObjectName = "DeckMotor";
        public static readonly Vector3 SoundLocalPosition = new(0f, 0.25f, 0.25f);
        public const float SoundMinDistance = 1f;
        public const float SoundMaxDistance = 14f;
        public const float KeyVolume = 0.7f;
        public const float LoadVolume = 0.9f;
        public const float EjectVolume = 0.9f;
        public const float MotorVolume = 0.24f;
        public const float PlaybackGain = 2.5f;
        public const float LimiterCeiling = 0.89f;
        public const float LimiterRelease = 0.15f;
        public const string MicrowaveUnlockable = "Microwave";
        public const string BoomboxItem = "Boombox";
        public static readonly string[] KeyClips = { "ButtonPress1_0", "ButtonPress2" };
        public const string LoadClip = "MicrowaveClose";
        public const string EjectClip = "BoomboxStop";
        public const string MotorClip = "MicrowaveWhir";
        public const string GhostObjectName = "ghost";
        public const string GhostRendererName = "DeckGhost";
        public const string OutlineRendererName = "DeckOutline";

        public const float VolumeMin = 0.1f;
        public const float VolumeStep = 0.1f;
        public const float VolumeMinAngle = 144f;
        public const float VolumeStepAngle = 32f;

        public const float LightMidLuma = 0.22f; // footage luma that lights room at 1x
        public const float LightContrast = 0.75f; // exponent on luma over mid
        public const float LightMin = 0.25f;
        public const float LightMax = 3f;
        public const float LightResponseSpeed = 12f; // high so cuts snap
        public const float LightMaxSaturation = 0.3f;
        public const float LightDarkLumaThreshold = 0.05f; // tint ramps from backlight to picture up to this luma
        public const float LightFlickerAmount = 0.04f;
        public const float LightFlickerSpeed = 10f;
        public const int LightProbeSize = 16;
        public const float LightSampleInterval = 0.08f;
        public static readonly Color BacklightColor = new(0.82f, 0.88f, 1f);

        public const float InsertSlideTime = 1.3f;
        public const float SlotOutsideOffset = 0.22f; // insert start and eject spawn
        public const float SlotInsideDepth = 0.119f;
        public const float BasketDrop = 0.028f;
        public const float InsertDrawStart = 0.08f;
        public const float InsertDrawEnd = 0.57f;
        public const float InsertDropStart = 0.45f;
        public const float InsertDropEnd = 0.77f;
        public const float InsertBuzzAmplitude = 0.0004f;
        public const float InsertBuzzRate = 56f;
        public static readonly Vector3 VisualLocalScale = Vector3.one;

        public const float FlapMass = 0.06f;
        public const float FlapAngularDrag = 2.5f;
        public const float FlapHingeSpring = 22f;
        public const float FlapHingeDamper = 2.5f;
        // symmetric, axis sign flips per flap
        public const float FlapHingeMinAngle = -60f;
        public const float FlapHingeMaxAngle = 60f;
        public static readonly Vector3 FlapHingeAxis = Vector3.right;
        public const float FlapInsertOpenSpeed = 3.5f; // rad/s
        public const float FlapEjectOpenSpeed = 8f; // rad/s

        public const float DeckEjectOutForce = 2f;
        public const float DeckEjectUpForce = 1.5f;
        public const float DeckEjectSettleVelocity = 0.18f;
        public const float DeckEjectSettleTimeout = 6f;
        public const float DeckEjectCrazyChance = 0.08f;
        public const float DeckEjectCrazyMultiplier = 2.5f; // kept low so cassette doesn't freak out
    }
}
