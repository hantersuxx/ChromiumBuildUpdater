using System;
using System.Text.RegularExpressions;

namespace ChromiumBuildUpdater.Core.Services;

public static class VersionService
{
    private static readonly Regex NumericVersionPattern = new(
        @"(?<!\d)\d+(?:\.\d+){1,3}(?!\d)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string NormalizeReleaseVersion(string rawTag)
    {
        if (string.IsNullOrWhiteSpace(rawTag))
            return "0.0.0.0";

        string cleaned = rawTag.Trim().TrimStart('v', 'V', 'M', 'm');
        int dashIndex = cleaned.IndexOf('-');
        return dashIndex > 0 ? cleaned[..dashIndex] : cleaned;
    }

    public static bool IsRemoteNewer(string localVersion, string remoteVersion)
    {
        if (localVersion is "Not Installed" or "Unknown")
            return true;

        Version? local = Parse(localVersion);
        Version? remote = Parse(remoteVersion);
        return local is not null && remote is not null && remote > local;
    }

    private static Version? Parse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        Match match = NumericVersionPattern.Match(raw);
        if (!match.Success)
            return null;

        string[] components = match.Value.Split('.');
        int[] parts = new int[4];
        for (int i = 0; i < components.Length; i++)
        {
            if (!int.TryParse(components[i], out parts[i]))
                return null;
        }

        return new Version(parts[0], parts[1], parts[2], parts[3]);
    }
}
