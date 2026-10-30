using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using HibbikiUpdater.Models;

namespace HibbikiUpdater.Services;

/// <summary>
/// Inspects the local machine to find the installed Chromium/Thorium binary, its FileVersionInfo version,
/// and whether the browser is currently running (Active Process Guard).
/// </summary>
public class LocalChromiumService
{
    private static readonly string[] ChromiumCandidatePaths =
    {
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Chromium\Application\chrome.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Chromium\Application\chrome.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Chromium\Application\chrome.exe")
    };

    private static readonly string[] ThoriumCandidatePaths =
    {
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Thorium", "Application", "thorium.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Thorium", "Application", "thorium.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Thorium", "Application", "thorium.exe")
    };

    public string? FindExecutablePath(ChromiumFlavor flavor)
    {
        var paths = GetCandidatePaths(flavor);
        return paths.FirstOrDefault(File.Exists);
    }

    internal static string[] GetCandidatePaths(ChromiumFlavor flavor) =>
        flavor == ChromiumFlavor.Thorium ? ThoriumCandidatePaths : ChromiumCandidatePaths;

    public string GetInstalledVersion(ChromiumFlavor flavor)
    {
        string? exePath = FindExecutablePath(flavor);
        if (exePath == null)
            return "Not Installed";

        try
        {
            var versionInfo = FileVersionInfo.GetVersionInfo(exePath);
            return versionInfo.ProductVersion ?? "Unknown";
        }
        catch
        {
            return "Unknown";
        }
    }

    /// <summary>
    /// Active Process Guard: Checks if the target browser binary is currently running in memory.
    /// </summary>
    public bool IsBrowserRunning(ChromiumFlavor flavor)
    {
        string? exePath = FindExecutablePath(flavor);
        string processName = flavor == ChromiumFlavor.Thorium ? "thorium" : "chrome";

        try
        {
            var processes = Process.GetProcessesByName(processName);
            if (processes.Length == 0)
                return false;

            if (exePath == null)
                return true;

            foreach (var proc in processes)
            {
                try
                {
                    if (proc.MainModule?.FileName != null &&
                        proc.MainModule.FileName.Equals(exePath, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                catch
                {
                    // If MainModule is inaccessible due to permissions, assume running if process name matches
                    return true;
                }
            }
        }
        catch
        {
            // Avoid installing while process state is unknown.
            return true;
        }

        return false;
    }

    public bool IsRemoteNewer(string localVersionStr, string remoteVersionStr)
        => VersionService.IsRemoteNewer(localVersionStr, remoteVersionStr);
}
