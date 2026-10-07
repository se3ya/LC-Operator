using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Operator.Recording;
using Operator.Recording.Voice;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;

namespace Operator.Patches;

[HarmonyPatch]
internal static class ProfilingPatch
{
    private static Dictionary<IntPtr, IntPtr> _handles = null!;

    private static (string Marker, Type Type, string Method)[] GetTargets() => new[]
    {
        ("Operator.Item.LateUpdate", typeof(VideoCameraItem), "LateUpdate"),
        ("Operator.Feed.Render", typeof(VideoCameraItem), "RenderFeed"),
        ("Operator.Feed.Osd", typeof(VideoCameraItem), "DrawFeedOsd"),
        ("Operator.Post.Probe", typeof(VideoCameraItem), "SampleRawFeedAmbient"),
        ("Operator.Post.Focus", typeof(VideoCameraItem), "DrawAutoFocusBlur"),
        ("Operator.Post.Exposure", typeof(VideoCameraItem), "DrawExposureAdaptation"),
        ("Operator.Post.Night", typeof(VideoCameraItem), "DrawNightVision"),
        ("Operator.Post.Wall", typeof(VideoCameraItem), "DrawOcclusionOverlay"),
        ("Operator.Post.Glass", typeof(VideoCameraItem), "DrawGlassDamage"),
        ("Operator.Post.Glare", typeof(VideoCameraItem), "DrawVeilingGlare"),
        ("Operator.Post.Look", typeof(VideoCameraItem), "DrawCamcorderLook"),
        ("Operator.Hud.Late", typeof(LateHud), "Render"),
        ("Operator.Weather.Suppress", typeof(VideoCameraItem), "SuppressFeedWeather"),
        ("Operator.Rec.Start", typeof(VideoCameraItem), "TryStartRecording"),
        ("Operator.Rec.Capture", typeof(FrameCapture), "Tick"),
        ("Operator.Rec.Readback", typeof(FrameCapture), "OnReadback"),
        ("Operator.Rec.Finalize", typeof(Recorder), "FinalizeOnMainThread"),
        ("Operator.Audio.Filter", typeof(AudioCapture), "OnAudioFilterRead"),
        ("Operator.Audio.Shadow", typeof(ShadowVoicePool), "Mix"),
        ("Operator.Audio.Remote", typeof(RemoteVoices), "Mix"),
        ("Operator.Audio.Reverb", typeof(ReverbBus), "Process"),
        ("Operator.Audio.CamMic", typeof(CamcorderMic), "Process"),
        ("Operator.Audio.Enqueue", typeof(AudioPipe), "EnqueueCopy"),
        ("Operator.Audio.Tick", typeof(ShadowAudio), "Tick"),
        ("Operator.Audio.Decode", typeof(ShadowClipCache), "Build"),
        ("Operator.Audio.OneShot", typeof(ShadowOneShotMirror), "MirrorAll"),
        ("Operator.Mic.Receive", typeof(DissonanceMicSubscriber), "ReceiveMicrophoneData"),
        ("Operator.Mic.Capture", typeof(MicCapture), "OnAudioFilterRead"),
        ("Operator.Mic.Update", typeof(VoiceChainController), "Update")
    };

    private static MethodInfo? Find(Type type, string name)
    {
        try
        {
            MethodInfo? method = type.GetMethod(name, AccessTools.allDeclared);
            if (method != null) return method;

            string local = $"g__{name}|";
            foreach (Type nested in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
            {
                foreach (MethodInfo candidate in nested.GetMethods(AccessTools.allDeclared))
                {
                    if (candidate.Name.Contains(local)) return candidate;
                }
            }
        }
        catch (Exception)
        {
        }
        return null;
    }

    [HarmonyPrepare]
    private static bool Prepare() => Debug.isDebugBuild;

    [HarmonyTargetMethods]
    private static IEnumerable<MethodBase> TargetMethods()
    {
        _handles = new Dictionary<IntPtr, IntPtr>();
        var methods = new List<MethodBase>();

        foreach ((string marker, Type type, string name) in GetTargets())
        {
            MethodInfo? method = Find(type, name);
            if (method == null)
            {
                Plugin.Log.LogWarning($"Could not find the method for profiler marker {marker}. Skipping it.");
                continue;
            }

            IntPtr handle = new ProfilerMarker(marker).Handle;
            if (handle == IntPtr.Zero) continue;

            _handles[method.MethodHandle.Value] = handle;
            methods.Add(method);
        }

        return methods;
    }

    [HarmonyPrefix]
    private static void Prefix(MethodBase __originalMethod)
    {
        if (_handles.TryGetValue(__originalMethod.MethodHandle.Value, out IntPtr handle))
        {
            ProfilerUnsafeUtility.BeginSample(handle);
        }
    }

    [HarmonyFinalizer]
    private static Exception? Finalizer(MethodBase __originalMethod, Exception? __exception)
    {
        if (_handles.TryGetValue(__originalMethod.MethodHandle.Value, out IntPtr handle))
        {
            ProfilerUnsafeUtility.EndSample(handle);
        }
        return __exception;
    }

    [HarmonyCleanup]
    private static Exception? Cleanup(MethodBase? original, Exception? ex)
    {
        if (ex != null) Plugin.Log.LogWarning($"Could not patch {original?.Name ?? "the methods"} for profiling, {ex.Message}");
        return null;
    }
}
