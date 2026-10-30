using System.Windows.Forms;
using Microsoft.Win32;

namespace HibbikiUpdater.Services;

/// <summary>
/// Manages the Windows startup entry in HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run.
/// </summary>
public class RegistryStartupService
{
    private const string RunRegistryPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AppRegistryName = "HibbikiChromiumUpdater";

    public void SyncStartupState(bool enableStartup)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunRegistryPath, writable: true);
            if (key == null) return;

            if (enableStartup)
            {
                // Pass --minimized so it boots straight into the system tray without popping up a window
                string launchCommand = $"\"{Application.ExecutablePath}\" --minimized";
                key.SetValue(AppRegistryName, launchCommand);
            }
            else
            {
                key.DeleteValue(AppRegistryName, throwOnMissingValue: false);
            }
        }
        catch
        {
            // Ignore registry permission errors
        }
    }
}
