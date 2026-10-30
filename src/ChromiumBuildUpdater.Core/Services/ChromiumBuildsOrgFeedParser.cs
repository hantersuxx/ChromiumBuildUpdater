using System.Globalization;
using System.Text.Json;
using ChromiumBuildUpdater.Core.Models;

namespace ChromiumBuildUpdater.Core.Services;

internal static class ChromiumBuildsOrgFeedParser
{
    public static ReleaseArtifact? TryParse(
        JsonElement root,
        ChromiumFlavor flavor,
        string keyword,
        string endpoint)
    {
        string sourceName = CreateSourceName(endpoint);
        foreach (JsonElement build in EnumerateBuildCandidates(root))
        {
            string idOrName = GetStringProperty(build, "id", "name", "slug", "title", "build");
            if (!idOrName.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                !HasSupportedArchitecture(build))
            {
                continue;
            }

            string rawVersion = GetStringProperty(build, "version", "latest_version", "tag");
            DateTime publishedUtc = ParsePublishedDate(
                GetStringProperty(build, "updated", "published", "published_at", "date", "built"));

            ReleaseArtifact? directDownload = TryParseDirectDownload(
                build,
                flavor,
                rawVersion,
                publishedUtc,
                sourceName);
            if (directDownload is not null)
                return directDownload;

            ReleaseArtifact? nestedDownload = TryParseNestedDownload(
                build,
                flavor,
                rawVersion,
                publishedUtc,
                sourceName);
            if (nestedDownload is not null)
                return nestedDownload;
        }

        return null;
    }

    private static ReleaseArtifact? TryParseDirectDownload(
        JsonElement build,
        ChromiumFlavor flavor,
        string rawVersion,
        DateTime publishedUtc,
        string sourceName)
    {
        string downloadUrl = GetStringProperty(build, "url", "download_url", "installer_url");
        if (!IsSupportedWindowsInstaller(downloadUrl))
        {
            return null;
        }

        string checksum = GetStringProperty(build, "sha256", "checksum", "hash");
        return CreateArtifact(flavor, rawVersion, publishedUtc, downloadUrl, checksum, sourceName);
    }

    private static ReleaseArtifact? TryParseNestedDownload(
        JsonElement build,
        ChromiumFlavor flavor,
        string rawVersion,
        DateTime publishedUtc,
        string sourceName)
    {
        if (!build.TryGetProperty("downloads", out JsonElement downloads) ||
            downloads.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (JsonElement download in downloads.EnumerateArray())
        {
            string downloadUrl = GetStringProperty(download, "url", "download_url", "href");
            if (!IsSupportedWindowsInstaller(downloadUrl))
                continue;

            string checksum = GetStringProperty(download, "sha256", "checksum", "digest");
            return CreateArtifact(flavor, rawVersion, publishedUtc, downloadUrl, checksum, sourceName);
        }

        return null;
    }

    private static ReleaseArtifact CreateArtifact(
        ChromiumFlavor flavor,
        string rawVersion,
        DateTime publishedUtc,
        string downloadUrl,
        string checksum,
        string sourceName) =>
        new(
            Flavor: flavor,
            CleanVersion: VersionService.NormalizeReleaseVersion(rawVersion),
            FullTag: rawVersion,
            PublishedAtUtc: publishedUtc,
            DownloadUrl: downloadUrl,
            ExpectedSha256: ChecksumService.NormalizeSha256(checksum),
            SourceName: sourceName);

    private static bool IsSupportedWindowsInstaller(string downloadUrl) =>
        !string.IsNullOrWhiteSpace(downloadUrl) &&
        downloadUrl.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
        !downloadUrl.Contains("arm64", StringComparison.OrdinalIgnoreCase) &&
        !downloadUrl.Contains("x86.", StringComparison.OrdinalIgnoreCase) &&
        !downloadUrl.Contains("32bit", StringComparison.OrdinalIgnoreCase);

    private static bool HasSupportedArchitecture(JsonElement build)
    {
        if (!build.TryGetProperty("arch", out JsonElement architecture))
            return true;

        string architectureName = architecture.ToString();
        return architectureName.Equals("64", StringComparison.OrdinalIgnoreCase) ||
               architectureName.Equals("64-bit", StringComparison.OrdinalIgnoreCase) ||
               architectureName.Contains("x64", StringComparison.OrdinalIgnoreCase) ||
               architectureName.Contains("amd64", StringComparison.OrdinalIgnoreCase) ||
               architectureName.Contains("x86_64", StringComparison.OrdinalIgnoreCase);
    }

    private static DateTime ParsePublishedDate(string value) =>
        DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal,
            out DateTimeOffset parsedDate)
            ? parsedDate.UtcDateTime
            : DateTime.UtcNow;

    private static IEnumerable<JsonElement> EnumerateBuildCandidates(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement element in root.EnumerateArray())
                yield return element;
            yield break;
        }

        if (root.ValueKind != JsonValueKind.Object)
            yield break;

        if (root.TryGetProperty("builds", out JsonElement builds) &&
            builds.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement element in builds.EnumerateArray())
                yield return element;
            yield break;
        }

        foreach (JsonProperty property in root.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.Object)
                yield return property.Value;
        }
    }

    private static string GetStringProperty(JsonElement element, params string[] candidateNames)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return string.Empty;

        foreach (string name in candidateNames)
        {
            if (element.TryGetProperty(name, out JsonElement value) &&
                value.ValueKind == JsonValueKind.String)
            {
                return value.GetString() ?? string.Empty;
            }
        }

        return string.Empty;
    }

    private static string CreateSourceName(string endpoint)
    {
        string path = Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? uri)
            ? uri.AbsolutePath
            : endpoint;
        return $"Failover (chromiumbuilds.org{path})";
    }
}
