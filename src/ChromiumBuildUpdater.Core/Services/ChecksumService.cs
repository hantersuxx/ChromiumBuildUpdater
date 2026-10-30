using System;
using System.Text.RegularExpressions;

namespace ChromiumBuildUpdater.Core.Services;

public static class ChecksumService
{
    private static readonly Regex Sha256Pattern = new(
        @"\b[a-fA-F0-9]{64}\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string? NormalizeSha256(string? checksum)
    {
        if (string.IsNullOrWhiteSpace(checksum))
            return null;

        string normalized = checksum.Trim();
        if (normalized.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            normalized = normalized["sha256:".Length..].Trim();

        return normalized;
    }

    public static bool IsValidSha256(string? checksum)
    {
        string? normalized = NormalizeSha256(checksum);
        return normalized is { Length: 64 } &&
               normalized.AsSpan().IndexOfAnyExcept("0123456789abcdefABCDEF") < 0;
    }

    public static string? ExtractSha256(
        string? digest,
        string? releaseBody,
        string? assetName = null)
    {
        if (digest?.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) == true)
        {
            string? normalizedDigest = NormalizeSha256(digest);
            if (IsValidSha256(normalizedDigest))
                return normalizedDigest;
        }

        if (string.IsNullOrWhiteSpace(releaseBody))
            return null;

        if (!string.IsNullOrWhiteSpace(assetName))
        {
            string[] lines = releaseBody.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (!lines[i].Contains(assetName, StringComparison.OrdinalIgnoreCase))
                    continue;

                int end = Math.Min(lines.Length - 1, i + 2);
                var nearbyMatches = new List<string>();
                for (int line = i; line <= end; line++)
                {
                    foreach (Match match in Sha256Pattern.Matches(lines[line]))
                        nearbyMatches.Add(match.Value);
                }

                if (nearbyMatches.Count == 1)
                    return nearbyMatches[0];

                if (nearbyMatches.Count == 0 && i > 0)
                {
                    MatchCollection precedingMatches = Sha256Pattern.Matches(lines[i - 1]);
                    if (precedingMatches.Count == 1)
                        return precedingMatches[0].Value;
                }
            }
        }

        MatchCollection matches = Sha256Pattern.Matches(releaseBody);
        return matches.Count == 1 ? matches[0].Value : null;
    }
}
