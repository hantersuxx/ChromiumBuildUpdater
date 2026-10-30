using System.Diagnostics;
using System.IO;
using System.Linq;
using ChromiumBuildUpdater.Core.Models;

namespace ChromiumBuildUpdater.Core.Services;

public class LocalChromiumService : IInstalledBuildService
{
    public string? FindExecutablePath(ChromiumFlavor flavor)
    {
        return GetCandidatePaths(flavor).FirstOrDefault(File.Exists);
    }

    internal static string[] GetCandidatePaths(ChromiumFlavor flavor) =>
        GetCandidatePaths(flavor, PlatformSupport.Current, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

    internal static string[] GetCandidatePaths(ChromiumFlavor flavor, HostPlatform platform, string homeDirectory)
    {
        if (platform == HostPlatform.Windows)
        {
            string browserFolder = flavor == ChromiumFlavor.Thorium ? "Thorium" : "Chromium";
            return
            [
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    browserFolder,
                    "Application",
                    flavor == ChromiumFlavor.Thorium ? "thorium.exe" : "chrome.exe"),
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    browserFolder,
                    "Application",
                    flavor == ChromiumFlavor.Thorium ? "thorium.exe" : "chrome.exe"),
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                    browserFolder,
                    "Application",
                    flavor == ChromiumFlavor.Thorium ? "thorium.exe" : "chrome.exe")
            ];
        }

        if (platform == HostPlatform.MacOS && flavor == ChromiumFlavor.UngoogledChromium)
        {
            return
            [
                Path.Combine(
                    homeDirectory,
                    "Applications",
                    "Ungoogled Chromium.app",
                    "Contents",
                    "MacOS",
                    "Chromium")
            ];
        }

        if (platform == HostPlatform.Linux && flavor == ChromiumFlavor.Thorium)
        {
            string dataRoot = Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } configuredRoot &&
                              Path.IsPathRooted(configuredRoot)
                ? configuredRoot
                : Path.Combine(homeDirectory, ".local", "share");
            return [Path.Combine(dataRoot, "ChromiumBuildUpdater", "Thorium.AppImage")];
        }

        return [];
    }

    public string GetInstalledVersion(ChromiumFlavor flavor)
    {
        string? executablePath = FindExecutablePath(flavor);
        if (executablePath is null)
            return "Not Installed";

        if (PlatformSupport.Current == HostPlatform.Windows)
        {
            try
            {
                return FileVersionInfo.GetVersionInfo(executablePath).ProductVersion ?? "Unknown";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                return "Unknown";
            }
        }

        return TryGetCommandVersion(executablePath);
    }

    public bool IsBrowserRunning(ChromiumFlavor flavor)
    {
        string? executablePath = FindExecutablePath(flavor);
        string[] processNames = flavor == ChromiumFlavor.Thorium
            ? ["thorium", "Thorium"]
            : ["chrome", "chromium", "Chromium"];

        try
        {
            foreach (string processName in processNames)
            {
                foreach (Process process in Process.GetProcessesByName(processName))
                {
                    using (process)
                    {
                        try
                        {
                            string? runningPath = process.MainModule?.FileName;
                            if (runningPath is null || executablePath is null ||
                                PathEquals(runningPath, executablePath))
                            {
                                return true;
                            }
                        }
                        catch (Exception ex) when (
                            ex is InvalidOperationException or System.ComponentModel.Win32Exception or
                            NotSupportedException)
                        {
                            return true;
                        }
                    }
                }
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return true;
        }

        return false;
    }

    public bool IsRemoteNewer(string localVersionStr, string remoteVersionStr) =>
        VersionService.IsRemoteNewer(localVersionStr, remoteVersionStr);

    private static bool PathEquals(string first, string second) =>
        string.Equals(
            Path.GetFullPath(first).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(second).TrimEnd(Path.DirectorySeparatorChar),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static string TryGetCommandVersion(string executablePath)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = executablePath,
                ArgumentList = { "--version" },
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            });

            if (process is null)
                return "Unknown";

            if (!process.WaitForExit(5000))
            {
                process.Kill(entireProcessTree: true);
                return "Unknown";
            }

            string output = process.StandardOutput.ReadToEnd();
            if (string.IsNullOrWhiteSpace(output))
                output = process.StandardError.ReadToEnd();

            return output.Trim() is { Length: > 0 } version
                ? version
                : "Unknown";
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or InvalidOperationException or
            System.ComponentModel.Win32Exception)
        {
            return "Unknown";
        }
    }
}
