using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using ChromiumBuildUpdater.Core.Models;
using ChromiumBuildUpdater.Core.Services;

namespace ChromiumBuildUpdater;

public partial class MainWindow
{
    private const string PayPalSupportUrl = "https://paypal.me/AliaksandrJoya";

    private TrayIcon? _trayIcon;

    private void OpenReleasePage()
    {
        string url = _configService.Current.SelectedFlavor switch
        {
            ChromiumFlavor.Hibbiki => "https://github.com/Hibbiki/chromium-win64/releases/latest",
            ChromiumFlavor.UngoogledChromium when PlatformSupport.Current == HostPlatform.MacOS =>
                "https://github.com/ungoogled-software/ungoogled-chromium-macos/releases/latest",
            ChromiumFlavor.UngoogledChromium =>
                "https://github.com/ungoogled-software/ungoogled-chromium-windows/releases/latest",
            ChromiumFlavor.Thorium => "https://github.com/gz83/thorium/releases/latest",
            _ => throw new ArgumentOutOfRangeException()
        };
        LaunchUrl(url);
    }

    private void OpenSupportPage() => LaunchUrl(PayPalSupportUrl);

    private void ConfigureTrayIcon()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using Stream iconStream = AssetLoader.Open(new Uri("avares://ChromiumBuildUpdater/Assets/app.ico"));
        _trayIcon = new TrayIcon
        {
            Icon = new WindowIcon(iconStream),
            ToolTipText = "Chromium Build Updater",
            Menu = CreateTrayMenu()
        };
        _trayIcon.Clicked += (_, _) => RestoreFromTray();
        TrayIcon.SetIcons(Application.Current!, new TrayIcons { _trayIcon });
    }

    private NativeMenu CreateTrayMenu()
    {
        var menu = new NativeMenu();
        var openItem = new NativeMenuItem("Open dashboard");
        openItem.Click += (_, _) => RestoreFromTray();
        var checkItem = new NativeMenuItem("Check for updates");
        checkItem.Click += async (_, _) => await RunUpdateCycleAsync(allowAutomaticInstall: false);
        var exitItem = new NativeMenuItem("Exit");
        exitItem.Click += (_, _) => ExitApplication();
        menu.Add(openItem);
        menu.Add(checkItem);
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(exitItem);
        return menu;
    }

    private void RestoreFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    public void ActivateFromSecondaryInstance() =>
        Dispatcher.UIThread.Post(RestoreFromTray);

    private void ExitApplication()
    {
        _allowClose = true;
        Close();
    }

    private void DisposeTrayIcon()
    {
        if (_trayIcon is null)
            return;

        TrayIcon.SetIcons(Application.Current!, new TrayIcons());
        _trayIcon.Dispose();
        _trayIcon = null;
    }

    private void LaunchUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex) when (
            ex is InvalidOperationException or System.ComponentModel.Win32Exception or
            PlatformNotSupportedException)
        {
            StatusText.Text = $"Could not open the link: {ex.Message}";
        }
    }
}
