using ChromiumBuildUpdater.Core.Services;

namespace ChromiumBuildUpdater.Tests;

public class PlatformStartupServiceTests
{
    [Fact]
    public void CreateMacLaunchAgent_EscapesApplicationPath()
    {
        string plist = PlatformStartupService.CreateMacLaunchAgent(
            "/Applications/Chromium & Browser/ChromiumBuildUpdater");

        Assert.Contains("Chromium &amp; Browser", plist);
        Assert.Contains("org.chromiumbuildupdater.app", plist);
        Assert.Contains("<true/>", plist);
    }

    [Fact]
    public void CreateLinuxAutostartEntry_EscapesDesktopFieldCodesAndQuotes()
    {
        string entry = PlatformStartupService.CreateLinuxAutostartEntry(
            "/home/alice/Apps/Updater 100% \"beta\"");

        Assert.Contains("Exec=\"/home/alice/Apps/Updater 100%% \\\"beta\\\"\"", entry);
        Assert.Contains("X-GNOME-Autostart-enabled=true", entry);
    }
}
