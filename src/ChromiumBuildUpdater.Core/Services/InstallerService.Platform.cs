using System.Diagnostics;
using ChromiumBuildUpdater.Core.Models;

namespace ChromiumBuildUpdater.Core.Services;

internal sealed class NativeArtifactInstallStrategy : IArtifactInstallStrategy
{
    public async Task InstallAsync(
        ReleaseArtifact artifact,
        string artifactPath,
        IProgress<(int Percent, string StatusText)> progress,
        HostPlatform platform,
        CancellationToken cancellationToken)
    {
        switch (platform)
        {
            case HostPlatform.Windows:
                progress.Report((100, $"Running {artifact.Flavor} installer..."));
                await RunProcessAsync(artifactPath, [], createNoWindow: true, cancellationToken);
                break;
            case HostPlatform.Linux:
                progress.Report((100, "Installing the verified Thorium AppImage for this user..."));
                InstallLinuxAppImage(artifactPath, cancellationToken);
                break;
            case HostPlatform.MacOS:
                progress.Report((100, "Verifying and installing the signed macOS application..."));
                await InstallMacApplicationAsync(artifact, artifactPath, cancellationToken);
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
