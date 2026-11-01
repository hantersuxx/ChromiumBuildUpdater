using System.Formats.Tar;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using ChromiumBuildUpdater.Core.Models;

namespace ChromiumBuildUpdater.Core.Services;

public sealed record AppUpdateRelease(string Version, string AssetName, Uri DownloadUri, string Sha256);

public sealed record StagedAppUpdate(string ExecutablePath, string WorkingDirectory);

public enum AppUpdateProgressStage
{
    Downloading,
    Verifying,
    Preparing
}

public sealed record AppUpdateProgress(
    AppUpdateProgressStage Stage,
    long BytesTransferred = 0,
    long? TotalBytes = null);

public sealed class AppUpdateService
{
    private const string LatestReleaseApi =
        "https://api.github.com/repos/hantersuxx/ChromiumBuildUpdater/releases/latest";
    private static readonly Regex ReleaseTagPattern = new(
        @"^v?\d+(?:\.\d+){1,3}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly HttpClient SharedHttp = CreateHttpClient();
    private readonly HttpClient _http;
    private readonly HostPlatform _platform;
    private readonly Architecture _architecture;
    private readonly string _currentVersion;

    public AppUpdateService()
        : this(SharedHttp, PlatformSupport.Current, PlatformSupport.CurrentArchitecture,
            typeof(AppUpdateService).Assembly.GetName().Version?.ToString() ?? "0.0.0")
    {
    }

    public AppUpdateService(
        HttpClient httpClient,
        HostPlatform platform,
        Architecture architecture,
        string currentVersion)
    {
        _http = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _platform = platform;
        _architecture = architecture;
        _currentVersion = currentVersion ?? throw new ArgumentNullException(nameof(currentVersion));
    }

    public async Task<AppUpdateRelease?> CheckForUpdateAsync(CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _http.GetAsync(LatestReleaseApi, cancellationToken);
        response.EnsureSuccessStatusCode();
        GitHubReleaseResponse release = await response.Content.ReadFromJsonAsync<GitHubReleaseResponse>(
            cancellationToken: cancellationToken)
            ?? throw new InvalidDataException("The latest app release response was empty.");

        if (!VersionService.IsRemoteNewer(_currentVersion, release.TagName))
            return null;

        string assetName = GetAssetName(release.TagName, _platform, _architecture);
        GitHubAssetResponse appAsset = release.Assets.FirstOrDefault(asset =>
            string.Equals(asset.Name, assetName, StringComparison.Ordinal))
            ?? throw new InvalidDataException($"The latest release does not contain {assetName}.");
        GitHubAssetResponse checksumAsset = release.Assets.FirstOrDefault(asset =>
            string.Equals(asset.Name, "SHA256SUMS.txt", StringComparison.Ordinal))
            ?? throw new InvalidDataException("The latest release does not contain its SHA-256 manifest.");

        string manifest = await DownloadTextAsync(checksumAsset.BrowserDownloadUrl, cancellationToken);
        string digest = ParseSha256(manifest, assetName);
        return new AppUpdateRelease(
            VersionService.NormalizeReleaseVersion(release.TagName),
            assetName,
            CreateHttpsUri(appAsset.BrowserDownloadUrl),
            digest);
    }

    public async Task<StagedAppUpdate> DownloadAndVerifyAsync(
        AppUpdateRelease release,
        CancellationToken cancellationToken = default,
        IProgress<AppUpdateProgress>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(release);
        string expectedAssetName = GetAssetName($"v{release.Version}", _platform, _architecture);
        if (!string.Equals(release.AssetName, expectedAssetName, StringComparison.Ordinal))
            throw new InvalidDataException("The selected app update asset does not match this platform.");
        if (release.Sha256.Length != 64 || !release.Sha256.All(Uri.IsHexDigit))
            throw new InvalidDataException("The selected app update has an invalid SHA-256 digest.");

        string workingDirectory = Directory.CreateTempSubdirectory("ChromiumBuildUpdater-update-").FullName;
        string packagePath = Path.Combine(workingDirectory, release.AssetName);
        string executablePath = Path.Combine(workingDirectory, "ChromiumBuildUpdater");
        try
        {
            using (HttpResponseMessage response = await _http.GetAsync(
                       CreateHttpsUri(release.DownloadUri.AbsoluteUri),
                       HttpCompletionOption.ResponseHeadersRead, cancellationToken))
            {
                response.EnsureSuccessStatusCode();
                await using Stream download = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var package = new FileStream(
                    packagePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
                long? totalBytes = response.Content.Headers.ContentLength;
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                long bytesTransferred = 0;
                var buffer = new byte[81920];
                ReportDownloadProgress(progress, bytesTransferred, totalBytes);
                int bytesRead;
                while ((bytesRead = await download.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await package.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                    bytesTransferred += bytesRead;
                    if (stopwatch.Elapsed >= TimeSpan.FromMilliseconds(500) ||
                        (totalBytes.HasValue && bytesTransferred >= totalBytes.Value))
                    {
                        ReportDownloadProgress(progress, bytesTransferred, totalBytes);
                        stopwatch.Restart();
                    }
                }

                ReportDownloadProgress(progress, bytesTransferred, totalBytes);
            }

            progress?.Report(new AppUpdateProgress(AppUpdateProgressStage.Verifying));
            await using (var package = File.OpenRead(packagePath))
            {
                byte[] actualDigest = await SHA256.HashDataAsync(package, cancellationToken);
                if (!CryptographicOperations.FixedTimeEquals(
                        actualDigest, Convert.FromHexString(release.Sha256)))
                {
                    throw new InvalidDataException("The downloaded app update failed SHA-256 verification.");
                }
            }

            progress?.Report(new AppUpdateProgress(AppUpdateProgressStage.Preparing));
            if (release.AssetName.EndsWith(".tar.gz", StringComparison.Ordinal))
                await ExtractUnixExecutableAsync(packagePath, executablePath, cancellationToken);
            else
                File.Copy(packagePath, executablePath);

            if (!OperatingSystem.IsWindows())
            {
                UnixFileMode currentMode = File.GetUnixFileMode(executablePath);
                File.SetUnixFileMode(executablePath, currentMode | UnixFileMode.UserExecute |
                    UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
            }

            File.Delete(packagePath);
            return new StagedAppUpdate(executablePath, workingDirectory);
        }
        catch
        {
            if (Directory.Exists(workingDirectory))
                Directory.Delete(workingDirectory, recursive: true);
            throw;
        }
    }

    private static void ReportDownloadProgress(
        IProgress<AppUpdateProgress>? progress,
        long bytesTransferred,
        long? totalBytes) =>
        progress?.Report(new AppUpdateProgress(
            AppUpdateProgressStage.Downloading, bytesTransferred, totalBytes));

    public static string GetAssetName(string tag, HostPlatform platform, Architecture architecture)
    {
        if (!ReleaseTagPattern.IsMatch(tag))
            throw new InvalidDataException($"The app release tag '{tag}' is not a stable numeric version.");

        string rid = (platform, architecture) switch
        {
            (HostPlatform.Windows, Architecture.X64) => "win-x64",
            (HostPlatform.Linux, Architecture.X64) => "linux-x64",
            (HostPlatform.Linux, Architecture.Arm64) => "linux-arm64",
            (HostPlatform.MacOS, Architecture.X64) => "osx-x64",
            (HostPlatform.MacOS, Architecture.Arm64) => "osx-arm64",
            _ => throw new PlatformNotSupportedException(
                $"App updates are not available for {platform} {architecture}.")
        };
        string extension = platform == HostPlatform.Windows ? ".exe" : ".tar.gz";
        return $"ChromiumBuildUpdater-{tag}-{rid}{extension}";
    }

    public static string ParseSha256(string manifest, string assetName)
    {
        foreach (string line in manifest.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] fields = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 2 ||
                !string.Equals(fields[^1].TrimStart('*'), assetName, StringComparison.Ordinal))
            {
                continue;
            }

            string digest = fields[0];
            if (digest.Length == 64 && digest.All(Uri.IsHexDigit))
                return digest;
            throw new InvalidDataException($"The SHA-256 manifest entry for {assetName} is invalid.");
        }

        throw new InvalidDataException($"The SHA-256 manifest has no entry for {assetName}.");
    }

    private async Task<string> DownloadTextAsync(string url, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _http.GetAsync(
            CreateHttpsUri(url), cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private static async Task ExtractUnixExecutableAsync(
        string archivePath,
        string executablePath,
        CancellationToken cancellationToken)
    {
        await using FileStream archive = File.OpenRead(archivePath);
        await using var gzip = new GZipStream(archive, CompressionMode.Decompress);
        using var reader = new TarReader(gzip);
        bool found = false;
        TarEntry? entry;
        while ((entry = reader.GetNextEntry(copyData: false)) is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!string.Equals(entry.Name, "ChromiumBuildUpdater", StringComparison.Ordinal))
                continue;
            if (found || entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile) ||
                entry.DataStream is null)
            {
                throw new InvalidDataException("The app update archive contains an invalid executable entry.");
            }

            await using var output = new FileStream(
                executablePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            await entry.DataStream.CopyToAsync(output, cancellationToken);
            found = true;
        }

        if (!found)
            throw new InvalidDataException("The app update archive does not contain its expected executable.");
    }

    private static Uri CreateHttpsUri(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new InvalidDataException("The app update release contains an invalid GitHub HTTPS download URL.");
        }

        return uri;
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ChromiumBuildUpdater");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }
}
