using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using HibbikiUpdater.Models;

namespace HibbikiUpdater.Services;

/// <summary>
/// Downloads the selected build's installer with live progress reporting,
/// verifies its SHA-256 checksum, and executes the silent update.
/// </summary>
public class InstallerService
{
    private static readonly HttpClient DownloadHttp = new()
    {
        Timeout = TimeSpan.FromMinutes(15),
        DefaultRequestHeaders =
        {
            { "User-Agent", "HibbikiChromiumUpdater/2.0" }
        }
    };

    public async Task DownloadVerifyAndInstallAsync(
        ReleaseArtifact artifact,
        IProgress<(int Percent, string StatusText)> progress,
        CancellationToken ct = default)
    {
        Uri downloadUri = ValidateArtifact(artifact);
        string tempInstallerPath = Path.Combine(
            Path.GetTempPath(),
            $"hibbiki-updater-{Guid.NewGuid():N}.exe");

        try
        {
            progress.Report((0, $"Downloading {artifact.Flavor} {artifact.CleanVersion}..."));

            using (var response = await DownloadHttp.GetAsync(downloadUri, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                response.EnsureSuccessStatusCode();
                if (response.RequestMessage?.RequestUri is Uri finalUri &&
                    finalUri.Scheme != Uri.UriSchemeHttps)
                    throw new InvalidOperationException("The installer download redirected to a non-HTTPS URL.");

                long totalBytes = response.Content.Headers.ContentLength ?? -1L;

                await using var contentStream = await response.Content.ReadAsStreamAsync(ct);
                await using var fileStream = new FileStream(
                    tempInstallerPath,
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
                        double mbRead = totalRead / 1024d / 1024d;
                        double mbTotal = totalBytes / 1024d / 1024d;
                        progress.Report((percent, $"Downloading: {mbRead:F1} / {mbTotal:F1} MB ({percent}%)"));
                    }
                }
            }

            // Extra Feature #7: Automatic SHA-256 Checksum Verification
            progress.Report((100, "Verifying SHA-256 checksum integrity..."));
            await using var verifyStream = File.OpenRead(tempInstallerPath);
            byte[] hashBytes = await SHA256.HashDataAsync(verifyStream, ct);
            string actualSha256 = Convert.ToHexString(hashBytes);
            string expectedSha256 = ChecksumService.NormalizeSha256(artifact.ExpectedSha256)!;

            if (!actualSha256.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"SHA-256 checksum mismatch. Expected {expectedSha256}, got {actualSha256}. Installation aborted.");
            }

            // Execute silent installer
            progress.Report((100, $"Running silent {artifact.Flavor} installer..."));
            var psi = new ProcessStartInfo
            {
                FileName = tempInstallerPath,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var installerProcess = Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start installer process.");

            await installerProcess.WaitForExitAsync(ct);

            if (installerProcess.ExitCode != 0)
            {
                throw new InvalidOperationException($"Installer exited with non-zero code: {installerProcess.ExitCode}");
            }

            progress.Report((100, $"Successfully updated {artifact.Flavor} to {artifact.CleanVersion}!"));
        }
        finally
        {
            if (File.Exists(tempInstallerPath))
            {
                try { File.Delete(tempInstallerPath); } catch { }
            }
        }
    }

    internal static Uri ValidateArtifact(ReleaseArtifact artifact)
    {
        if (!Uri.TryCreate(artifact.DownloadUrl, UriKind.Absolute, out Uri? uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            string.IsNullOrEmpty(uri.Host) ||
            !string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new InvalidOperationException("The installer download URL must be a valid HTTPS URL.");
        }

        string? sha256 = artifact.ExpectedSha256;
        if (string.IsNullOrWhiteSpace(sha256))
            throw new InvalidOperationException("The release does not provide a SHA-256 checksum; installation was blocked.");

        if (!ChecksumService.IsValidSha256(sha256))
            throw new InvalidOperationException("The release contains an invalid SHA-256 checksum; installation was blocked.");

        return uri;
    }
}
