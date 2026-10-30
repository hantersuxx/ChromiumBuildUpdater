using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32;

namespace ChromiumBuildUpdater.Core.Services;

public sealed class PlatformStartupService
{
    private const string WindowsRunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "ChromiumBuildUpdater";

    public void SyncStartupState(bool enabled)
    {
        string executablePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("The application executable path could not be determined.");

        if (OperatingSystem.IsWindows())
        {
            SyncWindowsStartup(enabled, executablePath);
            return;
        }

        if (OperatingSystem.IsMacOS())
        {
            SyncFileStartup(
                GetMacLaunchAgentPath(),
                enabled ? CreateMacLaunchAgent(executablePath) : null);
            return;
        }

        if (OperatingSystem.IsLinux())
        {
            SyncFileStartup(
                GetLinuxAutostartPath(),
                enabled ? CreateLinuxAutostartEntry(executablePath) : null);
            return;
        }

        throw new PlatformNotSupportedException("Startup registration is not supported on this OS.");
    }

    internal static string CreateMacLaunchAgent(string executablePath)
    {
        string escapedPath = System.Security.SecurityElement.Escape(executablePath)
            ?? throw new InvalidOperationException("The application path could not be escaped for the launch agent.");
        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
            <plist version="1.0">
            <dict>
              <key>Label</key>
              <string>org.chromiumbuildupdater.app</string>
              <key>ProgramArguments</key>
              <array><string>{escapedPath}</string></array>
              <key>RunAtLoad</key>
              <true/>
            </dict>
            </plist>
            """;
    }

    internal static string CreateLinuxAutostartEntry(string executablePath)
    {
        string escapedPath = executablePath
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("%", "%%", StringComparison.Ordinal);
        return $"""
            [Desktop Entry]
            Type=Application
            Name=Chromium Build Updater
            Comment=Check for Chromium build updates
            Exec="{escapedPath}"
            Terminal=false
            X-GNOME-Autostart-enabled=true
            """;
    }

    internal static string GetMacLaunchAgentPath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library",
            "LaunchAgents",
            "org.chromiumbuildupdater.app.plist");

    internal static string GetLinuxAutostartPath() =>
        Path.Combine(
            Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } configuredRoot &&
            Path.IsPathRooted(configuredRoot)
                ? configuredRoot
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config"),
            "autostart",
            "chromium-build-updater.desktop");

    private static void SyncFileStartup(string path, string? content)
    {
        if (content is null)
        {
            if (File.Exists(path))
                File.Delete(path);
            return;
        }

        string directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("The startup entry directory could not be determined.");
        Directory.CreateDirectory(directory);
        string temporaryPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporaryPath, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    [SupportedOSPlatform("windows")]
    private static void SyncWindowsStartup(bool enabled, string executablePath)
    {
        using RegistryKey runKey = Registry.CurrentUser.CreateSubKey(WindowsRunKey, writable: true)
            ?? throw new InvalidOperationException("The current-user startup registry key could not be opened.");

        if (enabled)
            runKey.SetValue(AppName, $"\"{executablePath}\"");
        else
            runKey.DeleteValue(AppName, throwOnMissingValue: false);

        runKey.DeleteValue("HibbikiUpdater", throwOnMissingValue: false);
    }
}
