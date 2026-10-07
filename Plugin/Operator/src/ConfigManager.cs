using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using Operator.Compat;

namespace Operator;

public enum EncoderChoice
{
    Auto,
    Nvidia,
    Intel,
    Amd,
    Cpu,
}

public enum RecordingResolutionPreset
{
    High_860x520,
    Medium_640x360,
    Low_426x240,
    UltraLow_256x144,
}

public enum RecordedMicCharacter
{
    Off,
    Casual,
    Real,
    Aggressive
}

public enum RecordingAudioBitratePreset
{
    Kbps16 = 16,
    Kbps24 = 24,
    Kbps32 = 32,
    Kbps48 = 48,
    Kbps64 = 64,
    Kbps96 = 96,
    Kbps128 = 128,
    Kbps160 = 160,
    Kbps192 = 192,
    Kbps256 = 256,
    Kbps320 = 320,
}

public enum OcclusionEffectStyle
{
    PlainBlackFade = 0,
    VignetteClosing = 1,
    VignetteSmear = 2,
}

public enum CameraFilterMode
{
    Point = 0,
    Bilinear = 1,
}

public enum CameraOverlayStyle
{
    Off = 0,
    Minimal = 1,
    Classic = 2,
    Vhs = 3,
    Pro = 4,
}

public enum CameraFocusMode
{
    Off = 0,
    Auto = 1,
    Manual = 2,
}

public enum FeedExposureMode
{
    Off = 0,
    Casual = 1,
    Real = 2,
}

public enum DeathRecordingBehavior
{
    DeadManRoll = 0,
    KeepFilming = 1,
}

public enum FeedRenderQuality
{
    Quality = 0,
    Performance = 1,
}

public enum LensRepair
{
    Never = 0,
    EachQuota = 1,
    EachOrbit = 2,
}

internal static class VideoCameraConfig
{
    public static ConfigEntry<int> StoreCost { get; private set; } = null!;
    public static ConfigEntry<string> TerminalHelpKeyword { get; private set; } = null!;

    public static ConfigEntry<float> MinFov { get; private set; } = null!;
    public static ConfigEntry<float> MaxFov { get; private set; } = null!;
    public static ConfigEntry<float> ZoomSpeed { get; private set; } = null!;
    public static ConfigEntry<float> IdleBatteryDrain { get; private set; } = null!;
    public static ConfigEntry<float> BobIntensity { get; private set; } = null!;
    public static ConfigEntry<float> ScreenEmissionIntensity { get; private set; } = null!;

    public static ConfigEntry<RecordingResolutionPreset> RenderResolution { get; private set; } = null!;
    public static ConfigEntry<CameraFilterMode> RenderFilterMode { get; private set; } = null!;
    public static ConfigEntry<int> RenderFps { get; private set; } = null!;
    public static ConfigEntry<FeedRenderQuality> RenderQuality { get; private set; } = null!;
    public static ConfigEntry<bool> WeatherPerspective { get; private set; } = null!;
    public static ConfigEntry<CameraOverlayStyle> Overlay { get; private set; } = null!;
    public static ConfigEntry<CameraFocusMode> FocusMode { get; private set; } = null!;
    public static ConfigEntry<FeedExposureMode> FeedExposure { get; private set; } = null!;
    public static ConfigEntry<OcclusionEffectStyle> OcclusionStyle { get; private set; } = null!;

    public static ConfigEntry<float> EffectStrength { get; private set; } = null!;
    public static ConfigEntry<float> FearResponse { get; private set; } = null!;
    public static ConfigEntry<float> Vignette { get; private set; } = null!;
    public static ConfigEntry<float> BarrelDistortion { get; private set; } = null!;
    public static ConfigEntry<float> ChromaticAberration { get; private set; } = null!;
    public static ConfigEntry<float> LensDust { get; private set; } = null!;
    public static ConfigEntry<float> VeilingGlare { get; private set; } = null!;
    public static ConfigEntry<float> FocusBreathing { get; private set; } = null!;
    public static ConfigEntry<float> RainIntensity { get; private set; } = null!;
    public static ConfigEntry<float> Grain { get; private set; } = null!;
    public static ConfigEntry<float> Scanlines { get; private set; } = null!;
    public static ConfigEntry<float> TrackingGlitch { get; private set; } = null!;
    public static ConfigEntry<float> ColdDesaturation { get; private set; } = null!;
    public static ConfigEntry<float> ExposurePump { get; private set; } = null!;
    public static ConfigEntry<float> WhiteBalanceDrift { get; private set; } = null!;
    public static ConfigEntry<bool> CrackOnLens { get; private set; } = null!;
    public static ConfigEntry<int> CrackDamageThreshold { get; private set; } = null!;
    public static ConfigEntry<int> CrackMaxStages { get; private set; } = null!;
    public static ConfigEntry<LensRepair> LensRepair { get; private set; } = null!;

    public static ConfigEntry<bool> RecordingEnabled { get; private set; } = null!;
    public static ConfigEntry<RecordingResolutionPreset> RecordingResolution { get; private set; } = null!;
    public static ConfigEntry<int> RecordingFps { get; private set; } = null!;
    public static ConfigEntry<int> RecordingCrf { get; private set; } = null!;
    public static ConfigEntry<int> MaxMinutes { get; private set; } = null!;
    public static ConfigEntry<int> MaxQueuedVideoFrames { get; private set; } = null!;
    public static ConfigEntry<DeathRecordingBehavior> DeathBehavior { get; private set; } = null!;
    public static ConfigEntry<bool> KeepUnsavedPerLobby { get; private set; } = null!;
    public static ConfigEntry<string> OutputDirectoryOverride { get; private set; } = null!;
    public static ConfigEntry<EncoderChoice> Encoder { get; private set; } = null!;
    public static ConfigEntry<string> FfmpegExePath { get; private set; } = null!;

    public static ConfigEntry<bool> RecordAudio { get; private set; } = null!;
    public static ConfigEntry<RecordingAudioBitratePreset> AudioBitrateKbps { get; private set; } = null!;
    public static ConfigEntry<RecordedMicCharacter> MicCharacter { get; private set; } = null!;
    public static ConfigEntry<bool> MicEnvironmentEffects { get; private set; } = null!;
    public static ConfigEntry<float> MicReverbAmount { get; private set; } = null!;
    public static ConfigEntry<float> MicVolume { get; private set; } = null!;
    public static ConfigEntry<float> EchoStrength { get; private set; } = null!;
    public static ConfigEntry<float> VoiceRange { get; private set; } = null!;
    public static ConfigEntry<float> DeathStaticFadeSeconds { get; private set; } = null!;

    public static ConfigEntry<bool> BroadcastRecordingsToClients { get; private set; } = null!;
    public static ConfigEntry<bool> KeepReceivedClips { get; private set; } = null!;
    public static ConfigEntry<bool> TerminalCoWatch { get; private set; } = null!;
    public static ConfigEntry<bool> TransferAudioFromSpeaker { get; private set; } = null!;
    public static ConfigEntry<bool> TelevisionCassetteWatching { get; private set; } = null!;
    public static ConfigEntry<float> TelevisionScreenLightIntensity { get; private set; } = null!;
    public static ConfigEntry<float> MonitorScreenLightIntensity { get; private set; } = null!;

    public static ConfigEntry<int> BlankCassettesPerOrbit { get; private set; } = null!;
    public static ConfigEntry<bool> BlanksPerShip { get; private set; } = null!;
    public static ConfigEntry<int> FlatBaseValue { get; private set; } = null!;
    public static ConfigEntry<int> DeadBodyPenalty { get; private set; } = null!;
    public static ConfigEntry<float> MinSecondsForBounty { get; private set; } = null!;
    public static ConfigEntry<string> WorthlessMoonsForced { get; private set; } = null!;
    public static ConfigEntry<string> WorthlessMoonsExcluded { get; private set; } = null!;
    public static ConfigEntry<bool> WorthlessOrbitFootage { get; private set; } = null!;

    private static bool _bountyOrphansCleared;

    private static (int Width, int Height) ResolutionFromPreset(RecordingResolutionPreset preset) => preset switch
    {
        RecordingResolutionPreset.UltraLow_256x144 => (256, 144),
        RecordingResolutionPreset.Low_426x240 => (426, 240),
        RecordingResolutionPreset.Medium_640x360 => (640, 360),
        RecordingResolutionPreset.High_860x520 => (860, 520),
        _ => (640, 360),
    };

    public static (int Width, int Height) GetRecordingSize() => ResolutionFromPreset(RecordingResolution.Value);

    public static (int Width, int Height) GetRenderSize() => ResolutionFromPreset(RenderResolution.Value);

    public static (int Width, int Height) GetReadbackSize(int captureWidth, int captureHeight)
    {
        (int outWidth, int outHeight) = GetRecordingSize();
        return outWidth * outHeight < captureWidth * captureHeight
            ? (outWidth, outHeight)
            : (captureWidth, captureHeight);
    }

    public static int GetAudioBitrate()
    {
        int kbps = (int)AudioBitrateKbps.Value;
        return Mathf.Clamp(kbps, Constants.ConfigRanges.AudioBitrateMin, Constants.ConfigRanges.AudioBitrateMax);
    }

    public static float GetEffect(ConfigEntry<float> slider)
    {
        return Mathf.Max(0f, slider.Value) * Mathf.Clamp01(EffectStrength.Value);
    }

    public static int GetRecordingFps()
    {
        return Mathf.Clamp(RecordingFps.Value, Constants.ConfigRanges.FpsMin, Constants.ConfigRanges.FpsMax);
    }

    public static void Initialize(ConfigFile config)
    {
        config.SaveOnConfigSet = false;

        const string general = "1. General";
        const string camera = "2. Camera";
        const string feed = "3. Feed";
        const string effects = "4. Effects";
        const string recording = "5. Recording";
        const string audio = "6. Audio";
        const string sharing = "7. Sharing";
        const string economy = "8. Economy";

        StoreCost = config.Bind(
            general,
            "StoreCost",
            260,
            new ConfigDescription(
                "Camcorder price in store.",
                new AcceptableValueRange<int>(Constants.ConfigRanges.StoreCostMin, Constants.ConfigRanges.StoreCostMax)));

        TerminalHelpKeyword = config.Bind(
            general,
            "TerminalHelpKeyword",
            "ohelp",
            "Word to type in terminal to list every command of this mod."
        );

        MinFov = config.Bind(
            camera,
            "MinFov",
            25f,
            new ConfigDescription(
                "How far camera can zoom in.",
                new AcceptableValueRange<float>(Constants.ConfigRanges.CameraMinFovMin, Constants.ConfigRanges.CameraMinFovMax))
        );

        MaxFov = config.Bind(
            camera,
            "MaxFov",
            75f,
            new ConfigDescription(
                "How far camera can zoom out.",
                new AcceptableValueRange<float>(Constants.ConfigRanges.CameraMaxFovMin, Constants.ConfigRanges.CameraMaxFovMax))
        );

        ZoomSpeed = config.Bind(
            camera,
            "ZoomSpeed",
            40f,
            new ConfigDescription(
                "Zooming speed.",
                new AcceptableValueRange<float>(Constants.ConfigRanges.CameraZoomSpeedMin, Constants.ConfigRanges.CameraZoomSpeedMax))
        );

        IdleBatteryDrain = config.Bind(
            camera,
            "IdleBatteryDrain",
            Constants.Battery.IdleDrainMultiplier,
            new ConfigDescription(
                "Battery drain while Camcorder is on but not recording.",
                new AcceptableValueRange<float>(Constants.ConfigRanges.IdleBatteryDrainMin, Constants.ConfigRanges.IdleBatteryDrainMax))
        );

        BobIntensity = config.Bind(
            camera,
            "BobIntensity",
            1.25f,
            new ConfigDescription(
                "How much camera sways as player with camera walks.",
                new AcceptableValueRange<float>(Constants.ConfigRanges.CameraBobIntensityMin, Constants.ConfigRanges.CameraBobIntensityMax))
        );

        ScreenEmissionIntensity = config.Bind(
            camera,
            "ScreenEmissionIntensity",
            3.0f,
            new ConfigDescription(
                "How brightly camera screen emits.",
                new AcceptableValueRange<float>(Constants.ConfigRanges.ScreenEmissionMin, Constants.ConfigRanges.ScreenEmissionMax))
        );

        RenderResolution = config.Bind(
            feed,
            "RenderResolution",
            RecordingResolutionPreset.Medium_640x360,
            "Resolution camera renders its own screen at."
        );

        RenderFilterMode = config.Bind(
            feed,
            "FilterMode",
            CameraFilterMode.Point,
            "How camera image is sampled onto screen. 'Point' is pixelated and matches an old tape. 'Bilinear' is smooth."
        );

        RenderFps = config.Bind(
            feed,
            "RenderFps",
            Constants.Visual.FeedRenderFps,
            new ConfigDescription(
                "How many times per second camera screen rerenders, lower saves CPU. Recordings are unaffected.",
                new AcceptableValueRange<int>(Constants.ConfigRanges.FeedRenderFpsMin, Constants.ConfigRanges.FeedRenderFpsMax))
        );

        RenderQuality = config.Bind(
            feed,
            "RenderQuality",
            Operator.FeedRenderQuality.Quality,
            "'Quality' graphics like in cyberpunk. 'Performance' renders the feed without shadows or volumetric fog and with lower detail models, about half its cost outdoors. 'Quality' keeps all of that."
        );

        WeatherPerspective = config.Bind(
            feed,
            "WeatherPerspective",
            true,
            "Show weather from where camera is instead of where players are. Turn this off if modded weather looks wrong on screen."
        );

        Overlay = config.Bind(
            feed,
            "Overlay",
            CameraOverlayStyle.Pro,
            "Camcorder style screen display. 'Off' is clear screen. 'Minimal' shows REC and battery. 'Classic' adds timecode and frame. 'Vhs' is retro look. 'Pro' is full Handycam style."
        );

        FocusMode = config.Bind(
            feed,
            "FocusMode",
            CameraFocusMode.Auto,
            "'Off' keeps everything always sharp. 'Auto' blurs and refocuses on its own. 'Manul' lets you pull focus yourself with Focus Near/Far keys using reticle on screen to see when youre sharp."
        );

        FeedExposure = config.Bind(
            feed,
            "Exposure",
            FeedExposureMode.Real,
            "Simulates camcorders exposure catching up when lighting changes suddenly. 'Casual' is gentle lag that never lowers visibility. 'Real' does it properly and can briefly wash out or go dark."
        );

        OcclusionStyle = config.Bind(
            feed,
            "OcclusionStyle",
            Operator.OcclusionEffectStyle.VignetteSmear,
            "How feed reacts when camera is pressed against objects so you cannot see through walls. 'PlainBlackFade' just dims to black. 'VignetteClosing' closes edges inward and blurs as it darkens. 'VignetteSmear' adds directional smear at close range."
        );

        EffectStrength = config.Bind(
            effects,
            "EffectStrength",
            1f,
            new ConfigDescription(
                "Master value over every effect below. Lens cracks, night vision, exposure lag and screen display are separate and stay on.",
                new AcceptableValueRange<float>(0f, 1f))
        );

        FearResponse = config.Bind(
            effects,
            "FearResponse",
            1f,
            new ConfigDescription(
                "How hard camera reacts to held camera mans fear.",
                new AcceptableValueRange<float>(0f, 2f))
        );

        Vignette = config.Bind(
            effects,
            "Vignette",
            1.0f,
            new ConfigDescription(
                "How dark corners of frame darken.",
                new AcceptableValueRange<float>(0.0f, 2.0f)));

        BarrelDistortion = config.Bind(
            effects,
            "BarrelDistortion",
            1.0f,
            new ConfigDescription(
                "How much lens distorts image outward at edges.",
                new AcceptableValueRange<float>(0.0f, 2.0f)));

        ChromaticAberration = config.Bind(
            effects,
            "ChromaticAberration",
            1.0f,
            new ConfigDescription(
                "Colour fringing that grows toward edges of frame where lens fails to focus red and blue on same spot.",
                new AcceptableValueRange<float>(0.0f, 2.0f)));

        LensDust = config.Bind(
            effects,
            "LensDust",
            0.5f,
            new ConfigDescription(
                "Dust and smears on front element.",
                new AcceptableValueRange<float>(0.0f, 2.0f)));

        VeilingGlare = config.Bind(
            effects,
            "VeilingGlare",
            0.5f,
            new ConfigDescription(
                "Stray light scattering off lens elements into haze around bright sources.",
                new AcceptableValueRange<float>(0.0f, 2.0f)));

        FocusBreathing = config.Bind(
            effects,
            "FocusBreathing",
            1.0f,
            new ConfigDescription(
                "Slight zoom that happens as lens racks focus.",
                new AcceptableValueRange<float>(0.0f, 2.0f)));

        RainIntensity = config.Bind(
            effects,
            "RainIntensity",
            1.0f,
            new ConfigDescription(
                "How heavily rain beads on lens while outside in rainy or stormy weather.",
                new AcceptableValueRange<float>(0.0f, 2.0f)));

        Grain = config.Bind(
            effects,
            "Grain",
            1.0f,
            new ConfigDescription(
                "Tape grain over the whole image.",
                new AcceptableValueRange<float>(0.0f, 2.0f)));

        Scanlines = config.Bind(
            effects,
            "Scanlines",
            1.0f,
            new ConfigDescription(
                "Darkened alternating rows, look of an interlaced tape.",
                new AcceptableValueRange<float>(0.0f, 2.0f)));

        TrackingGlitch = config.Bind(
            effects,
            "TrackingGlitch",
            1.0f,
            new ConfigDescription(
                "Tape tracking errors that tear across frame when fear jumps suddenly.",
                new AcceptableValueRange<float>(0.0f, 2.0f)));

        ColdDesaturation = config.Bind(
            effects,
            "ColdDesaturation",
            1.0f,
            new ConfigDescription(
                "Colour draining toward cold grey as fear rises.",
                new AcceptableValueRange<float>(0.0f, 2.0f)));

        ExposurePump = config.Bind(
            effects,
            "ExposurePump",
            1.0f,
            new ConfigDescription(
                "Exposure hunting up and down when player is frightened.",
                new AcceptableValueRange<float>(0.0f, 2.0f)));

        WhiteBalanceDrift = config.Bind(
            effects,
            "WhiteBalanceDrift",
            1.0f,
            new ConfigDescription(
                "White balance sliding warmer or cooler with fear.",
                new AcceptableValueRange<float>(0.0f, 2.0f)));

        CrackOnLens = config.Bind(
            effects,
            "CrackOnLens",
            true,
            "Camera lens cracks when you take damage or drop camera from height."
        );

        CrackDamageThreshold = config.Bind(
            effects,
            "CrackDamageThreshold",
            20,
            new ConfigDescription(
                "How much health you must lose to add crack stage.",
                new AcceptableValueRange<int>(1, 100))
        );

        CrackMaxStages = config.Bind(
            effects,
            "CrackMaxStages",
            2,
            new ConfigDescription(
                "Max lens crack stages.",
                new AcceptableValueRange<int>(1, 3))
        );

        LensRepair = config.Bind(
            effects,
            "LensRepair",
            Operator.LensRepair.EachQuota,
            "When cracked camera lens is repaired. 'EachOrbit' repairs every time ship leaves moon and returns to orbit. 'EachQuota' repairs only when new profit quota begins. 'Never' leaves cracks permanently."
        );

        RecordingEnabled = config.Bind(
            recording,
            "Enabled",
            true,
            "Write recordings to output directory while camera is powered on. Turning this off leaves camera working as viewfinder that records nothing."
        );

        RecordingResolution = config.Bind(
            recording,
            "Resolution",
            RecordingResolutionPreset.Low_426x240,
            "Resolution of recorded MP4."
        );

        RecordingFps = config.Bind(
            recording,
            "Fps",
            24,
            new ConfigDescription(
                "Framerate of recorded MP4. 60 is smooth and increases the size.",
                new AcceptableValueRange<int>(Constants.ConfigRanges.FpsMin, Constants.ConfigRanges.FpsMax))
        );

        RecordingCrf = config.Bind(
            recording,
            "Crf",
            23,
            new ConfigDescription(
                "Recording quality. Lower is better quality and bigger file. 16 is near lossless, 32 is visibly blocky.",
                new AcceptableValueRange<int>(Constants.ConfigRanges.CrfMin, Constants.ConfigRanges.CrfMax))
        );

        MaxMinutes = config.Bind(
            recording,
            "MaxMinutes",
            10,
            new ConfigDescription(
                "Recording stops on its own after this many minutes of filming.",
                new AcceptableValueRange<int>(Constants.ConfigRanges.MaxMinutesMin, Constants.ConfigRanges.MaxMinutesMax))
        );

        MaxQueuedVideoFrames = config.Bind(
            recording,
            "MaxQueuedVideoFrames",
            96,
            new ConfigDescription(
                "INTENDED FOR TESTING ONLY. Leave this at 96 unless you are doing stuff.\n\nMaximum video frames buffered between capture pump and ffmpeg. ffmpeg takes few seconds to start reading and anything captured past this buffer is replaced by repeated frame, so lowering it brings back stutter at start of every recording. Raising it trades RAM for headroom on slower machines.",
                new AcceptableValueRange<int>(Constants.ConfigRanges.MaxQueuedFramesMin, Constants.ConfigRanges.MaxQueuedFramesMax))
        );

        DeathBehavior = config.Bind(
            recording,
            "DeathBehavior",
            Operator.DeathRecordingBehavior.KeepFilming,
            "What camera does when operator dies while filming. 'KeepFilming' keeps recording with static hiss. 'DeadManRoll' rolls static instead."
        );

        KeepUnsavedPerLobby = config.Bind(
            recording,
            "KeepUnsavedPerLobby",
            true,
            "Keep finished recordings per lobby under Recordings/Lobbies/<save file>/ and only move one into main Recordings folder when you save that cassette. Cassette that no longer exists is cleaned up automatically. Turn this off to write every finished recording straight to main Recordings folder and keep it."
        );

        OutputDirectoryOverride = config.Bind(
            recording,
            "OutputDirectoryOverride",
            "",
            @"Path for output folder. Leave empty to use %USERPROFILE%\AppData\LocalLow\ZeekerssRBLX\Lethal Company\Recordings\."
        );

        Encoder = config.Bind(
            recording,
            "Encoder",
            EncoderChoice.Auto,
            "Auto tries NVIDIA, Intel and AMD, then the CPU (x264). Cpu always works, about 6% of one core at 426x240. Under Proton (Linux, Steam Deck) Auto uses the CPU."
        );
        Encoder.SettingChanged += (_, _) => Operator.Recording.VideoEncoder.Restart();

        FfmpegExePath = config.Bind(
            recording,
            "FfmpegExePath",
            "",
            "Path to an existing ffmpeg.exe. Leave empty to auto download and cache pinned build under BepInEx/cache/ffmpeg/."
        );

        RecordAudio = config.Bind(
            audio,
            "RecordAudio",
            true,
            "Capture game audio onto recording. Turning this off saves disk space and makes MP4 silent hill."
        );

        AudioBitrateKbps = config.Bind(
            audio,
            "AudioBitrateKbps",
            RecordingAudioBitratePreset.Kbps64,
            "AAC audio bitrate. 32 and below sound obviously compressed, 192 and above are wasted on a camcorder mix."
        );

        MicCharacter = config.Bind(
            audio,
            "MicCharacter",
            RecordedMicCharacter.Casual,
            "Run recording through camcorder microphone. It band limits sound, adds tape hiss and rides level so a quiet room swells up and a loud noise ducks everything for moment. 'Off' records clean mix."
        );

        MicEnvironmentEffects = config.Bind(
            audio,
            "MicEnvironmentEffects",
            true,
            "Run your recorded microphone through games real environmental voice filters so it matches what other players hear."
        );

        MicReverbAmount = config.Bind(
            audio,
            "MicReverbAmount",
            0.5f,
            new ConfigDescription(
                "How much room players own recorded microphone picks up so your own voice sits in world instead of on top of it. Only affects players own mic.",
                new AcceptableValueRange<float>(0f, 1f))
        );

        MicVolume = config.Bind(
            audio,
            "MicVolume",
            1f,
            new ConfigDescription(
                "Volume of your own voice on tape. 1 is your mic as is, 2 twice as loud, 0 leaves it out.",
                new AcceptableValueRange<float>(0f, 4f))
        );

        EchoStrength = config.Bind(
            audio,
            "EchoStrength",
            1f,
            new ConfigDescription(
                "How much room recorded voice is heard in. Raise it for wetter, more cavernous tape, lower it for drier one.",
                new AcceptableValueRange<float>(0f, 3f))
        );

        VoiceRange = config.Bind(
            audio,
            "VoiceRange",
            Constants.Audio.VoiceRange,
            new ConfigDescription(
                "Meters at which recorded voices fade out, yours too when camera is set down. 50 is game's own range.",
                new AcceptableValueRange<float>(10f, 150f))
        );

        DeathStaticFadeSeconds = config.Bind(
            audio,
            "DeathStaticFadeSeconds",
            8f,
            new ConfigDescription(
                "How long static takes to fade out after player dies with camera and camera keeps recording.",
                new AcceptableValueRange<float>(0f, 60f))
        );

        BroadcastRecordingsToClients = config.Bind(
            sharing,
            "BroadcastRecordingsToClients",
            true,
            "[Client side] After recording finishes send MP4 to everyone else so they can watch it. They get saved under ZeekerssRBLX/Lethal Company/Recordings/Received/. This is read on whoever did recording so host cannot turn it off for everyone."
        );

        KeepReceivedClips = config.Bind(
            sharing,
            "KeepReceivedClips",
            false,
            "Keep other players recordings that were sent to you, under Recordings/Received/ so you can rewatch them anytime. If false, each is marked for deletion once no longer needed. Use 'save <id>' in terminal to keep permanently."
        );

        TerminalCoWatch = config.Bind(
            sharing,
            "TerminalCoWatch",
            true,
            "When anyone plays cassette in terminal/TV, mirror it to every players terminal so whole lobby sees and hears same recording."
        );

        TransferAudioFromSpeaker = config.Bind(
            sharing,
            "TransferAudioFromSpeaker",
            true,
            "Play transferred recording through ship speaker. If false, sound comes from monitor itself."
        );

        TelevisionCassetteWatching = config.Bind(
            sharing,
            "TelevisionCassetteWatching",
            true,
            "Add cassette deck on top of ship Television. Insert cassette to play its recording on TV screen for whole lobby."
        );

        TelevisionScreenLightIntensity = config.Bind(
            sharing,
            "TelevisionScreenLightIntensity",
            1.6f,
            new ConfigDescription(
                "How hard playing recording drives Television light.",
                new AcceptableValueRange<float>(Constants.ConfigRanges.TelevisionLightMin, Constants.ConfigRanges.TelevisionLightMax))
        );

        MonitorScreenLightIntensity = config.Bind(
            sharing,
            "MonitorScreenLightIntensity",
            1.6f,
            new ConfigDescription(
                "How hard transferred recording drives ship monitor light.",
                new AcceptableValueRange<float>(Constants.ConfigRanges.MonitorLightMin, Constants.ConfigRanges.MonitorLightMax))
        );

        BlankCassettesPerOrbit = config.Bind(
            economy,
            "BlankCassettesPerOrbit",
            1,
            new ConfigDescription(
                "Blank cassettes restocked each time the ship reaches orbit. Counts for the whole ship when 'BlanksPerShip' is true, otherwise each camera gets this many.",
                new AcceptableValueRange<int>(Constants.ConfigRanges.CassettesPerOrbitMin, Constants.ConfigRanges.CassettesPerOrbitMax))
        );

        BlanksPerShip = config.Bind(
            economy,
            "BlanksPerShip",
            true,
            new ConfigDescription(
                "Every camera takes from one shared stock of 'BlankCassettesPerOrbit' blanks, so second camera does not mean more tapes.")
        );

        FlatBaseValue = config.Bind(
            economy,
            "FlatBaseValue",
            5,
            new ConfigDescription(
                "Scrap value added to every cassette regardless of what is on it.",
                new AcceptableValueRange<int>(Constants.ConfigRanges.FlatBaseValueMin, Constants.ConfigRanges.FlatBaseValueMax))
        );

        DeadBodyPenalty = config.Bind(
            economy,
            "DeadBodyPenalty",
            25,
            new ConfigDescription(
                "Percent taken off the earned part of a tape for each dead crewmate filmed long enough to count (see 'MinSecondsForBounty'). Bodies already in the ship or being carried don't count. 'FlatBaseValue' is never reduced. 0 turns it off.",
                new AcceptableValueRange<int>(Constants.ConfigRanges.DeadBodyPenaltyMin, Constants.ConfigRanges.DeadBodyPenaltyMax))
        );

        MinSecondsForBounty = config.Bind(
            economy,
            "MinSecondsForBounty",
            2.0f,
            new ConfigDescription(
                "How long an individual enemy or dead body must spend on camera before any value is added for it.",
                new AcceptableValueRange<float>(Constants.ConfigRanges.MinBountyTimeMin, Constants.ConfigRanges.MinBountyTimeMax))
        );

        WorthlessMoonsForced = config.Bind(
            economy,
            "WorthlessMoonsForced",
            "",
            "Extra moons whose cassettes is worth nothing, separated by commas, i.e. \"Assurance, Titan\". Cassettes from the Company is already worth nothing."
        );

        WorthlessMoonsExcluded = config.Bind(
            economy,
            "WorthlessMoonsExcluded",
            "",
            "Moons to keep paying for even though they look like the Company moons, separated by commas, i.e. \"Assurance, Titan\"."
        );

        WorthlessOrbitFootage = config.Bind(
            economy,
            "WorthlessOrbitFootage",
            true,
            "Cassettes shot in orbit is worth nothing."
        );

        ClearOrphanedEntries(config);
        config.Save();
        config.SaveOnConfigSet = true;

        LethalConfigCompat.Register(config);
    }

    private static Dictionary<ConfigDefinition, string>? GetOrphanedEntries(ConfigFile config)
    {
        PropertyInfo? orphanedEntriesProp = AccessTools.Property(typeof(ConfigFile), "OrphanedEntries");
        return orphanedEntriesProp?.GetValue(config) as Dictionary<ConfigDefinition, string>;
    }

    private static int RemoveOrphans(Dictionary<ConfigDefinition, string> orphanedEntries, bool bountySection)
    {
        List<ConfigDefinition> stale = new();
        foreach (ConfigDefinition definition in orphanedEntries.Keys)
        {
            if ((definition.Section == Constants.Bounty.ConfigSection) == bountySection)
            {
                stale.Add(definition);
            }
        }

        foreach (ConfigDefinition definition in stale)
        {
            orphanedEntries.Remove(definition);
        }

        return stale.Count;
    }

    private static void ClearOrphanedEntries(ConfigFile config)
    {
        try
        {
            Dictionary<ConfigDefinition, string>? orphanedEntries = GetOrphanedEntries(config);
            if (orphanedEntries == null || orphanedEntries.Count == 0) return;

            int removed = RemoveOrphans(orphanedEntries, bountySection: false);
            if (removed > 0) Plugin.Log.LogInfo($"Cleared {removed} settings that no longer exist.");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"Failed to clear obsolete settings: {ex.Message}");
        }
    }

    internal static void ClearBountyOrphans(ConfigFile config)
    {
        if (_bountyOrphansCleared) return;
        _bountyOrphansCleared = true;

        try
        {
            Dictionary<ConfigDefinition, string>? orphanedEntries = GetOrphanedEntries(config);
            if (orphanedEntries == null) return;

            int removed = RemoveOrphans(orphanedEntries, bountySection: true);
            if (removed > 0) Plugin.Log.LogInfo($"Cleared {removed} bounty entries for enemies that are no longer installed.");
            config.Save();
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"Failed to clear obsolete bounty entries: {ex.Message}");
        }
    }
}
