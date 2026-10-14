using UnityEngine;

namespace Operator.Cassette;

internal sealed class CassetteTakerManager : MonoBehaviour
{
    private static CassetteTakerManager? _instance;
    private static GameObject? _deckPrefab;
    private static bool _deckPrefabMissingLogged;

    internal static TVScript? ActiveTv { get; private set; }

    internal static CassetteTakerDeck? ActiveDeck { get; private set; }

    internal static bool IsRunning => _instance != null;

    private CassetteTakerDeck? _deck;
    private TVScript? _attachedTv;
    private float _nextPoll;

    internal static void Init()
    {
        if (!VideoCameraConfig.TelevisionCassetteWatching.Value) return;
        if (_instance != null) return;

        var go = new GameObject("OperatorCassetteTakerManager");
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<CassetteTakerManager>();
    }

    internal static void Shutdown()
    {
        TelevisionPlayback.ClearAll();
        ActiveTv = null;
        if (_instance != null)
        {
            Destroy(_instance.gameObject);
            _instance = null;
        }
    }

    internal static void NotifyTvReady(TVScript tv)
    {
        if (tv == null || tv.tvMesh == null) return;
        if (!VideoCameraConfig.TelevisionCassetteWatching.Value) return;
        Init();
        if (_instance != null) _instance.AttachIfNeeded(tv);
    }

    private void Update()
    {
        TelevisionPlayback.Update();
        TelevisionCoWatch.Update();

        if (Time.time < _nextPoll) return;
        _nextPoll = Time.time + Constants.Television.AttachPollInterval;

        if (!VideoCameraConfig.TelevisionCassetteWatching.Value)
        {
            Detach();
            return;
        }

        TVScript? tv = FindActiveTv();
        if (tv == null)
        {
            Detach();
            return;
        }

        AttachIfNeeded(tv);
    }

    private void AttachIfNeeded(TVScript tv)
    {
        if (_deck == null || _attachedTv != tv)
        {
            AttachToTv(tv);
        }
        else
        {
            ActiveTv = tv;
        }
    }

    private static TVScript? FindActiveTv()
    {
        foreach (TVScript tv in FindObjectsOfType<TVScript>())
        {
            if (tv != null && tv.isActiveAndEnabled && tv.tvMesh != null)
            {
                return tv;
            }
        }
        return null;
    }

    private void AttachToTv(TVScript tv)
    {
        Detach();

        GameObject? prefab = LoadDeckPrefab();
        if (prefab == null) return;

        Transform anchor = GetAnchor(tv);
        GameObject deckGo = Instantiate(prefab, anchor);
        deckGo.name = "CassetteTakerDeck";
        deckGo.transform.localPosition = Constants.Television.DeckLocalPosition;
        deckGo.transform.localEulerAngles = Constants.Television.DeckLocalEuler;
        deckGo.transform.localScale = Constants.Television.DeckLocalScale;

        _deck = deckGo.AddComponent<CassetteTakerDeck>();
        _deck.Initialize(tv);
        _attachedTv = tv;
        ActiveTv = tv;
        ActiveDeck = _deck;
        Plugin.Log.LogInfo("Cassette deck attached to ship Television.");
    }

    private void Detach()
    {
        if (_deck != null)
        {
            Destroy(_deck.gameObject);
        }
        _deck = null;
        _attachedTv = null;
        ActiveTv = null;
        ActiveDeck = null;
    }

    private static Transform GetAnchor(TVScript tv)
    {
        var autoParent = tv.GetComponentInParent<AutoParentToShip>();
        return autoParent != null ? autoParent.transform : tv.transform;
    }

    private static GameObject? LoadDeckPrefab()
    {
        if (_deckPrefab != null) return _deckPrefab;

        _deckPrefab = AssetsLoader.LoadOptionalAsset<GameObject>(Constants.Television.DeckPrefabAssetName);
        if (_deckPrefab == null && !_deckPrefabMissingLogged)
        {
            _deckPrefabMissingLogged = true;
            Plugin.Log.LogError(
                $"Deck prefab '{Constants.Television.DeckPrefabAssetName}' is missing from asset bundle.");
        }
        return _deckPrefab;
    }
}
