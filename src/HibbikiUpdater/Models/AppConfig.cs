namespace HibbikiUpdater.Models;

/// <summary>
/// Represents the user preferences and cached telemetry saved to %LocalAppData%\HibbikiUpdater\config.json.
/// </summary>
public class AppConfig
{
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

    // Optional support links. These can be overridden in config.json.
    public string? SupportUrl { get; set; } = "https://paypal.me/AliaksandrJoya";
    public string? EthereumAddress { get; set; } = "0xfdC3A6Dcd29a98d4c1FB0c8EF1Ab643809F5a791";
    public string? BitcoinAddress { get; set; } = "bc1q9d7qsevsnycdyxt2972n6482hked2m2wnprmlr";

    // Cached Telemetry (Item 6)
    public string LastKnownRemoteVersion { get; set; } = "Unknown";
    public string LastKnownRemoteDate { get; set; } = "Unknown";
    public string LastKnownSource { get; set; } = "Not checked yet";
    public string LastCheckTime { get; set; } = "Never";
}
