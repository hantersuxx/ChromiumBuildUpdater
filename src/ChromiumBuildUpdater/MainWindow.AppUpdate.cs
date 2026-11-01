using ChromiumBuildUpdater.Core.Services;

namespace ChromiumBuildUpdater;

public partial class MainWindow
{
    private readonly AppUpdateService _appUpdateService = new();
    private AppUpdateRelease? _availableAppUpdate;

    private void HookAppUpdateEvents()
    {
        CheckAppUpdatesButton.Click += async (_, _) => await CheckForAppUpdateAsync();
        InstallAppUpdateButton.Click += async (_, _) => await InstallAppUpdateAsync();
    }

    private async Task CheckForAppUpdateAsync()
    {
        CheckAppUpdatesButton.IsEnabled = false;
        AppUpdateProgressBar.IsVisible = false;
        AppUpdateStatusText.Text = "Checking GitHub for a newer app version…";
        try
        {
            _availableAppUpdate = await _appUpdateService.CheckForUpdateAsync(_lifetime.Token);
            if (_availableAppUpdate is null)
            {
                AppUpdateStatusText.Text = "You’re using the latest version of Chromium Build Updater.";
                InstallAppUpdateButton.IsVisible = false;
                return;
            }

            AppUpdateStatusText.Text = $"Version {_availableAppUpdate.Version} is available.";
            InstallAppUpdateButton.IsVisible = true;
            InstallAppUpdateButton.IsEnabled = true;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex) when (
            ex is HttpRequestException or IOException or InvalidDataException or System.Text.Json.JsonException or
            TaskCanceledException)
        {
            AppUpdateStatusText.Text = $"Could not check for app updates: {ex.Message}";
        }
        finally
        {
            CheckAppUpdatesButton.IsEnabled = true;
        }
    }

    private async Task InstallAppUpdateAsync()
    {
        if (_availableAppUpdate is null)
            return;

        string? executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            AppUpdateStatusText.Text = "The running application executable could not be located.";
            return;
        }

        InstallAppUpdateButton.IsEnabled = false;
        CheckAppUpdatesButton.IsEnabled = false;
        AppUpdateStatusText.Text = $"Downloading and verifying version {_availableAppUpdate.Version}…";
        StagedAppUpdate? stagedUpdate = null;
        try
        {
            var progress = new Progress<AppUpdateProgress>(OnAppUpdateProgress);
            stagedUpdate = await _appUpdateService.DownloadAndVerifyAsync(
                _availableAppUpdate, _lifetime.Token, progress);
            SelfUpdateLauncher.Start(stagedUpdate, executablePath, Environment.ProcessId);
            AppUpdateStatusText.Text = "Update verified. Restarting to finish installation…";
            _allowClose = true;
            Close();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex) when (
            ex is HttpRequestException or IOException or InvalidDataException or UnauthorizedAccessException or
            System.ComponentModel.Win32Exception or PlatformNotSupportedException or TaskCanceledException or
            InvalidOperationException)
        {
            AppUpdateProgressBar.IsVisible = false;
            if (stagedUpdate is not null && Directory.Exists(stagedUpdate.WorkingDirectory))
            {
                try
                {
                    Directory.Delete(stagedUpdate.WorkingDirectory, recursive: true);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }

            AppUpdateStatusText.Text = $"App update could not be installed: {ex.Message}";
            InstallAppUpdateButton.IsEnabled = true;
            CheckAppUpdatesButton.IsEnabled = true;
        }
    }

    private void OnAppUpdateProgress(AppUpdateProgress progress)
    {
        AppUpdateProgressBar.IsVisible = true;
        switch (progress.Stage)
        {
            case AppUpdateProgressStage.Downloading when progress.TotalBytes is > 0:
                double percentage = Math.Clamp(
                    progress.BytesTransferred * 100d / progress.TotalBytes.Value, 0d, 100d);
                AppUpdateProgressBar.IsIndeterminate = false;
                AppUpdateProgressBar.Value = percentage;
                AppUpdateStatusText.Text =
                    $"Downloading version {_availableAppUpdate?.Version}: " +
                    $"{FormatMegabytes(progress.BytesTransferred)} of " +
                    $"{FormatMegabytes(progress.TotalBytes.Value)} ({percentage:F0}%).";
                break;
            case AppUpdateProgressStage.Downloading:
                AppUpdateProgressBar.IsIndeterminate = true;
                AppUpdateStatusText.Text =
                    $"Downloading version {_availableAppUpdate?.Version}: " +
                    $"{FormatMegabytes(progress.BytesTransferred)} received.";
                break;
            case AppUpdateProgressStage.Verifying:
                AppUpdateProgressBar.IsIndeterminate = true;
                AppUpdateStatusText.Text = "Download complete. Verifying the update…";
                break;
            case AppUpdateProgressStage.Preparing:
                AppUpdateProgressBar.IsIndeterminate = true;
                AppUpdateStatusText.Text = "Update verified. Preparing the application…";
                break;
        }
    }

    private static string FormatMegabytes(long bytes) =>
        $"{bytes / (1024d * 1024d):F1} MiB";
}
