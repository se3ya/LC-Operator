using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using GameNetcodeStuff;
using UnityEngine;

namespace Operator.Cassette;

internal sealed class EnemyBountyTracker
{
    private const int OldCloseFlag = 1;
    private const int KillFlag = 2;
    private const int FirstFlag = 4;
    private const int SharedFlag = 8;
    private const int ClientFlags = KillFlag | SharedFlag;

    public readonly struct EnemyRow
    {
        public readonly bool IsBody;
        public readonly string Name;
        public readonly float Seconds;
        public readonly int Flags;
        public readonly float Danger;
        public readonly float Frame;

        public EnemyRow(bool isBody, string name, float seconds, int flags, float danger, float frame)
        {
            IsBody = isBody;
            Name = name;
            Seconds = seconds;
            Flags = flags;
            Danger = danger;
            Frame = frame;
        }

        public EnemyRow WithFlags(int flags) => new(IsBody, Name, Seconds, flags, Danger, Frame);

        public EnemyRow WithDanger(float danger) => new(IsBody, Name, Seconds, Flags, danger, Frame);
    }

    public readonly struct BountyTotals
    {
        public readonly IReadOnlyList<EnemyRow> Rows;

        public BountyTotals(IReadOnlyList<EnemyRow> rows)
        {
            Rows = rows;
        }

        public string Serialize()
        {
            var sb = new StringBuilder(Rows.Count * 40);
            for (int i = 0; i < Rows.Count; i++)
            {
                EnemyRow r = Rows[i];
                if (i > 0) sb.Append(';');
                sb.Append(r.IsBody ? 'b' : 'e').Append('=')
                  .Append(CleanName(r.Name)).Append('=')
                  .Append(Fixed(r.Seconds)).Append('=')
                  .Append(r.Flags.ToString(CultureInfo.InvariantCulture)).Append('=')
                  .Append(Fixed(r.Danger)).Append('=')
                  .Append(Fixed(r.Frame));
            }
            return sb.ToString();
        }

        private static string Fixed(float v)
            => v.ToString("F2", CultureInfo.InvariantCulture);

        private static string CleanName(string s)
            => string.IsNullOrEmpty(s) ? "Unknown" : s.Replace(';', ',').Replace('=', '_');
    }

    internal static string Serialize(IReadOnlyList<EnemyRow> rows) => new BountyTotals(rows).Serialize();

    internal static List<EnemyRow> Parse(string breakdown, bool trusted = false)
    {
        var rows = new List<EnemyRow>();
        if (string.IsNullOrEmpty(breakdown)) return rows;

        string[] parts = breakdown.Split(';');
        for (int i = 0; i < parts.Length && rows.Count < Constants.Bounty.MaxRows; i++)
        {
            string[] fields = parts[i].Split('=');
            if (fields.Length < 4 || fields.Length > 6) continue;
            if (!float.TryParse(fields[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float seconds)) continue;
            if (float.IsNaN(seconds)) continue;

            int flags = 0;
            float danger = 1f;
            float frame = 0f;
            if (fields.Length >= 5 && !int.TryParse(fields[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out flags)) continue;
            if (fields.Length >= 5 && !float.TryParse(fields[4], NumberStyles.Float, CultureInfo.InvariantCulture, out danger)) continue;
            if (fields.Length == 6 && !float.TryParse(fields[5], NumberStyles.Float, CultureInfo.InvariantCulture, out frame)) continue;
            if (float.IsNaN(danger) || float.IsNaN(frame)) continue;
            if (fields.Length < 6 && (flags & OldCloseFlag) != 0) frame = 1f;

            rows.Add(new EnemyRow(
                fields[0] == "b",
                fields[1],
                Mathf.Clamp(seconds, 0f, Constants.Bounty.MaxRowTime),
                flags & (trusted ? ClientFlags | FirstFlag : ClientFlags),
                Mathf.Clamp(danger, Constants.Bounty.MinDanger, Constants.Bounty.MaxDanger),
                Mathf.Clamp01(frame)));
        }

        return rows;
    }

    internal static int GetValue(IReadOnlyList<EnemyRow> rows)
    {
        Dictionary<string, TypeShot> types = GetTypes(rows, out int bodies);
        float earned = 0f;
        foreach (var kv in types)
        {
            CassetteBountyConfig.TryGetBounty(kv.Key, out int bounty);
            float share = 1f + Constants.Bounty.FramingBonus * kv.Value.Frame;
            if ((kv.Value.Flags & KillFlag) != 0) share += Constants.Bounty.KillBonus;
            if ((kv.Value.Flags & FirstFlag) != 0) share += Constants.Bounty.FirstFootageBonus;
            if ((kv.Value.Flags & SharedFlag) != 0) share += Constants.Bounty.SharedShotBonus;
            earned += bounty * kv.Value.Danger * share;
        }

        float keep = 1f - Mathf.Clamp(VideoCameraConfig.DeadBodyPenalty.Value, 0, 100) / 100f;
        earned *= Mathf.Pow(keep, bodies);
        return VideoCameraConfig.FlatBaseValue.Value + Mathf.Max(0, Mathf.RoundToInt(earned));
    }

    internal static List<EnemyRow> SetDanger(IReadOnlyList<EnemyRow> rows, float danger)
    {
        var result = new List<EnemyRow>(rows.Count);
        for (int i = 0; i < rows.Count; i++)
        {
            result.Add(rows[i].IsBody ? rows[i] : rows[i].WithDanger(danger));
        }
        return result;
    }

    internal static List<EnemyRow> Counted(IReadOnlyList<EnemyRow> rows)
    {
        float minSeconds = VideoCameraConfig.MinSecondsForBounty.Value;
        var kept = new List<EnemyRow>(rows.Count);
        for (int i = 0; i < rows.Count; i++)
        {
            if (rows[i].Seconds >= minSeconds) kept.Add(rows[i]);
        }
        return kept;
    }

    internal static List<EnemyRow> Claim(IReadOnlyList<EnemyRow> rows, IReadOnlyList<EnemyRow>? prior = null)
    {
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (prior != null)
        {
            foreach (EnemyRow row in prior)
            {
                if (!row.IsBody && (row.Flags & FirstFlag) != 0) claimed.Add(row.Name);
            }
        }
        foreach (EnemyRow row in rows)
        {
            if (!row.IsBody && (row.Flags & FirstFlag) != 0) claimed.Add(row.Name);
        }

        var fresh = new List<string>();
        foreach (string name in GetTypes(rows, out _).Keys)
        {
            if (claimed.Contains(name)) continue;
            if (!FirstFootage.IsNew(name)) continue;
            claimed.Add(name);
            fresh.Add(name);
        }

        float minSeconds = VideoCameraConfig.MinSecondsForBounty.Value;
        var result = new List<EnemyRow>(rows.Count);
        var flagged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (EnemyRow row in rows)
        {
            bool paid = !row.IsBody && row.Seconds >= minSeconds
                && CassetteBountyConfig.TryGetBounty(row.Name, out int bounty) && bounty > 0;
            bool first = paid && claimed.Contains(row.Name) && flagged.Add(row.Name);
            result.Add(first ? row.WithFlags(row.Flags | FirstFlag) : row.WithFlags(row.Flags & ~FirstFlag));
        }

        if (fresh.Count > 0) FirstFootage.Mark(fresh);
        return result;
    }

    private readonly struct TypeShot
    {
        public readonly int Flags;
        public readonly float Danger;
        public readonly float Frame;

        public TypeShot(int flags, float danger, float frame)
        {
            Flags = flags;
            Danger = danger;
            Frame = frame;
        }
    }

    private static Dictionary<string, TypeShot> GetTypes(IReadOnlyList<EnemyRow> rows, out int bodies)
    {
        float minSeconds = VideoCameraConfig.MinSecondsForBounty.Value;
        var types = new Dictionary<string, TypeShot>(StringComparer.OrdinalIgnoreCase);
        var bodyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < rows.Count; i++)
        {
            EnemyRow row = rows[i];
            if (row.Seconds < minSeconds) continue;
            if (row.IsBody)
            {
                bodyNames.Add(row.Name);
                continue;
            }

            if (!CassetteBountyConfig.TryGetBounty(row.Name, out int bounty) || bounty <= 0) continue;
            types.TryGetValue(row.Name, out TypeShot prior);
            types[row.Name] = new TypeShot(
                prior.Flags | row.Flags,
                Mathf.Max(prior.Danger, row.Danger),
                Mathf.Max(prior.Frame, row.Frame));
        }
        bodies = bodyNames.Count;
        return types;
    }

    private static float _levelScrap;

    internal static float LevelScrap => _levelScrap;

    internal static void ClearLevelScrap() => _levelScrap = 0f;

    internal static void CountLevelScrap()
    {
        float total = 0f;
        foreach (GrabbableObject item in UnityEngine.Object.FindObjectsOfType<GrabbableObject>())
        {
            if (item == null || item.itemProperties == null || !item.itemProperties.isScrap) continue;
            if (item.isInShipRoom || item.isInElevator || item.isHeld) continue;
            total += item.scrapValue;
        }
        _levelScrap = total;
    }

    internal static float GetDanger()
    {
        if (_levelScrap <= 0f) return 1f;
        return Mathf.Clamp(_levelScrap / Constants.Bounty.DangerReference, Constants.Bounty.MinDanger, Constants.Bounty.MaxDanger);
    }

    private sealed class EnemyRecord
    {
        public string TypeName = "Unknown";
        public float Seconds;
        public float LastSeenTime;
        public float Danger = 1f;
        public float Frame;
        public bool Shared;
        public bool Killed;
        public Renderer[]? Renderers;
    }

    private sealed class BodyRecord
    {
        public string PlayerName = "Player";
        public float Seconds;
    }

    private readonly Camera _camera;
    private readonly Dictionary<int, EnemyRecord> _records = new();
    private readonly Dictionary<int, BodyRecord> _bodies = new();
    private readonly float _sampleInterval;
    private float _sampleAccum;

    public EnemyBountyTracker(Camera camera)
    {
        _camera = camera;
        _sampleInterval = 1f / Constants.Bounty.SampleRate;
    }

    private bool _banked;

    private readonly List<EnemyRecord> _seen = new();

    public void Tick(float dt)
    {
        if (_camera == null) return;
        if (CompanyMoon.IsFootageWorthless())
        {
            _sampleAccum = 0f;
            return;
        }

        _banked = true;
        _sampleAccum += dt;
        if (_sampleAccum < _sampleInterval) return;
        float sampleDt = _sampleAccum;
        _sampleAccum = 0f;

        Plane[] planes = GeometryUtility.CalculateFrustumPlanes(_camera);
        Vector3 camPos = _camera.transform.position;
        StartOfRound? sor = StartOfRound.Instance;
        int losMask = sor != null ? sor.collidersAndRoomMaskAndDefault : 0;
        float now = Time.time;

        TrackDeadBodies(sor, planes, camPos, losMask, sampleDt);

        RoundManager? rm = RoundManager.Instance;
        if (rm == null || rm.SpawnedEnemies == null) return;

        _seen.Clear();
        foreach (EnemyAI enemy in rm.SpawnedEnemies)
        {
            if (enemy == null) continue;

            int id = enemy.GetInstanceID();
            _records.TryGetValue(id, out EnemyRecord? rec);

            if (enemy.isEnemyDead)
            {
                if (rec != null && !rec.Killed && now - rec.LastSeenTime <= Constants.Bounty.KillWindow)
                {
                    rec.Killed = true;
                }
                continue;
            }

            Bounds? bounds = TryGetWorldBounds(enemy);
            if (bounds == null) continue;
            if (!GeometryUtility.TestPlanesAABB(planes, bounds.Value)) continue;

            Vector3 center = bounds.Value.center;
            float distance = Vector3.Distance(camPos, center);
            if (distance > Constants.Bounty.MaxDistance) continue;

            if (losMask != 0 && Physics.Linecast(camPos, center, losMask, QueryTriggerInteraction.Ignore)) continue;

            if (rec == null)
            {
                rec = new EnemyRecord
                {
                    TypeName = GetTypeName(enemy),
                    Danger = GetDanger(),
                };
                _records[id] = rec;
            }

            rec.Renderers ??= GetRenderers(enemy);
            float frame = GetFrameScore(GetCoverage(rec.Renderers, bounds.Value));
            if (frame > rec.Frame) rec.Frame = frame;
            rec.Seconds += sampleDt;
            rec.LastSeenTime = now;
            if (CassetteBountyConfig.TryGetBounty(rec.TypeName, out int pay) && pay > 0) _seen.Add(rec);
        }

        MarkShared();
    }

    private void MarkShared()
    {
        if (_seen.Count < 2) return;
        bool mixed = false;
        for (int i = 1; i < _seen.Count && !mixed; i++)
        {
            mixed = !string.Equals(_seen[i].TypeName, _seen[0].TypeName, StringComparison.OrdinalIgnoreCase);
        }
        if (!mixed) return;
        foreach (EnemyRecord rec in _seen) rec.Shared = true;
    }

    private static Renderer[] GetRenderers(EnemyAI enemy)
    {
        var kept = new List<Renderer>();
        foreach (Renderer r in enemy.GetComponentsInChildren<Renderer>(true))
        {
            if (r is MeshRenderer || r is SkinnedMeshRenderer) kept.Add(r);
        }
        return kept.ToArray();
    }

    private float GetCoverage(Renderer[] renderers, Bounds fallback)
    {
        bool any = false;
        Bounds box = fallback;
        foreach (Renderer r in renderers)
        {
            if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
            if ((_camera.cullingMask & (1 << r.gameObject.layer)) == 0) continue;
            if (!any) { box = r.bounds; any = true; }
            else box.Encapsulate(r.bounds);
        }

        Vector3 c = box.center;
        Vector3 e = box.extents;
        float minX = 1f, minY = 1f, maxX = 0f, maxY = 0f;
        bool seen = false;
        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = c + new Vector3(
                (i & 1) == 0 ? -e.x : e.x,
                (i & 2) == 0 ? -e.y : e.y,
                (i & 4) == 0 ? -e.z : e.z);
            Vector3 v = _camera.WorldToViewportPoint(corner);
            if (v.z <= 0f) return 1f;
            seen = true;
            minX = Mathf.Min(minX, v.x);
            minY = Mathf.Min(minY, v.y);
            maxX = Mathf.Max(maxX, v.x);
            maxY = Mathf.Max(maxY, v.y);
        }
        if (!seen) return 0f;

        float w = Mathf.Min(maxX, 1f) - Mathf.Max(minX, 0f);
        float h = Mathf.Min(maxY, 1f) - Mathf.Max(minY, 0f);
        return Mathf.Max(0f, w) * Mathf.Max(0f, h);
    }

    private static float GetFrameScore(float coverage)
    {
        Vector2[] keys = Constants.Bounty.FramingKeys;
        if (coverage <= keys[0].x) return keys[0].y;
        for (int i = 1; i < keys.Length; i++)
        {
            if (coverage > keys[i].x) continue;
            float t = Mathf.InverseLerp(keys[i - 1].x, keys[i].x, coverage);
            return Mathf.Lerp(keys[i - 1].y, keys[i].y, t);
        }
        return keys[keys.Length - 1].y;
    }

    private void TrackDeadBodies(StartOfRound? round, Plane[] planes, Vector3 camPos, int losMask, float sampleDt)
    {
        if (round == null || round.allPlayerScripts == null) return;

        foreach (PlayerControllerB player in round.allPlayerScripts)
        {
            if (player == null) continue;

            DeadBodyInfo body = player.deadBody;
            if (body == null || !body.gameObject.activeInHierarchy) continue;
            if (body.isInShip || body.deactivated) continue;
            if (body.grabBodyObject != null && body.grabBodyObject.isHeld) continue;

            Bounds? bounds = TryGetBodyBounds(body);
            if (bounds == null) continue;
            if (!GeometryUtility.TestPlanesAABB(planes, bounds.Value)) continue;

            Vector3 center = bounds.Value.center;
            if (Vector3.Distance(camPos, center) > Constants.Bounty.MaxDistance) continue;
            if (losMask != 0 && Physics.Linecast(camPos, center, losMask, QueryTriggerInteraction.Ignore)) continue;

            int id = body.GetInstanceID();
            if (!_bodies.TryGetValue(id, out BodyRecord? rec))
            {
                rec = new BodyRecord { PlayerName = GetPlayerName(player) };
                _bodies[id] = rec;
            }
            rec.Seconds += sampleDt;
        }
    }

    public BountyTotals Finalize()
    {
        var rows = new List<EnemyRow>();
        if (!_banked) return new BountyTotals(rows);

        foreach (var kv in _records)
        {
            EnemyRecord rec = kv.Value;
            if (rec.Seconds <= 0f) continue;
            int flags = (rec.Killed ? KillFlag : 0) | (rec.Shared ? SharedFlag : 0);
            rows.Add(new EnemyRow(false, rec.TypeName, rec.Seconds, flags, rec.Danger, rec.Frame));
        }

        foreach (var kv in _bodies)
        {
            BodyRecord body = kv.Value;
            if (body.Seconds <= 0f) continue;
            rows.Add(new EnemyRow(true, body.PlayerName, body.Seconds, 0, 1f, 0f));
        }

        return new BountyTotals(rows);
    }

    private static string GetTypeName(EnemyAI enemy)
    {
        if (enemy.enemyType != null && !string.IsNullOrWhiteSpace(enemy.enemyType.enemyName))
        {
            return enemy.enemyType.enemyName;
        }
        return enemy.enemyType != null ? enemy.enemyType.name : "Unknown";
    }

    private static string GetPlayerName(PlayerControllerB player)
        => string.IsNullOrWhiteSpace(player.playerUsername) ? "Player" : player.playerUsername;

    private static Bounds? TryGetBodyBounds(DeadBodyInfo body)
    {
        var skinned = body.GetComponentInChildren<SkinnedMeshRenderer>();
        if (skinned != null) return skinned.bounds;

        var rend = body.GetComponentInChildren<Renderer>();
        if (rend != null) return rend.bounds;

        var col = body.GetComponentInChildren<Collider>();
        if (col != null) return col.bounds;

        return null;
    }

    private static Bounds? TryGetWorldBounds(EnemyAI enemy)
    {
        var col = enemy.GetComponent<Collider>();
        if (col == null) col = enemy.GetComponentInChildren<Collider>();
        if (col != null) return col.bounds;

        var rend = enemy.GetComponentInChildren<Renderer>();
        if (rend != null) return rend.bounds;

        return null;
    }
}
