namespace ChromiumBuildUpdater.Core.Models;

/// <summary>
/// Represents user preferences and cached telemetry saved in the platform's application-data directory.
/// </summary>
public class AppConfig
{
    public AppTheme Theme { get; set; } = AppTheme.System;

    // Core user preferences (Items 1-4)
    public bool AutoUpdate { get; set; } = true;
    public bool EnableNotifications { get; set; } = true;
    public bool MinimizeToTrayOnClose { get; set; } = true;
    public bool StartWithWindows { get; set; } = true;

    // Extra Feature #7: Active Process Guard
    public bool DeferIfChromiumRunning { get; set; } = true;

    // Extra Feature #7: Dual-Source Preference (Allow forcing chromiumbuilds.org JSON endpoint)
    public bool PreferChromiumBuildsOrgApi { get; set; } = false;

    // Build Variant Dropdown (Hibbiki, UngoogledChromium, Thorium)
    public ChromiumFlavor SelectedFlavor { get; set; } = ChromiumFlavor.Hibbiki;

    // Scheduler Interval
    public int CheckIntervalHours { get; set; } = 6;

    // Cached Telemetry (Item 6)
    public string LastKnownRemoteVersion { get; set; } = "Unknown";
    public string LastKnownRemoteDate { get; set; } = "Unknown";
    public string LastKnownSource { get; set; } = "Not checked yet";
    public string LastCheckTime { get; set; } = "Never";
}

public enum AppTheme
{
    System,
    Light,
    Dark
}
