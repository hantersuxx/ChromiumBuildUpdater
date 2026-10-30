using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using ChromiumBuildUpdater.Core.Models;

namespace ChromiumBuildUpdater.Core.Services;

public class InstallerService
{
    private readonly HttpClient _downloadHttp;
    private readonly HostPlatform _platform;

    public InstallerService(HttpClient? downloadHttp = null)
        : this(downloadHttp, PlatformSupport.Current)
    {
    }

    internal InstallerService(HttpClient? downloadHttp, HostPlatform platform)
    {
        _platform = platform;
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

            await InstallArtifactAsync(artifact, temporaryArtifactPath, progress, _platform, ct);
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

    private static async Task InstallArtifactAsync(
        ReleaseArtifact artifact,
        string artifactPath,
        IProgress<(int Percent, string StatusText)> progress,
        HostPlatform platform,
        CancellationToken ct)
    {
        switch (platform)
        {
            case HostPlatform.Windows:
                progress.Report((100, $"Running {artifact.Flavor} installer..."));
                await RunProcessAsync(
                    artifactPath,
                    [],
                    createNoWindow: true,
                    ct);
                break;
            case HostPlatform.Linux:
                progress.Report((100, "Installing the verified Thorium AppImage for this user..."));
                InstallLinuxAppImage(artifactPath, ct);
                break;
            case HostPlatform.MacOS:
                progress.Report((100, "Verifying and installing the signed macOS application..."));
                await InstallMacApplicationAsync(artifact, artifactPath, ct);
                break;
            default:
                throw new PlatformNotSupportedException("Browser installation is not supported on this OS.");
        }
    }

    private static void InstallLinuxAppImage(string artifactPath, CancellationToken ct)
    {
        if (!OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException("AppImage installation is only available on Linux.");

        ct.ThrowIfCancellationRequested();
        string destination = LocalChromiumService.GetCandidatePaths(
            ChromiumFlavor.Thorium,
            HostPlatform.Linux,
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))[0];
        string directory = Path.GetDirectoryName(destination)
            ?? throw new InvalidOperationException("The Thorium install directory could not be determined.");
        Directory.CreateDirectory(directory);
        string stagedPath = Path.Combine(directory, $".Thorium-{Guid.NewGuid():N}.AppImage");
        try
        {
            File.Copy(artifactPath, stagedPath, overwrite: false);
            File.SetUnixFileMode(
                stagedPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            File.Move(stagedPath, destination, overwrite: true);
        }
        finally
        {
            DeleteTemporaryFile(stagedPath);
        }
    }

    private static async Task InstallMacApplicationAsync(
        ReleaseArtifact artifact,
        string diskImagePath,
        CancellationToken ct)
    {
        string mountPath = Path.Combine(Path.GetTempPath(), $"chromium-build-updater-mount-{Guid.NewGuid():N}");
        string applicationsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Applications");
        string finalAppPath = Path.Combine(
            applicationsPath,
            artifact.Flavor == ChromiumFlavor.UngoogledChromium
                ? "Ungoogled Chromium.app"
                : "Chromium.app");
        string stagedAppPath = Path.Combine(applicationsPath, $".ChromiumBuildUpdater-{Guid.NewGuid():N}.app");
        string backupAppPath = Path.Combine(applicationsPath, $".ChromiumBuildUpdater-backup-{Guid.NewGuid():N}.app");
        bool imageMounted = false;

        Directory.CreateDirectory(mountPath);
        Directory.CreateDirectory(applicationsPath);
        try
        {
            await RunProcessAsync(
                "/usr/bin/hdiutil",
                ["attach", "-nobrowse", "-readonly", "-mountpoint", mountPath, diskImagePath],
                createNoWindow: true,
                ct);
            imageMounted = true;

            string[] appBundles = Directory.GetDirectories(mountPath, "*.app", SearchOption.TopDirectoryOnly);
            if (appBundles.Length != 1)
                throw new InvalidOperationException("The downloaded disk image did not contain exactly one application bundle.");

            await RunProcessAsync(
                "/usr/bin/ditto",
                [appBundles[0], stagedAppPath],
                createNoWindow: true,
                ct);
            await RunProcessAsync(
                "/usr/bin/codesign",
                ["--verify", "--deep", "--strict", stagedAppPath],
                createNoWindow: true,
                ct);
            await RunProcessAsync(
                "/usr/sbin/spctl",
                ["--assess", "--type", "execute", stagedAppPath],
                createNoWindow: true,
                ct);

            bool hadPreviousInstall = Directory.Exists(finalAppPath);
            if (hadPreviousInstall)
                Directory.Move(finalAppPath, backupAppPath);

            try
            {
                Directory.Move(stagedAppPath, finalAppPath);
            }
            catch (Exception installError) when (
                installError is IOException or UnauthorizedAccessException)
            {
                if (hadPreviousInstall && !Directory.Exists(finalAppPath))
                {
                    try
                    {
                        Directory.Move(backupAppPath, finalAppPath);
                    }
                    catch (Exception restoreError) when (
                        restoreError is IOException or UnauthorizedAccessException)
                    {
                        throw new AggregateException(
                            "The update failed and the previous browser could not be restored.",
                            installError,
                            restoreError);
                    }
                }

                throw;
            }

            if (Directory.Exists(backupAppPath))
                Directory.Delete(backupAppPath, recursive: true);
        }
        finally
        {
            if (imageMounted)
            {
                try
                {
                    await RunProcessAsync(
                        "/usr/bin/hdiutil",
                        ["detach", mountPath, "-quiet"],
                        createNoWindow: true,
                        CancellationToken.None);
                }
                catch (Exception ex) when (
                    ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    Trace.TraceWarning($"Could not detach the Chromium update disk image: {ex.Message}");
                }
            }

            DeleteTemporaryDirectory(stagedAppPath);
            DeleteTemporaryDirectory(mountPath);
        }
    }

    private static async Task RunProcessAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        bool createNoWindow,
        CancellationToken ct)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            CreateNoWindow = createNoWindow,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (string argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start {Path.GetFileName(fileName)}.");
        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync(ct);
        Task<string> standardError = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        string output = await standardOutput;
        string error = await standardError;
        if (process.ExitCode != 0)
        {
            string details = string.IsNullOrWhiteSpace(error) ? output.Trim() : error.Trim();
            throw new InvalidOperationException(
                $"{Path.GetFileName(fileName)} exited with code {process.ExitCode}" +
                (details.Length > 0 ? $": {details}" : "."));
        }
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

    private static void DeleteTemporaryDirectory(string path)
    {
        if (!Directory.Exists(path))
            return;

        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.TraceWarning($"Could not remove temporary directory '{path}': {ex.Message}");
        }
    }
}
