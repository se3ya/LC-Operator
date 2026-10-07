using System;
using UnityEngine;


namespace Operator;

internal sealed partial class VideoCameraItem
{
    private int OcclusionMask
    {
        get
        {
            if (_occlusionMaskParsed) return _occlusionMask;
            _occlusionMaskParsed = true;
            string raw = Constants.Visual.OcclusionLayerNames;
            string[] names = raw.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < names.Length; i++) names[i] = names[i].Trim();
            _occlusionMask = names.Length > 0 ? LayerMask.GetMask(names) : 0;
            return _occlusionMask;
        }
    }

    private float _occlusionRawTarget;
    private bool _nearPlaneHeld;

    private static readonly RaycastHit[] _occlusionHits = new RaycastHit[16];
    private static readonly Collider[] _occlusionOverlaps = new Collider[16];
    private static int _interactLayer = -2;

    private static readonly Vector2[] _nearPlanePoints =
    {
        new(-1f, -1f), new(-1f, 1f), new(1f, 1f), new(1f, -1f),
        new(0f, -1f), new(0f, 1f), new(-1f, 0f), new(1f, 0f),
    };

    private void ResetOcclusion()
    {
        _occlusionProximity = 0f;
        _occlusionRawTarget = 0f;
        _nearPlaneHeld = false;
    }

    private void UpdateCameraOcclusion(bool sampleScene)
    {
        Camera? active = ActiveCamera;
        if (active == null) return;
        Camera? eye = playerHeldBy != null ? playerHeldBy.gameplayCamera : null;
        int mask = OcclusionMask;
        float nv = Mathf.Clamp01(_nvAmount);
        float rate = Mathf.Lerp(8f, Constants.Visual.OcclusionNightVisionSlew, nv) * Time.deltaTime;
        if (nv <= 0f)
        {
            _nearPlaneHeld = false;
        }
        if (mask == 0)
        {
            _occlusionRawTarget = 0f;
            _occlusionProximity = Mathf.MoveTowards(_occlusionProximity, 0f, rate);
            return;
        }

        if (sampleScene)
        {
            _occlusionRawTarget = eye != null
                ? SampleOcclusionProximity(active, eye.transform.position, mask)
                : SampleLensBuried(active, mask);

            if (nv > 0f)
            {
                _nearPlaneHeld = IsLensBlocked(active, eye == null, mask);
                if (_nearPlaneHeld)
                {
                    _occlusionRawTarget = Mathf.Max(_occlusionRawTarget, nv);
                    _occlusionProximity = Mathf.Max(_occlusionProximity, nv);
                }
            }
        }

        _occlusionProximity = Mathf.MoveTowards(_occlusionProximity, _occlusionRawTarget, rate);
    }

    private static float SampleLensBuried(Camera active, int mask)
    {
        return IsBuried(active.transform.position, Constants.Visual.OcclusionProbeRadius, mask) ? 1f : 0f;
    }

    private static bool IsSolid(Collider collider)
    {
        if (collider == null) return false;
        if (_interactLayer == -2) _interactLayer = LayerMask.NameToLayer("InteractableObject");
        if (collider.gameObject.layer != _interactLayer) return true;
        if (collider.GetComponentInParent<VehicleController>() != null) return false;
        if (!collider.TryGetComponent(out InteractTrigger _)) return collider.TryGetComponent(out Renderer _);
        return collider.TryGetComponent(out DoorLock _)
            || collider.TryGetComponent(out EntranceTeleport _)
            || collider.TryGetComponent(out TriggerPointToDoor _)
            || collider.TryGetComponent(out AnimatedObjectTrigger _);
    }

    private static bool IsBuried(Vector3 point, float radius, int mask)
    {
        int count = Physics.OverlapSphereNonAlloc(point, radius, _occlusionOverlaps, mask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            if (IsSolid(_occlusionOverlaps[i])) return true;
        }
        return false;
    }

    private static bool IsLineBlocked(Vector3 from, Vector3 to, int mask)
    {
        Vector3 delta = to - from;
        float length = delta.magnitude;
        if (length < 0.0001f) return false;
        int count = Physics.RaycastNonAlloc(from, delta / length, _occlusionHits, length, mask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            if (IsSolid(_occlusionHits[i].collider)) return true;
        }
        return false;
    }

    private static bool CastSolid(Vector3 origin, float radius, Vector3 dir, float distance, int mask, out RaycastHit nearest)
    {
        nearest = default;
        float best = float.MaxValue;
        int count = Physics.SphereCastNonAlloc(origin, radius, dir, _occlusionHits, distance, mask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = _occlusionHits[i];
            if (hit.distance <= 0f || hit.distance >= best || !IsSolid(hit.collider)) continue;
            best = hit.distance;
            nearest = hit;
        }
        return best < float.MaxValue && best > Constants.Visual.OcclusionSelfHitEpsilon;
    }

    private float SampleOcclusionProximity(Camera active, Vector3 eye, int mask)
    {
        Vector3 lens = active.transform.position;
        float nv = Mathf.Clamp01(_nvAmount);
        float onset = Mathf.Lerp(Constants.Visual.OcclusionFadeDistance, Constants.Visual.OcclusionNightVisionOnset, nv);
        float probeRadius = Constants.Visual.OcclusionProbeRadius;
        float nearClip = active.nearClipPlane;

        float rawProximity = 0f;

        Vector3 toLens = lens - eye;
        float lensDist = toLens.magnitude;
        if (lensDist > 0.0001f)
        {
            Vector3 dir = toLens / lensDist;
            if (CastSolid(eye, probeRadius, dir, lensDist + Constants.Visual.OcclusionFadeDistance, mask, out RaycastHit hit))
            {
                float gap = hit.distance - lensDist;
                float reach = onset;
                if (nv > 0f)
                {
                    float facing = Mathf.Clamp01(-Vector3.Dot(hit.normal, active.transform.forward));
                    reach *= Mathf.Lerp(1f, Mathf.Lerp(Constants.Visual.OcclusionGrazeFloor, 1f, facing), nv);
                    gap *= Mathf.Lerp(1f, Mathf.Abs(Vector3.Dot(hit.normal, dir)), nv * facing);
                }
                rawProximity = 1f - Mathf.Clamp01((gap - nearClip) / reach);
            }
        }

        if (float.IsNaN(rawProximity))
        {
            rawProximity = 0f;
        }
        if (rawProximity < 1f && IsBuried(lens, probeRadius, mask))
        {
            rawProximity = 1f;
        }

        return rawProximity;
    }

    private bool IsLensBlocked(Camera active, bool dropped, int mask)
    {
        if (_occlusionRawTarget >= 0.999f) return true;
        if (IsLocalWebcam()) return false;

        float depth = active.nearClipPlane + (_nearPlaneHeld
            ? Constants.Visual.OcclusionNearPlaneRelease
            : Constants.Visual.OcclusionNearPlaneEngage);
        if (HitsNearPlane(active, depth, mask)) return true;
        if (!dropped || _cameraMode.Value) return false;

        Transform t = active.transform;
        return IsLineBlocked(t.position - t.forward * Constants.Visual.OcclusionDroppedReach, t.position, mask);
    }

    private static bool HitsNearPlane(Camera active, float depth, int mask)
    {
        active.transform.GetPositionAndRotation(out Vector3 lens, out Quaternion rot);
        float y = Mathf.Tan(active.fieldOfView * 0.5f * Mathf.Deg2Rad) * depth;
        float x = y * active.aspect;
        for (int i = 0; i < _nearPlanePoints.Length; i++)
        {
            Vector2 p = _nearPlanePoints[i];
            Vector3 end = lens + rot * new Vector3(p.x * x, p.y * y, depth);
            if (IsLineBlocked(lens, end, mask)) return true;
        }
        return false;
    }

    private void DrawOcclusionOverlay(float t)
    {
        if (_feedTexture == null) return;
        CreateOcclusionMaterial();
        if (_occlusionOverlayMaterial == null)
        {
            if (!_occlusionMaterialMissLogged)
            {
                _occlusionMaterialMissLogged = true;
                Plugin.Log.LogWarning("Wall blocking overlay has no material.");
            }
            return;
        }

        if (t <= 0.001f) return;

        switch (VideoCameraConfig.OcclusionStyle.Value)
        {
            case Operator.OcclusionEffectStyle.PlainBlackFade: DrawPlainBlackFade(t); break;
            case Operator.OcclusionEffectStyle.VignetteClosing: DrawVignetteClosing(t); break;
            case Operator.OcclusionEffectStyle.VignetteSmear: DrawVignetteSmear(t); break;
        }
    }

    private void ApplyProximityBlur(float t)
    {
        if (t <= 0.5f) return;
        CreateBlurTextures();
        if (_occlusionBlurTempA == null) return;

        Graphics.Blit(_feedTexture, _occlusionBlurTempA);
        if (t > 0.75f && _occlusionBlurTempB != null)
        {
            Graphics.Blit(_occlusionBlurTempA, _occlusionBlurTempB);
            Graphics.Blit(_occlusionBlurTempB, _occlusionBlurTempA);
        }
        Graphics.Blit(_occlusionBlurTempA, _feedTexture);
    }

    private void DrawPlainBlackFade(float t)
    {
        CreateBlackTexture();
        _occlusionOverlayMaterial!.color = new Color(0f, 0f, 0f, t);
        Graphics.Blit(_occlusionBlackTexture, _feedTexture, _occlusionOverlayMaterial);
    }

    private void DrawVignetteClosing(float t)
    {
        CreateVignetteTexture();
        CreateBlackTexture();

        ApplyProximityBlur(t);

        _occlusionOverlayMaterial!.color = new Color(0f, 0f, 0f, t);
        Graphics.Blit(_occlusionVignetteTexture, _feedTexture, _occlusionOverlayMaterial);

        float centreFill = Mathf.Clamp01(t * 1.6f - 0.6f);
        if (centreFill > 0f)
        {
            _occlusionOverlayMaterial.color = new Color(0f, 0f, 0f, centreFill);
            Graphics.Blit(_occlusionBlackTexture, _feedTexture, _occlusionOverlayMaterial);
        }
    }

    private void DrawVignetteSmear(float t)
    {
        CreateVignetteTexture();
        CreateBlackTexture();
        CreateBlurTextures();

        ApplyProximityBlur(t);

        float pitchDelta = _bobPitch - _lastBobPitch;
        float rollDelta = _bobRoll - _lastBobRoll;
        Vector2 smearDir = new(rollDelta, pitchDelta);
        float rawMag = smearDir.magnitude;
        float smearMag = Mathf.Clamp(rawMag * 0.05f * t, 0f, 0.015f);
        if (rawMag > 0.0001f && smearMag > 0.0001f && _occlusionBlurTempA != null)
        {
            smearDir = (smearDir / rawMag) * smearMag;
            DoOffsetSelfBlit(_occlusionBlurTempA, smearDir, 0.35f);
            DoOffsetSelfBlit(_occlusionBlurTempA, -smearDir, 0.35f);
        }

        _occlusionOverlayMaterial!.color = new Color(0f, 0f, 0f, t);
        _occlusionOverlayMaterial.mainTextureOffset = Vector2.zero;
        Graphics.Blit(_occlusionVignetteTexture, _feedTexture, _occlusionOverlayMaterial);

        float centreFill = Mathf.Clamp01(t * 1.6f - 0.6f);
        if (centreFill > 0f)
        {
            _occlusionOverlayMaterial.color = new Color(0f, 0f, 0f, centreFill);
            Graphics.Blit(_occlusionBlackTexture, _feedTexture, _occlusionOverlayMaterial);
        }

        _lastBobPitch = _bobPitch;
        _lastBobRoll = _bobRoll;
    }

    private void DoOffsetSelfBlit(RenderTexture scratch, Vector2 uvOffset, float alpha)
    {
        Graphics.Blit(_feedTexture, scratch);
        _occlusionOverlayMaterial!.color = new Color(1f, 1f, 1f, alpha);
        _occlusionOverlayMaterial.mainTextureOffset = uvOffset;
        Graphics.Blit(scratch, _feedTexture, _occlusionOverlayMaterial);
        _occlusionOverlayMaterial.mainTextureOffset = Vector2.zero;
    }

    private bool _occlusionMaterialMissLogged;

    private void CreateOcclusionMaterial()
    {
        if (_occlusionOverlayMaterial != null) return;
        Shader? sh = Shader.Find("Sprites/Default") ?? Shader.Find("UI/Default");
        if (sh == null)
        {
            return;
        }
        _occlusionOverlayMaterial = new Material(sh) { name = "VideoCamOcclusionOverlay" };
    }

    private void CreateBlackTexture()
    {
        if (_occlusionBlackTexture == null) _occlusionBlackTexture = MakeSolidColorTexture(Color.black, "VideoCamOcclBlack");
    }

    private void CreateVignetteTexture()
    {
        if (_occlusionVignetteTexture != null) return;
        int size = Constants.Visual.OcclusionVignetteTextureSize;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false, linear: false)
        {
            name = "VideoCamOcclVignette",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
        };
        var pixels = new Color32[size * size];
        float half = size * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x - half) / half;
                float dy = (y - half) / half;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.25f, 1f, r));
                pixels[y * size + x] = new Color32(0, 0, 0, (byte)Mathf.RoundToInt(a * 255f));
            }
        }
        tex.SetPixels32(pixels);
        tex.Apply(updateMipmaps: false, makeNoLongerReadable: false);
        _occlusionVignetteTexture = tex;
    }

    private void CreateBlurTextures()
    {
        if (_feedTexture == null) return;
        if (_occlusionBlurTempA == null)
        {
            int w = Mathf.Max(2, _feedTexture.width / 2);
            int h = Mathf.Max(2, _feedTexture.height / 2);
            _occlusionBlurTempA = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32) { name = "VideoCamOcclBlurA", filterMode = FilterMode.Bilinear };
            _occlusionBlurTempA.Create();
        }
        if (_occlusionBlurTempB == null)
        {
            int w = Mathf.Max(2, _feedTexture.width / 4);
            int h = Mathf.Max(2, _feedTexture.height / 4);
            _occlusionBlurTempB = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32) { name = "VideoCamOcclBlurB", filterMode = FilterMode.Bilinear };
            _occlusionBlurTempB.Create();
        }
    }

    private static Texture2D MakeSolidColorTexture(Color color, string name)
    {
        var tex = new Texture2D(1, 1, TextureFormat.RGBA32, mipChain: false, linear: false)
        {
            name = name,
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Point,
        };
        tex.SetPixel(0, 0, color);
        tex.Apply(updateMipmaps: false, makeNoLongerReadable: false);
        return tex;
    }
}
