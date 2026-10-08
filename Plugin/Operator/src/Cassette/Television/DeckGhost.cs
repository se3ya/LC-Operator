using UnityEngine;
using UnityEngine.Rendering;

namespace Operator.Cassette;

internal static class DeckGhost
{
    private static PlaceableShipObject? _target;
    private static MeshRenderer? _ghost;
    private static MeshRenderer? _outline;
    private static bool _shown;
    private static bool _attached;

    internal static void Sync(ShipBuildModeManager manager)
    {
        PlaceableShipObject? placing = manager.InBuildMode ? manager.placingObject : null;
        if (placing == null)
        {
            if (_shown) Hide();
            return;
        }

        if (!_shown || placing != _target) _attached = Attach(manager, placing);
        if (!_attached) return;

        Mirror(_ghost, manager.ghostObjectRenderer);
        Mirror(_outline, manager.selectionOutlineRenderer);
    }

    private static bool Attach(ShipBuildModeManager manager, PlaceableShipObject placing)
    {
        Hide();
        _target = placing;
        _shown = true;

        CassetteTakerDeck? deck = placing.parentObject != null ? placing.parentObject.GetComponentInChildren<CassetteTakerDeck>() : null;
        Transform? holder = deck != null ? deck.transform.Find(Constants.Television.GhostObjectName) : null;
        MeshFilter? filter = holder != null ? holder.GetComponent<MeshFilter>() : null;
        if (deck == null || filter == null || filter.sharedMesh == null || placing.mainMesh == null) return false;

        Transform main = placing.mainMesh.transform;
        _ghost = Place(_ghost, manager.ghostObjectMesh.transform, Constants.Television.GhostRendererName, filter.sharedMesh, main, deck.transform, 1f);
        _outline = Place(_outline, manager.selectionOutlineMesh.transform, Constants.Television.OutlineRendererName, filter.sharedMesh, main, deck.transform, Constants.Television.OutlineScale);
        return true;
    }

    private static MeshRenderer Place(MeshRenderer? renderer, Transform parent, string name, Mesh mesh, Transform main, Transform deck, float spread)
    {
        if (renderer == null)
        {
            var go = new GameObject(name);
            go.AddComponent<MeshFilter>();
            renderer = go.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        Transform t = renderer.transform;
        t.SetParent(parent, false);
        t.gameObject.layer = parent.gameObject.layer;
        t.SetLocalPositionAndRotation(main.InverseTransformPoint(deck.position) / spread, Quaternion.Inverse(main.rotation) * deck.rotation);
        t.localScale = Vector3.one * (deck.lossyScale.x / main.lossyScale.x);
        renderer.GetComponent<MeshFilter>().sharedMesh = mesh;
        renderer.enabled = false;
        return renderer;
    }

    private static void Mirror(MeshRenderer? renderer, MeshRenderer source)
    {
        if (renderer == null || source == null) return;
        if (renderer.enabled != source.enabled) renderer.enabled = source.enabled;
        if (renderer.sharedMaterial != source.sharedMaterial) renderer.sharedMaterial = source.sharedMaterial;
    }

    private static void Hide()
    {
        if (_ghost != null) _ghost.enabled = false;
        if (_outline != null) _outline.enabled = false;
        _target = null;
        _shown = false;
        _attached = false;
    }
}
