using System.Collections.Generic;
using UnityEngine;

namespace Operator.Cassette;

internal static class DeckSounds
{
    private static bool _resolved;

    internal static AudioClip[] Keys { get; private set; } = System.Array.Empty<AudioClip>();
    internal static AudioClip? Load { get; private set; }
    internal static AudioClip? Eject { get; private set; }
    internal static AudioClip? Motor { get; private set; }

    internal static void Init()
    {
        if (_resolved) return;
        StartOfRound? sor = StartOfRound.Instance;
        if (sor == null) return;
        _resolved = true;

        RoundManager? round = RoundManager.Instance;
        if (round != null && round.PressButtonSFX1 != null && round.PressButtonSFX2 != null)
        {
            Keys = new[] { round.PressButtonSFX1, round.PressButtonSFX2 };
        }

        if (sor.unlockablesList != null)
        {
            foreach (UnlockableItem unlockable in sor.unlockablesList.unlockables)
            {
                if (unlockable == null || unlockable.unlockableName != Constants.Television.MicrowaveUnlockable || unlockable.prefabObject == null) continue;
                var microwave = unlockable.prefabObject.GetComponentInChildren<MicrowaveItem>(true);
                if (microwave == null) continue;
                Load = microwave.microwaveClose;
                Motor = microwave.whirringAudio != null ? microwave.whirringAudio.clip : null;
                break;
            }
        }

        if (sor.allItemsList != null)
        {
            foreach (Item item in sor.allItemsList.itemsList)
            {
                if (item == null || item.itemName != Constants.Television.BoomboxItem || item.spawnPrefab == null) continue;
                var boombox = item.spawnPrefab.GetComponent<BoomboxItem>();
                if (boombox != null && boombox.stopAudios != null && boombox.stopAudios.Length > 0) Eject = boombox.stopAudios[0];
                break;
            }
        }

        if (Keys.Length == 0 || Load == null || Eject == null || Motor == null) FindByName();

        var all = new List<AudioClip?>(Keys) { Load, Eject, Motor };
        foreach (AudioClip? clip in all)
        {
            if (clip != null && clip.loadState != AudioDataLoadState.Loaded) clip.LoadAudioData();
        }
    }

    private static void FindByName()
    {
        var byName = new Dictionary<string, AudioClip>();
        foreach (AudioClip clip in Resources.FindObjectsOfTypeAll<AudioClip>())
        {
            if (clip != null && !byName.ContainsKey(clip.name)) byName[clip.name] = clip;
        }

        if (Keys.Length == 0)
        {
            var keys = new List<AudioClip>();
            foreach (string name in Constants.Television.KeyClips)
            {
                AudioClip? clip = Lookup(byName, name);
                if (clip != null) keys.Add(clip);
            }
            Keys = keys.ToArray();
        }
        if (Load == null) Load = Lookup(byName, Constants.Television.LoadClip);
        if (Eject == null) Eject = Lookup(byName, Constants.Television.EjectClip);
        if (Motor == null) Motor = Lookup(byName, Constants.Television.MotorClip);
    }

    private static AudioClip? Lookup(Dictionary<string, AudioClip> byName, string name)
    {
        if (byName.TryGetValue(name, out AudioClip clip)) return clip;
        Plugin.Log.LogWarning($"Could not find vanilla sound '{name}'. The deck plays without it.");
        return null;
    }
}
