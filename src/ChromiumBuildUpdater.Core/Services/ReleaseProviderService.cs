using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ChromiumBuildUpdater.Core.Models;

namespace ChromiumBuildUpdater.Core.Services;

/// <summary>
/// Selects verified release assets for the current OS. Windows uses GitHub with
/// chromiumbuilds.org failover; macOS and Linux use their matching GitHub feeds.
/// </summary>
public class ReleaseProviderService : IReleaseProvider
{
    private const string HibbikiGitHubUrl = "https://api.github.com/repos/Hibbiki/chromium-win64/releases/latest";
    private const string UngoogledGitHubUrl = "https://api.github.com/repos/ungoogled-software/ungoogled-chromium-windows/releases/latest";
    private const string UngoogledMacGitHubUrl = "https://api.github.com/repos/ungoogled-software/ungoogled-chromium-macos/releases/latest";
    private const string ThoriumGz83GitHubUrl = "https://api.github.com/repos/gz83/thorium/releases/latest";
    private const string ThoriumAlexGitHubUrl = "https://api.github.com/repos/Alex313031/Thorium-Win/releases/latest";

    private const string ChromiumBuildsLatestJsonUrl = "https://chromiumbuilds.org/api/v1/latest.json";
    private const string ChromiumBuildsWindowsJsonUrl = "https://chromiumbuilds.org/api/v1/windows.json";
    private static readonly string[] ChromiumBuildsEndpoints =
    [
        ChromiumBuildsWindowsJsonUrl,
        ChromiumBuildsLatestJsonUrl
    ];

    private static readonly HttpClient SharedHttp = new()
    {
        Timeout = TimeSpan.FromSeconds(30),
        DefaultRequestHeaders =
        {
            { "User-Agent", "ChromiumBuildUpdater/3.0 (+https://github.com/hantersuxx/ChromiumBuildUpdater)" },
            { "Accept", "application/json" }
        }
    };

    private readonly HttpClient _http;
    private readonly HostPlatform _platform;
    private readonly Architecture _architecture;
    private readonly bool _supportsAvx2;

    public ReleaseProviderService(HttpClient? httpClient = null)
        : this(
            httpClient,
            PlatformSupport.Current,
            PlatformSupport.CurrentArchitecture,
            PlatformSupport.SupportsAvx2)
    {
    }

    internal ReleaseProviderService(
        HttpClient? httpClient,
        HostPlatform platform,
        Architecture architecture,
        bool supportsAvx2)
    {
        _http = httpClient ?? SharedHttp;
        _platform = platform;
        _architecture = architecture;
        _supportsAvx2 = supportsAvx2;
    }

    public async Task<ReleaseArtifact> FetchLatestReleaseAsync(
        ChromiumFlavor flavor,
        bool preferChromiumBuildsOrg = false,
        CancellationToken ct = default)
    {
        if (!PlatformSupport.IsFlavorAvailable(flavor, _platform))
        {
            string platform = _platform.ToString();
            throw new PlatformNotSupportedException(
                $"{flavor} updates are not supported on {platform}. " +
                $"Available builds: {string.Join(", ", PlatformSupport.GetAvailableFlavors(_platform))}.");
        }

        if (_platform != HostPlatform.Windows)
            return await FetchFromGitHubPrimaryAsync(flavor, ct);

        var errors = new List<string>();

        if (!preferChromiumBuildsOrg)
        {
            try
            {
                return await FetchFromGitHubPrimaryAsync(flavor, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (IsRecoverableFeedFailure(ex))
            {
                errors.Add($"GitHub API ({ex.Message})");
            }
        }

        // Failover (or primary when preferChromiumBuildsOrg is checked): query chromiumbuilds.org
        try
        {
            return await FetchFromChromiumBuildsOrgAsync(flavor, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (IsRecoverableFeedFailure(ex))
        {
            errors.Add($"chromiumbuilds.org ({ex.Message})");
        }

        // If preferChromiumBuildsOrg was true and failed, try GitHub as backup
        if (preferChromiumBuildsOrg)
        {
            try
            {
                return await FetchFromGitHubPrimaryAsync(flavor, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (IsRecoverableFeedFailure(ex))
            {
                errors.Add($"GitHub Backup ({ex.Message})");
            }
        }

        throw new InvalidOperationException($"All release endpoints failed: {string.Join(" | ", errors)}");
    }

    private async Task<ReleaseArtifact> FetchFromGitHubPrimaryAsync(ChromiumFlavor flavor, CancellationToken ct)
    {
        return flavor switch
        {
            ChromiumFlavor.Hibbiki => await FetchGitHubReleaseInternalAsync(
                flavor,
                HibbikiGitHubUrl,
                asset => asset.Name.Contains("mini_installer", StringComparison.OrdinalIgnoreCase) &&
                         asset.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                         !asset.Name.Contains("arm64", StringComparison.OrdinalIgnoreCase),
                "GitHub (Hibbiki/chromium-win64)",
                ct),

            ChromiumFlavor.UngoogledChromium => await FetchUngoogledReleaseAsync(ct),

            ChromiumFlavor.Thorium => await FetchThoriumGitHubReleaseAsync(ct),

            _ => throw new ArgumentOutOfRangeException(nameof(flavor), flavor, null)
        };
    }

    private async Task<ReleaseArtifact> FetchUngoogledReleaseAsync(CancellationToken ct)
    {
        if (_platform == HostPlatform.MacOS)
        {
            string architectureSuffix =
                PlatformSupport.GetUngoogledMacAssetSuffix(_architecture);
            return await FetchGitHubReleaseInternalAsync(
                ChromiumFlavor.UngoogledChromium,
                UngoogledMacGitHubUrl,
                asset => asset.Name.Contains(architectureSuffix, StringComparison.OrdinalIgnoreCase) &&
                         asset.Name.EndsWith(".dmg", StringComparison.OrdinalIgnoreCase),
                $"GitHub (ungoogled-chromium-macos, {architectureSuffix})",
                ct);
        }

        return await FetchGitHubReleaseInternalAsync(
            ChromiumFlavor.UngoogledChromium,
            UngoogledGitHubUrl,
            asset => asset.Name.Contains("installer_x64", StringComparison.OrdinalIgnoreCase) &&
                     asset.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase),
            "GitHub (ungoogled-chromium-windows)",
            ct);
    }

    private async Task<ReleaseArtifact> FetchThoriumGitHubReleaseAsync(CancellationToken ct)
    {
        string[] candidateUrls = _platform == HostPlatform.Windows
            ? [ThoriumGz83GitHubUrl, ThoriumAlexGitHubUrl]
            : [ThoriumGz83GitHubUrl];

        Func<GitHubAssetResponse, bool> assetSelector = _platform switch
        {
            HostPlatform.Windows => asset =>
                asset.Name.Contains("mini_installer", StringComparison.OrdinalIgnoreCase) &&
                asset.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                asset.Name.Contains(
                    PlatformSupport.GetThoriumWindowsAssetSuffix(_supportsAvx2),
                    StringComparison.OrdinalIgnoreCase),
            HostPlatform.Linux => CreateThoriumLinuxAssetSelector(),
            _ => throw new PlatformNotSupportedException("Thorium is currently not offered for this platform.")
        };

        var errors = new List<string>();
        foreach (string url in candidateUrls)
        {
            try
            {
                return await FetchGitHubReleaseInternalAsync(
                    ChromiumFlavor.Thorium,
                    url,
                    assetSelector,
                    url.Contains("gz83", StringComparison.Ordinal)
                        ? "GitHub (gz83/thorium; beta multi-platform builds)"
                        : "GitHub (Alex313031/Thorium-Win)",
                    ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (IsRecoverableFeedFailure(ex))
            {
                errors.Add($"{url}: {ex.Message}");
            }
        }

        throw new InvalidOperationException(
            $"All Thorium release feeds failed: {string.Join(" | ", errors)}");
    }

    private Func<GitHubAssetResponse, bool> CreateThoriumLinuxAssetSelector()
    {
        string suffix = PlatformSupport.GetThoriumLinuxAssetSuffix(
            _architecture,
            _supportsAvx2);
        return asset => asset.Name.EndsWith(".AppImage", StringComparison.OrdinalIgnoreCase) &&
                        asset.Name.Contains(suffix, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<ReleaseArtifact> FetchGitHubReleaseInternalAsync(
        ChromiumFlavor flavor,
        string apiUrl,
        Func<GitHubAssetResponse, bool> assetPredicate,
        string sourceLabel,
        CancellationToken ct)
    {
        var release = await _http.GetFromJsonAsync<GitHubReleaseResponse>(apiUrl, ct)
            ?? throw new InvalidOperationException("GitHub API returned an empty release response.");

        var installerAsset = release.Assets.FirstOrDefault(assetPredicate)
            ?? throw new InvalidOperationException($"No matching installer asset found in {sourceLabel}.");

        string cleanVersion = VersionService.NormalizeReleaseVersion(release.TagName);
        string? sha256 = ChecksumService.ExtractSha256(
            installerAsset.Digest,
            release.Body,
            installerAsset.Name);

        return new ReleaseArtifact(
            Flavor: flavor,
            CleanVersion: cleanVersion,
            FullTag: release.TagName,
            PublishedAtUtc: release.PublishedAt,
            DownloadUrl: installerAsset.BrowserDownloadUrl,
            ExpectedSha256: sha256,
            SourceName: sourceLabel,
            AssetName: installerAsset.Name
        );
    }

    /// <summary>
    /// Queries https://chromiumbuilds.org/api/v1/windows.json and /api/v1/latest.json for rate-limit-free failover.
    /// </summary>
    private async Task<ReleaseArtifact> FetchFromChromiumBuildsOrgAsync(ChromiumFlavor flavor, CancellationToken ct)
    {
        string searchKeyword = flavor switch
        {
            ChromiumFlavor.Hibbiki => "hibbiki",
            ChromiumFlavor.UngoogledChromium => "ungoogled",
            ChromiumFlavor.Thorium => "thorium",
            _ => "hibbiki"
        };
        var errors = new List<string>();

        foreach (string endpoint in ChromiumBuildsEndpoints)
        {
            try
            {
                using var response = await _http.GetAsync(endpoint, ct);
                response.EnsureSuccessStatusCode();
                await using var stream = await response.Content.ReadAsStreamAsync(ct);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

                var matched = ChromiumBuildsOrgFeedParser.TryParse(
                    doc.RootElement,
                    flavor,
                    searchKeyword,
                    endpoint);
                if (matched != null)
                    return matched;

                errors.Add($"{endpoint}: no matching x64 installer was found.");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (IsRecoverableFeedFailure(ex))
            {
                errors.Add($"{endpoint}: {ex.Message}");
            }
        }

        string details = errors.Count == 0
            ? "No matching x64 installer was found in the feeds."
            : string.Join(" | ", errors);
        throw new InvalidOperationException(
            $"Could not locate {flavor} x64 installer in chromiumbuilds.org JSON feeds. {details}");
    }

    private static bool IsRecoverableFeedFailure(Exception exception) =>
        exception is HttpRequestException or JsonException or InvalidOperationException or OperationCanceledException;
}
