using System;
using System.Collections.Generic;
using Operator.Recording;

namespace Operator.Cassette;

[Serializable]
public sealed class CameraSaveData
{
    public const float FullCharge = 0.999f;
    public const float EmptyCharge = 0.001f;
    public const int MaxStage = 3;

    public bool HasTape;
    public string TapeId = string.Empty;
    public string TapeFileName = string.Empty;
    public string TapeTimestamp = string.Empty;
    public float TapeDuration;
    public int TapeValue;
    public string TapeBreakdown = string.Empty;
    public float Charge = 1f;
    public int CrackFront;
    public int CrackBack;
    public int CrackStar;
    public List<CameraTapePart> Parts = new();
    public float TapeTime;
    public int Lobby;

    public bool IsDefault()
    {
        return !HasTape
            && Charge >= FullCharge
            && CrackFront == 0
            && CrackBack == 0
            && CrackStar == 0;
    }

    public bool ClearCracks()
    {
        if (CrackFront == 0 && CrackBack == 0 && CrackStar == 0) return false;
        CrackFront = 0;
        CrackBack = 0;
        CrackStar = 0;
        return true;
    }

    public CameraSaveData Normalized()
    {
        string file = TapeFileName ?? string.Empty;
        bool keep = HasTape && ClipName.IsSafe(file);
        List<CameraTapePart> parts = keep ? NormalizedParts(file) : new List<CameraTapePart>();
        bool continuing = parts.Count > 0;
        return new CameraSaveData
        {
            HasTape = keep,
            TapeId = keep ? (TapeId ?? string.Empty) : string.Empty,
            TapeFileName = keep ? file : string.Empty,
            TapeTimestamp = keep ? (TapeTimestamp ?? string.Empty) : string.Empty,
            TapeDuration = keep && float.IsFinite(TapeDuration) ? Math.Max(0f, TapeDuration) : 0f,
            TapeValue = keep ? Math.Max(0, TapeValue) : 0,
            TapeBreakdown = keep ? (TapeBreakdown ?? string.Empty) : string.Empty,
            Charge = float.IsNaN(Charge) ? 1f : (float)Math.Round(Math.Clamp(Charge, 0f, 1f), 3),
            CrackFront = Math.Clamp(CrackFront, 0, MaxStage),
            CrackBack = Math.Clamp(CrackBack, 0, MaxStage),
            CrackStar = CrackStar,
            Parts = parts,
            TapeTime = continuing && float.IsFinite(TapeTime) ? Math.Max(0f, TapeTime) : 0f,
            Lobby = continuing ? Lobby : 0,
        };
    }

    private List<CameraTapePart> NormalizedParts(string tapeFile)
    {
        var parts = new List<CameraTapePart>();
        if (Parts == null) return parts;
        foreach (CameraTapePart part in Parts)
        {
            if (part == null || !ClipName.IsSafe(part.File ?? string.Empty)) continue;
            parts.Add(new CameraTapePart
            {
                Owner = Math.Max(0L, part.Owner),
                File = part.File!,
                Duration = float.IsFinite(part.Duration) ? Math.Max(0f, part.Duration) : 0f,
                Value = Math.Max(0, part.Value),
                Breakdown = part.Breakdown ?? string.Empty,
                Priced = part.Priced,
            });
        }
        if (parts.Count == 0 || !string.Equals(parts[parts.Count - 1].File, tapeFile, StringComparison.OrdinalIgnoreCase)) parts.Clear();
        return parts;
    }

    public static void AddFiles(HashSet<string> into, IEnumerable<CameraSaveData> records)
    {
        foreach (CameraSaveData record in records)
        {
            if (record == null || !record.HasTape) continue;
            if (string.IsNullOrWhiteSpace(record.TapeFileName)) continue;
            into.Add(record.TapeFileName);
            if (record.Parts == null) continue;
            foreach (CameraTapePart part in record.Parts)
            {
                if (part != null && !string.IsNullOrWhiteSpace(part.File)) into.Add(part.File);
            }
        }
    }
}

[Serializable]
public sealed class CameraTapePart
{
    public long Owner;
    public string File = string.Empty;
    public float Duration;
    public int Value;
    public string Breakdown = string.Empty;
    public bool Priced;
}
