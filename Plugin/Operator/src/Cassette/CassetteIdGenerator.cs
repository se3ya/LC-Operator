using System;
using System.Text;

namespace Operator.Cassette;

internal static class CassetteIdGenerator
{
    private static readonly char[] LabelLetters =
    {
        'v', 'd', 't', 'c', 'm', 'r', 'b', 'k', 'n', 'p', 's', 'w',
    };

    public static string ToMatchKey(string id)
    {
        if (string.IsNullOrEmpty(id)) return string.Empty;
        var sb = new StringBuilder(id.Length);
        foreach (char c in id)
        {
            if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }

    public static string GenerateId(string seed)
    {
        if (string.IsNullOrEmpty(seed)) seed = Guid.NewGuid().ToString();

        int h = StableHash(seed);
        var rng = new Random(h);

        char letter = LabelLetters[rng.Next(LabelLetters.Length)];

        int serialDigits = 2 + rng.Next(2);
        int max = (int)Math.Pow(10, serialDigits);
        int min = max / 10;
        int serial = rng.Next(min, max);

        return string.Concat(letter.ToString(), serial.ToString());
    }

    private static int StableHash(string s)
    {
        unchecked
        {
            int h = (int)2166136261;
            for (int i = 0; i < s.Length; i++)
            {
                h = (h ^ s[i]) * 16777619;
            }
            return h;
        }
    }
}
