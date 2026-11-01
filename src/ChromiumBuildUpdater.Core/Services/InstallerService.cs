using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using ChromiumBuildUpdater.Core.Models;

namespace ChromiumBuildUpdater.Core.Services;

internal interface IArtifactInstallStrategy
{
    Task InstallAsync(
        ReleaseArtifact artifact,
        string artifactPath,
        IProgress<(int Percent, string StatusText)> progress,
        HostPlatform platform,
        CancellationToken cancellationToken);
}

public class InstallerService
{
    private readonly HttpClient _downloadHttp;
    private readonly HostPlatform _platform;
    private readonly IArtifactInstallStrategy _installStrategy;

    public InstallerService(HttpClient? downloadHttp = null)
        : this(downloadHttp, PlatformSupport.Current)
    {
    }

    internal InstallerService(HttpClient? downloadHttp, HostPlatform platform)
        : this(downloadHttp, platform, new NativeArtifactInstallStrategy())
    {
    }

    internal InstallerService(
        HttpClient? downloadHttp,
        HostPlatform platform,
        IArtifactInstallStrategy installStrategy)
    {
        _platform = platform;
        _installStrategy = installStrategy ?? throw new ArgumentNullException(nameof(installStrategy));
        _downloadHttp = downloadHttp ?? new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(15),
            DefaultRequestHeaders =
            {
                { "User-Agent", "ChromiumBuildUpdater/3.0" }
            }
        };
    }

    public async Task DownloadVerifyAndInstallAsync(
        ReleaseArtifact artifact,
        IProgress<(int Percent, string StatusText)> progress,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        ArgumentNullException.ThrowIfNull(progress);

        Uri downloadUri = ValidateArtifact(artifact, _platform);
        string extension = Path.GetExtension(downloadUri.AbsolutePath);
        string temporaryArtifactPath = Path.Combine(
            Path.GetTempPath(),
            $"chromium-build-updater-{Guid.NewGuid():N}{extension}");

        try
        {
            progress.Report((0, $"Downloading {artifact.Flavor} {artifact.CleanVersion}..."));

            using (var response = await _downloadHttp.GetAsync(
                       downloadUri,
                       HttpCompletionOption.ResponseHeadersRead,
                       ct))
            {
                response.EnsureSuccessStatusCode();
                if (response.RequestMessage?.RequestUri is Uri finalUri &&
                    finalUri.Scheme != Uri.UriSchemeHttps)
                {
                    throw new InvalidOperationException(
                        "The browser download redirected to a non-HTTPS URL.");
                }

                long totalBytes = response.Content.Headers.ContentLength ?? -1L;
                await using var contentStream = await response.Content.ReadAsStreamAsync(ct);
                await using var fileStream = new FileStream(
                    temporaryArtifactPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 81920,
                    useAsync: true);

                byte[] buffer = new byte[81920];
                long totalRead = 0;
                int bytesRead;
                while ((bytesRead = await contentStream.ReadAsync(buffer, ct)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                    totalRead += bytesRead;

                    if (totalBytes > 0)
                    {
                        int percent = (int)((double)totalRead / totalBytes * 100);
                        progress.Report((percent, $"Downloading: {percent}%"));
                    }
                }
            }

            progress.Report((100, "Verifying SHA-256 checksum..."));
            await using var verifyStream = File.OpenRead(temporaryArtifactPath);
            byte[] hashBytes = await SHA256.HashDataAsync(verifyStream, ct);
            string actualSha256 = Convert.ToHexString(hashBytes);
            string expectedSha256 = ChecksumService.NormalizeSha256(artifact.ExpectedSha256)!;
            if (!actualSha256.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"SHA-256 checksum mismatch. Expected {expectedSha256}, got {actualSha256}. Installation aborted.");
            }

            await _installStrategy.InstallAsync(
                artifact,
                temporaryArtifactPath,
                progress,
                _platform,
                ct);
            progress.Report((100, $"Successfully updated {artifact.Flavor} to {artifact.CleanVersion}."));
        }
        finally
        {
            DeleteTemporaryFile(temporaryArtifactPath);
        }
    }

    internal static Uri ValidateArtifact(ReleaseArtifact artifact) =>
        ValidateArtifact(artifact, PlatformSupport.Current);

    internal static Uri ValidateArtifact(ReleaseArtifact artifact, HostPlatform platform)
    {
        if (!Uri.TryCreate(artifact.DownloadUrl, UriKind.Absolute, out Uri? uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            string.IsNullOrEmpty(uri.Host) ||
            !string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new InvalidOperationException("The browser download URL must be a valid HTTPS URL.");
        }

        if (string.IsNullOrWhiteSpace(artifact.ExpectedSha256))
            throw new InvalidOperationException("The release does not provide a SHA-256 checksum; installation was blocked.");

        if (!ChecksumService.IsValidSha256(artifact.ExpectedSha256))
            throw new InvalidOperationException("The release contains an invalid SHA-256 checksum; installation was blocked.");

        string extension = Path.GetExtension(uri.AbsolutePath);
        bool supportedFormat = platform switch
        {
            HostPlatform.Windows =>
                extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) &&
                PlatformSupport.IsFlavorAvailable(artifact.Flavor, platform),
            HostPlatform.Linux =>
                extension.Equals(".AppImage", StringComparison.OrdinalIgnoreCase) &&
                artifact.Flavor == ChromiumFlavor.Thorium &&
                PlatformSupport.IsFlavorAvailable(artifact.Flavor, platform),
            HostPlatform.MacOS =>
                extension.Equals(".dmg", StringComparison.OrdinalIgnoreCase) &&
                artifact.Flavor == ChromiumFlavor.UngoogledChromium &&
                PlatformSupport.IsFlavorAvailable(artifact.Flavor, platform),
            _ => false
        };

        if (!supportedFormat)
            throw new PlatformNotSupportedException($"The {extension} asset is not an installable {artifact.Flavor} build on {platform}.");

        return uri;
    }

    private static void DeleteTemporaryFile(string path)
    {
        if (!File.Exists(path))
            return;

        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.TraceWarning($"Could not remove temporary file '{path}': {ex.Message}");
        }
    }
}
