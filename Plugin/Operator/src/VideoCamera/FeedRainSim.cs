using System.Collections.Generic;
using UnityEngine;

namespace Operator;

internal sealed class FeedCrackFlow
{
    internal const int GridW = 160;
    internal const int GridH = 90;

    private byte[]? _grid;

    public bool Active => _grid != null;

    public void Clear() => _grid = null;

    public void SetGrid(byte[] data) =>
        _grid = data != null && data.Length >= GridW * GridH ? data : null;

    public int Sample(float canvasX, float canvasY)
    {
        byte[]? g = _grid;
        if (g == null) return 0;
        int gx = (int)(canvasX / FeedRainSim.CanvasW * GridW);
        int gy = (int)((1f - (canvasY / FeedRainSim.CanvasH)) * GridH);
        if (gx < 0 || gx >= GridW || gy < 0 || gy >= GridH) return 0;
        return g[(gy * GridW) + gx];
    }
}

internal sealed class FeedRainSim
{
    internal const int CanvasW = 800;
    internal const int CanvasH = 450;
    internal const int MaxGpuDrops = 512;

    private const int MaxStatic = 230;
    private const int MaxBead = 220;
    private const int MaxSlider = 22;

    private const float StaticPerSec = 16f;
    private const float SliderPerSec = 1.2f;

    private const float SpawnMin = 3.2f;
    private const float SpawnMax = 8.0f;
    private const float SpawnBias = 1.7f;
    private const float BigChance = 0.06f;
    private const float BigMin = 8f;
    private const float BigMax = 11f;
    private const float SliderSpawnMin = 7f;
    private const float SliderSpawnMax = 11f;

    private const float KillRadius = 1.1f;
    private const float MaxRadius = 24f;
    private const float WetAreaR2Cap = 25000f;

    private const float BirthTime = 0.28f;
    private const float SplatOvershoot = 1.15f;
    private const float DieTime = 0.20f;
    private const float MergeDieTime = 0.13f;
    private const float MaxStaticDwell = 11f;
    private const float RunSize = 12.5f;
    private const float RunChancePerSec = 0.5f;
    private const float MergeGap = 0.72f;
    private const float MergeInterval = 0.12f;
    private const float AbsorbGain = 0.55f;
    private const float WetShrinkPerSec = 0.12f;
    private const float WetShrinkAbs = 1.1f;
    private const float BeadShrinkPerSec = 1.0f;
    private const float DryKFactor = 2.2f;
    private const float BeadDryMul = 1.6f;
    private const float DripChancePerSec = 0.06f;
    private const float DripMinSize = 6f;

    private const float TerminalBase = 40f;
    private const float TerminalSizeK = 4.5f;
    private const float TerminalRefSize = 6f;
    private const float FallTau = 0.9f;
    private const float SideTau = 0.35f;
    private const float MeanderFreqMin = 0.8f, MeanderFreqMax = 2.2f;
    private const float MeanderAmpMin = 6f, MeanderAmpMax = 22f;
    private const float StallSize = 4.2f;
    private const float SlowSpeed = 7f;
    private const float StallAfter = 1.1f;
    private const float OffscreenMargin = 40f;

    private const int CrackOnThreshold = 14;
    private const float CrackProbePx = 6f;
    private const float CrackFollowPx = 26f;
    private const float CrackHugSlow = 0.8f;
    private const float CrackWetSpacing = 0.65f;

    private const float FrameReleaseFrac = 0.7f;
    private const float FrameEdgeMargin = 20f;
    private const float StaticEdgeMargin = 22f;
    private const float CurveGain = 7f;

    private const float TrailSpacing = 9f;
    private const float TrailSpacingJitter = 0.3f;
    private const float TrailBeadFrac = 0.25f;
    private const float WeightLossPerBead = 0.012f;

    private const float AspectMin = 0.72f;
    private const float AspectMax = 1.35f;
    private const float BigAspectMin = 1.15f;
    private const float BigAspectMax = 1.6f;
    private const float StaticTilt = 0.45f;
    private const float SliderStretchK = 0.006f;
    private const float SliderStretchMax = 0.75f;

    private enum Kind { Static, Slider, Bead }

    private sealed class Drop
    {
        public Kind Kind;
        public Vector2 Pos;
        public Vector2 Vel;
        public float Size;
        public float Age;
        public float Aspect;
        public float Rot;
        public float Seed;
        public float Birth;
        public float Die = -1f;
        public float DieRate;
        public float MeanderPhase, MeanderFreq, MeanderAmp;
        public float TravelAcc;
        public float NextTrailAt;
        public float SlowTime;
    }

    private readonly List<Drop> _statics = new(MaxStatic);
    private readonly List<Drop> _sliders = new(MaxSlider);
    private readonly List<Drop> _beads = new(MaxBead);
    private readonly Stack<Drop> _pool = new();

    public readonly Vector4[] GpuData = new Vector4[MaxGpuDrops * 2];
    public int DropCount { get; private set; }

    public readonly FeedCrackFlow CrackFlow = new();

    private float _staticBudget;
    private float _sliderBudget;
    private float _mergeTimer;
    private float _wetR2;

    private bool HasRoom => _wetR2 < WetAreaR2Cap;

    public void Reset()
    {
        RecycleAll(_statics);
        RecycleAll(_sliders);
        RecycleAll(_beads);
        _staticBudget = 0f;
        _sliderBudget = 0f;
        _mergeTimer = 0f;
        DropCount = 0;
    }

    public void Update(float dt, float density, bool drying, float dryTime,
        float dropSizeMul, float slideMul, float windX, float fallMul)
    {
        dt = Mathf.Min(dt, 0.1f);
        float dryK = drying ? DryKFactor / Mathf.Max(dryTime, 1f) : 0f;

        if (!drying && density > 0.001f)
        {
            _staticBudget += dt * StaticPerSec * density;
            while (_staticBudget >= 1f)
            {
                _staticBudget -= 1f;
                if (_statics.Count < MaxStatic && HasRoom) SpawnStatic(dropSizeMul);
            }

            _sliderBudget += dt * SliderPerSec * density;
            while (_sliderBudget >= 1f)
            {
                _sliderBudget -= 1f;
                if (_sliders.Count < MaxSlider) SpawnSlider(dropSizeMul);
            }
        }

        UpdateStatics(dt, drying, dryK, dropSizeMul);
        UpdateSliders(dt, dryK, slideMul, windX, fallMul);
        UpdateBeads(dt, dryK);

        _mergeTimer -= dt;
        if (_mergeTimer <= 0f)
        {
            _mergeTimer = MergeInterval;
            MergeStatics();
        }

        PackGpuData();
    }

    private void UpdateStatics(float dt, bool drying, float dryK, float sizeMul)
    {
        for (int i = _statics.Count - 1; i >= 0; i--)
        {
            Drop d = _statics[i];
            if (UpdateDeath(d, dt)) { Recycle(_statics, i); continue; }
            d.Birth = Mathf.Min(d.Birth + (dt / BirthTime), 1f);

            d.Age += dt;
            if (d.Age > MaxStaticDwell && d.Die < 0f) { StartDie(d, DieTime); continue; }

            if (drying)
            {
                d.Size -= ((WetShrinkAbs * 0.5f) + (d.Size * dryK)) * dt;
            }
            else
            {
                d.Size -= (WetShrinkAbs + (d.Size * WetShrinkPerSec)) * dt;
            }

            if (d.Size < KillRadius) { StartDie(d, DieTime); continue; }

            float runAt = Mathf.Min(RunSize * sizeMul, MaxRadius * 0.85f);
            bool letsGo = drying
                ? d.Size >= DripMinSize * sizeMul && Random.value < DripChancePerSec * dt
                : d.Size >= runAt && Random.value < RunChancePerSec * dt;
            if (letsGo && _sliders.Count < MaxSlider)
            {
                _statics.RemoveAt(i);
                ToSlider(d);
            }
        }
    }

    private void UpdateSliders(float dt, float dryK, float slideMul, float windX, float fallMul)
    {
        for (int i = _sliders.Count - 1; i >= 0; i--)
        {
            Drop d = _sliders[i];
            if (UpdateDeath(d, dt)) { Recycle(_sliders, i); continue; }
            d.Birth = Mathf.Min(d.Birth + (dt / BirthTime), 1f);

            if (dryK > 0f) d.Size -= d.Size * dryK * 0.5f * dt;
            if (d.Size < KillRadius) { StartDie(d, DieTime); continue; }

            bool onCrack = false;
            float crackSteer = 0f;
            if (CrackFlow.Active && CrackFlow.Sample(d.Pos.x, d.Pos.y) > CrackOnThreshold)
            {
                int bl = CrackFlow.Sample(d.Pos.x - CrackProbePx, d.Pos.y - CrackProbePx);
                int bd = CrackFlow.Sample(d.Pos.x, d.Pos.y - CrackProbePx);
                int br = CrackFlow.Sample(d.Pos.x + CrackProbePx, d.Pos.y - CrackProbePx);
                int best = Mathf.Max(bd, Mathf.Max(bl, br));
                if (best > CrackOnThreshold)
                {
                    onCrack = true;
                    crackSteer = best == bd ? 0f : (best == br ? CrackFollowPx : -CrackFollowPx);
                }
            }

            float terminal = (TerminalBase
                + (TerminalSizeK * Mathf.Max(d.Size - TerminalRefSize, 0f))) * slideMul
                * (onCrack ? CrackHugSlow : 1f);
            float kFall = 1f - Mathf.Exp(-dt / FallTau);
            d.Vel.y = Mathf.Lerp(d.Vel.y, -terminal * fallMul, kFall);

            d.MeanderPhase += d.MeanderFreq * dt;
            float curve = ((d.Pos.x / CanvasW) - 0.5f) * 2f * CurveGain * slideMul;
            float sideTarget = onCrack
                ? (windX * 0.3f) + crackSteer
                : windX + curve + (Mathf.Sin(d.MeanderPhase) * d.MeanderAmp);
            float kSide = 1f - Mathf.Exp(-dt / SideTau);
            d.Vel.x = Mathf.Lerp(d.Vel.x, sideTarget, kSide);

            Vector2 delta = d.Vel * dt;
            d.Pos += delta;

            if (d.Pos.x < -OffscreenMargin || d.Pos.x > CanvasW + OffscreenMargin
                || d.Pos.y < -OffscreenMargin || d.Pos.y > CanvasH + OffscreenMargin)
            {
                Recycle(_sliders, i);
                continue;
            }

            AbsorbStatics(d);

            d.TravelAcc += delta.magnitude;
            while (d.TravelAcc >= d.NextTrailAt)
            {
                d.TravelAcc -= d.NextTrailAt;
                d.NextTrailAt = TrailSpacing * (onCrack ? CrackWetSpacing : 1f)
                    * Random.Range(1f - TrailSpacingJitter, 1f + TrailSpacingJitter);
                Vector2 dir = d.Vel.sqrMagnitude > 1e-4f ? d.Vel.normalized : Vector2.down;
                Vector2 tail = d.Pos - (dir * (d.Size * 0.9f));
                SpawnBead(tail, d.Size * TrailBeadFrac * Random.Range(0.7f, 1.15f), d.Seed);
                d.Size *= 1f - WeightLossPerBead;
            }

            d.SlowTime = d.Vel.magnitude < SlowSpeed ? d.SlowTime + dt : 0f;
            if ((d.Size < StallSize || d.SlowTime > StallAfter) && _statics.Count < MaxStatic
                && d.Pos.y >= StaticEdgeMargin
                && d.Pos.y <= CanvasH - StaticEdgeMargin)
            {
                _sliders.RemoveAt(i);
                ToStatic(d);
            }
        }
    }

    private void UpdateBeads(float dt, float dryK)
    {
        for (int i = _beads.Count - 1; i >= 0; i--)
        {
            Drop d = _beads[i];
            if (UpdateDeath(d, dt)) { Recycle(_beads, i); continue; }
            d.Birth = Mathf.Min(d.Birth + (dt / (BirthTime * 0.5f)), 1f);

            d.Size -= (BeadShrinkPerSec + (d.Size * dryK * BeadDryMul)) * dt;
            if (d.Size < KillRadius) StartDie(d, DieTime);
        }
    }

    private void AbsorbStatics(Drop s)
    {
        for (int i = _statics.Count - 1; i >= 0; i--)
        {
            Drop t = _statics[i];
            if (t.Die >= 0f) continue;
            float reach = 0.8f * (s.Size + t.Size);
            if ((t.Pos - s.Pos).sqrMagnitude < reach * reach)
            {
                s.Size = Mathf.Min(Mathf.Sqrt((s.Size * s.Size) + (AbsorbGain * t.Size * t.Size)), MaxRadius);
                StartDie(t, MergeDieTime);
            }
        }
    }

    private void MergeStatics()
    {
        int n = _statics.Count;
        for (int i = 0; i < n; i++)
        {
            Drop a = _statics[i];
            if (a.Die >= 0f || a.Birth < 1f) continue;
            for (int j = i + 1; j < n; j++)
            {
                Drop b = _statics[j];
                if (b.Die >= 0f || b.Birth < 1f) continue;
                float gap = MergeGap * (a.Size + b.Size);
                if ((a.Pos - b.Pos).sqrMagnitude >= gap * gap) continue;

                Drop big = a.Size >= b.Size ? a : b;
                Drop small = big == a ? b : a;
                float wa = big.Size * big.Size;
                float wb = small.Size * small.Size;
                big.Pos = Vector2.Lerp(big.Pos, small.Pos, 0.6f * wb / (wa + wb));
                big.Size = Mathf.Min(Mathf.Sqrt(wa + wb), MaxRadius);
                StartDie(small, MergeDieTime);
                if (big == b) break;
            }
        }
    }

    private void SpawnStatic(float sizeMul)
    {
        bool big = Random.value < BigChance;
        float size = big
            ? Mathf.Lerp(BigMin, BigMax, Random.value)
            : Mathf.Lerp(SpawnMin, SpawnMax, Mathf.Pow(Random.value, SpawnBias));

        Drop d = Get();
        d.Kind = Kind.Static;
        float x = Random.Range(0f, (float)CanvasW);
        d.Pos = new Vector2(x, Random.Range(StaticEdgeMargin, CanvasH - StaticEdgeMargin));
        d.Vel = Vector2.zero;
        d.Size = size * sizeMul;
        d.Aspect = big ? Random.Range(BigAspectMin, BigAspectMax) : Random.Range(AspectMin, AspectMax);
        d.Rot = Random.Range(-StaticTilt, StaticTilt);
        _statics.Add(d);

        if (big)
        {
            int sats = Random.Range(2, 5);
            for (int s = 0; s < sats && _statics.Count < MaxStatic; s++)
            {
                Drop m = Get();
                m.Kind = Kind.Static;
                float ang = Random.value * 6.2832f;
                float dist = d.Size * Random.Range(1.6f, 2.6f);
                float mx = Mathf.Clamp(d.Pos.x + (Mathf.Cos(ang) * dist), 0f, CanvasW);
                m.Pos = new Vector2(mx, Mathf.Clamp(d.Pos.y + (Mathf.Sin(ang) * dist),
                    StaticEdgeMargin, CanvasH - StaticEdgeMargin));
                m.Vel = Vector2.zero;
                m.Size = Random.Range(1.6f, 2.6f) * sizeMul;
                m.Aspect = Random.Range(0.8f, 1.2f);
                m.Rot = Random.Range(-StaticTilt, StaticTilt);
                _statics.Add(m);
            }
        }
    }

    private void SpawnSlider(float sizeMul)
    {
        Drop d = Get();
        d.Kind = Kind.Slider;
        float x = Random.Range(FrameEdgeMargin, CanvasW - FrameEdgeMargin);
        d.Pos = Random.value < FrameReleaseFrac
            ? new Vector2(x, CanvasH - Random.Range(2f, 10f))
            : new Vector2(x, Random.Range(CanvasH * 0.45f, (float)CanvasH));
        d.Vel = new Vector2(0f, -12f);
        d.Size = Mathf.Lerp(SliderSpawnMin, SliderSpawnMax, Random.value) * sizeMul;
        d.Aspect = Random.Range(0.9f, 1.05f);
        InitMeander(d);
        _sliders.Add(d);
    }

    private void SpawnBead(Vector2 pos, float size, float parentSeed)
    {
        if (size < KillRadius || _beads.Count >= MaxBead) return;
        Drop d = Get();
        d.Kind = Kind.Bead;
        d.Pos = pos;
        d.Vel = Vector2.zero;
        d.Size = size;
        d.Aspect = Random.Range(0.85f, 1.0f);
        d.Rot = 0f;
        d.Seed = parentSeed + (Random.value * 37f);
        _beads.Add(d);
    }

    private void ToSlider(Drop d)
    {
        d.Kind = Kind.Slider;
        d.Vel = new Vector2(0f, -12f);
        d.Aspect = Mathf.Lerp(d.Aspect, 0.95f, 0.7f);
        d.TravelAcc = 0f;
        d.NextTrailAt = TrailSpacing;
        d.SlowTime = 0f;
        InitMeander(d);
        _sliders.Add(d);
    }

    private void ToStatic(Drop d)
    {
        d.Kind = Kind.Static;
        d.Vel = Vector2.zero;
        d.Age = 0f;
        d.Rot = Random.Range(-StaticTilt, StaticTilt);
        _statics.Add(d);
    }

    private void InitMeander(Drop d)
    {
        d.MeanderPhase = Random.value * 6.2832f;
        d.MeanderFreq = Random.Range(MeanderFreqMin, MeanderFreqMax);
        d.MeanderAmp = Random.Range(MeanderAmpMin, MeanderAmpMax);
    }

    private static float BirthScale(float b)
    {
        if (b >= 1f) return 1f;
        if (b < 0.55f)
        {
            float t = b / 0.55f;
            return t * t * (3f - (2f * t)) * SplatOvershoot;
        }
        float u = (b - 0.55f) / 0.45f;
        u = u * u * (3f - (2f * u));
        return Mathf.Lerp(SplatOvershoot, 1f, u);
    }

    private static bool UpdateDeath(Drop d, float dt)
    {
        if (d.Die < 0f) return false;
        d.Die -= d.DieRate * dt;
        return d.Die <= 0f;
    }

    private static void StartDie(Drop d, float time)
    {
        if (d.Die >= 0f) return;
        d.Die = 1f;
        d.DieRate = 1f / time;
    }

    private Drop Get()
    {
        Drop d = _pool.Count > 0 ? _pool.Pop() : new Drop();
        d.Birth = 0f;
        d.Die = -1f;
        d.Age = 0f;
        d.TravelAcc = 0f;
        d.NextTrailAt = TrailSpacing;
        d.SlowTime = 0f;
        d.Seed = Random.Range(1f, 997f);
        return d;
    }

    private void Recycle(List<Drop> list, int i)
    {
        _pool.Push(list[i]);
        list.RemoveAt(i);
    }

    private void RecycleAll(List<Drop> list)
    {
        foreach (Drop d in list) _pool.Push(d);
        list.Clear();
    }

    private void PackGpuData()
    {
        _wetR2 = 0f;
        for (int i = 0; i < _statics.Count; i++) _wetR2 += _statics[i].Size * _statics[i].Size;
        for (int i = 0; i < _beads.Count; i++) _wetR2 += _beads[i].Size * _beads[i].Size;

        int n = 0;
        n = PackList(_statics, n);
        n = PackList(_beads, n);
        n = PackList(_sliders, n);
        DropCount = n;
    }

    private int PackList(List<Drop> list, int n)
    {
        for (int i = 0; i < list.Count && n < MaxGpuDrops; i++)
        {
            Drop d = list[i];
            float die = d.Die >= 0f ? d.Die * d.Die * (3f - (2f * d.Die)) : 1f;
            float scale = BirthScale(d.Birth) * die;
            if (scale <= 0.01f) continue;

            float rx = d.Size * d.Aspect;
            float ry = d.Size;
            float rot = d.Rot;
            if (d.Kind == Kind.Slider)
            {
                float stretch = Mathf.Min(d.Vel.magnitude * SliderStretchK, SliderStretchMax);
                ry *= 1f + stretch;
                rx *= 1f - (stretch * 0.30f);
                rot = Mathf.Atan2(d.Vel.y, d.Vel.x) + (Mathf.PI * 0.5f);
            }

            GpuData[n * 2] = new Vector4(d.Pos.x, d.Pos.y, rx * scale, ry * scale);
            GpuData[(n * 2) + 1] = new Vector4(Mathf.Cos(rot), Mathf.Sin(rot), d.Seed, 1f);
            n++;
        }
        return n;
    }
}
