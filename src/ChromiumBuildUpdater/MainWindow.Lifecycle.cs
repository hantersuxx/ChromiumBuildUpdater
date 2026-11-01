using Avalonia.Controls;

namespace ChromiumBuildUpdater;

public partial class MainWindow
{
    private void InitializeLifecycle()
    {
        try
        {
            _startupService.SyncStartupState(_configService.Current.StartWithWindows);
        }
        catch (Exception ex) when (
            ex is InvalidOperationException or IOException or UnauthorizedAccessException or
            System.ComponentModel.Win32Exception or PlatformNotSupportedException)
        {
            StatusText.Text = $"Startup setting could not be applied: {ex.Message}";
        }

        _schedulerTimer.Tick += async (_, _) =>
            await RunUpdateCycleAsync(allowAutomaticInstall: true);
        _deferredInstallTimer.Interval = TimeSpan.FromSeconds(30);
        _deferredInstallTimer.Tick += async (_, _) => await TryInstallDeferredUpdateAsync();
        Opened += OnOpened;
        Closing += OnClosing;
        Closed += OnClosed;
        ResetScheduler();
    }

    private async void OnOpened(object? sender, EventArgs args)
    {
        if (_didInitialCheck)
            return;

        _didInitialCheck = true;
        await RunUpdateCycleAsync(allowAutomaticInstall: true);
        if (App.StartupUpdateError is null)
        {
            await CheckForAppUpdateAsync();
        }
        else
        {
            AppUpdateStatusText.Text = $"The previous app update did not finish: {App.StartupUpdateError}";
        }
    }

    private void OnClosing(object? sender, WindowClosingEventArgs args)
    {
        if (_allowClose || !_minimizeToTrayOnClose)
            return;

        args.Cancel = true;
        Hide();
    }

    private void OnClosed(object? sender, EventArgs args)
    {
        _schedulerTimer.Stop();
        _deferredInstallTimer.Stop();
        _lifetime.Cancel();
        _lifetime.Dispose();
        DisposeTrayIcon();
    }
}
