using System;
using System.Text.RegularExpressions;

namespace HibbikiUpdater.Services;

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

    public static string? ExtractSha256(string? digest, string? releaseBody)
    {
        if (digest?.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) == true)
        {
            string? normalizedDigest = NormalizeSha256(digest);
            if (IsValidSha256(normalizedDigest))
                return normalizedDigest;
        }

        if (string.IsNullOrWhiteSpace(releaseBody))
            return null;

        Match match = Sha256Pattern.Match(releaseBody);
        return match.Success ? match.Value : null;
    }
}
