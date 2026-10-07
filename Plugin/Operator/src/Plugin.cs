using System;
using System.IO;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Dawn;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;
using Operator.Cassette;
using Operator.Compat;
using Operator.Input;
using Operator.Recording;

namespace Operator;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
[BepInDependency(DawnLib.PLUGIN_GUID, "0.9.0")]
[BepInDependency(ModGUIDs.InputUtils)]
[BepInDependency(ModGUIDs.CullFactory, BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency(ModGUIDs.GeneralImprovements, BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency(ModGUIDs.LethalConfig, BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency(ModGUIDs.LethalSponge, BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency(ModGUIDs.MoreShipUpgrades, BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency(ModGUIDs.OpenBodyCams, BepInDependency.DependencyFlags.SoftDependency)]
public class Plugin : BaseUnityPlugin
{
    private readonly Harmony _harmony = new(MyPluginInfo.PLUGIN_GUID);
    public static Plugin Instance { get; private set; } = null!;
    public static ManualLogSource Log { get; private set; } = null!;
    internal static OperatorInputs Inputs { get; private set; } = null!;

    private static void RegisterNetworkVariableTypes()
    {
        try
        {
            NetworkVariableSerializationTypes.InitializeSerializer_UnmanagedByMemcpy<bool>();
            NetworkVariableSerializationTypes.InitializeEqualityChecker_UnmanagedIEquatable<bool>();
            NetworkVariableSerializationTypes.InitializeSerializer_UnmanagedByMemcpy<int>();
            NetworkVariableSerializationTypes.InitializeEqualityChecker_UnmanagedIEquatable<int>();
            NetworkVariableSerializationTypes.InitializeSerializer_UnmanagedByMemcpy<float>();
            NetworkVariableSerializationTypes.InitializeEqualityChecker_UnmanagedIEquatable<float>();
            NetworkVariableSerializationTypes.InitializeSerializer_UnmanagedByMemcpy<double>();
            NetworkVariableSerializationTypes.InitializeEqualityChecker_UnmanagedIEquatable<double>();
        }
        catch (Exception ex)
        {
            Log.LogWarning($"Could not register network variable types, {ex.Message}");
        }
    }

    private void Awake()
    {
        Instance = this;
        Log = base.Logger;

        RegisterNetworkVariableTypes();
        VideoCameraConfig.Initialize(Config);
        Inputs = new OperatorInputs();
        VideoCameraRegistrar.Register();
        CassetteRegistrar.Register();
        CassetteLibrary.Load();
        EnemyBountyLoader.Hook();
        AssetsLoader.WarmAudioClips();

        _harmony.PatchAll();
        LethalSpongeCompat.Init();

        if (VideoCameraConfig.RecordingEnabled.Value)
        {
            PrewarmRecording();
        }

        Application.quitting += OnApplicationQuitting;

        Log.LogInfo($"{MyPluginInfo.PLUGIN_NAME} is loaded!");
    }

    private static void PrewarmRecording()
    {
        _ = FfmpegLocator.InitAsync()
            .ContinueWith(_ => VideoEncoder.PrewarmAsync(), TaskScheduler.Default);

        try
        {
            Directory.CreateDirectory(Recorder.GetOutputDirectory());
            Directory.CreateDirectory(Recorder.GetActiveRecordingDirectory());
        }
        catch (Exception ex)
        {
            Log.LogDebug($"Could not create recording directories yet: {ex.Message}");
        }

        (int width, int height) = (32, 32);
        try
        {
            (int captureWidth, int captureHeight) = VideoCameraConfig.GetRenderSize();
            (width, height) = VideoCameraConfig.GetReadbackSize(captureWidth, captureHeight);
            VideoPipe.PrewarmPool(width, height, Constants.Recording.PrewarmedFrameBuffers);
        }
        catch (Exception ex)
        {
            Log.LogDebug($"Could not prewarm frame pool: {ex.Message}");
        }

        PrewarmAsyncReadback(width, height);
    }

    private static void PrewarmAsyncReadback(int width, int height)
    {
        var texture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32) { name = "OPERATOR_Prewarm" };
        texture.Create();
        AsyncGPUReadback.Request(texture, 0, TextureFormat.RGBA32, _ =>
        {
            if (texture == null)
            {
                return;
            }
            texture.Release();
            Destroy(texture);
        });
    }

    private static void OnApplicationQuitting()
    {
        try
        {
            CassetteLibrary.CleanupUnsaved();
            CassetteLibrary.PruneAllReceived();
        }
        catch (Exception ex)
        {
            Log.LogWarning($"Failed to clean up cassette files on quit: {ex.Message}");
        }

        ClearScreenTargets();
    }

    private static void ClearScreenTargets()
    {
        foreach (Camera cam in Resources.FindObjectsOfTypeAll<Camera>())
        {
            if (cam == null) continue;
            RenderTexture? target = cam.targetTexture;
            if (target != null && target.name == Constants.Assets.PrefabScreenTextureName) cam.targetTexture = null;
        }
    }
}
