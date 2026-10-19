namespace AltevaCrawler.Shared.Utilities;

using System;
using System.IO.Hashing;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;

public static class SimHashCalculator
{
    private static readonly Regex WordRegex = new Regex(@"\w+", RegexOptions.Compiled);

    public static ulong Compute64(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;

        var matches = WordRegex.Matches(text.ToLowerInvariant());
        if (matches.Count == 0) return 0;

        int[] v = new int[64];

        foreach (Match match in matches)
        {
            string word = match.Value;
            byte[] bytes = Encoding.UTF8.GetBytes(word);
            ulong hash = Crc64.HashToUInt64(bytes);

            for (int i = 0; i < 64; i++)
            {
                if ((hash & (1UL << i)) != 0)
                {
                    v[i]++;
                }
                else
                {
                    v[i]--;
                }
            }
        }

        ulong fingerprint = 0;
        for (int i = 0; i < 64; i++)
        {
            if (v[i] > 0)
            {
                fingerprint |= (1UL << i);
            }
        }

        return fingerprint;
    }

    public static int HammingDistance(ulong hash1, ulong hash2)
    {
        return BitOperations.PopCount(hash1 ^ hash2);
    }
}
