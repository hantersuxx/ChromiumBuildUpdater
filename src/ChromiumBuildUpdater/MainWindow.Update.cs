using Avalonia.Controls;
using ChromiumBuildUpdater.Core.Models;
using ChromiumBuildUpdater.Core.Services;

namespace ChromiumBuildUpdater;

public partial class MainWindow
{
    private async Task RunUpdateCycleAsync(bool allowAutomaticInstall)
    {
        if (_isBusy || FlavorSelector.SelectedItem is not FlavorChoice choice)
            return;

        _isBusy = true;
        SetControlsEnabled(false);
        UpdateProgress.Value = 0;
        ProgressText.Text = "";
        StatusText.Text = $"Checking for {choice.DisplayName} updates...";

        try
        {
            UpdateCheckResult result = await _updateCheckService.CheckAsync(
                choice.Flavor,
                _lifetime.Token);
            ReleaseArtifact artifact = result.Artifact;
            _latestArtifact = artifact;
            LatestVersionText.Text = artifact.CleanVersion;
            ReleaseDateText.Text = result.ReleaseDateText;
            SourceText.Text = artifact.SourceName;
            UpdateLastCheckedText();

            InstalledVersionText.Text = result.InstalledVersion;
            InstallButton.IsEnabled = result.UpdateAvailable;
            InstallButton.Content = result.InstalledVersion == "Not Installed"
                ? "Install build"
                : "Install update";

            if (!result.UpdateAvailable)
            {
                StatusText.Text = AppendSettingsWarning("You are up to date.", result.SettingsError);
                return;
            }

            StatusText.Text = AppendSettingsWarning(
                $"Version {artifact.CleanVersion} is available.",
                result.SettingsError);
            if (allowAutomaticInstall && _configService.Current.AutoUpdate)
                await InstallOrDeferAsync(artifact);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (OperationCanceledException ex)
        {
            StatusText.Text = $"Update check timed out: {ex.Message}";
            InstallButton.IsEnabled = false;
        }
        catch (Exception ex) when (
            ex is HttpRequestException or InvalidOperationException or PlatformNotSupportedException or
            IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or
            System.Text.Json.JsonException)
        {
            StatusText.Text = $"Update check failed: {ex.Message}";
            InstallButton.IsEnabled = false;
        }
        finally
        {
            _isBusy = false;
            SetControlsEnabled(true);
            if (_latestArtifact is not null)
            {
                InstallButton.IsEnabled = _localChromiumService.IsRemoteNewer(
                    _localChromiumService.GetInstalledVersion(_configService.Current.SelectedFlavor),
                    _latestArtifact.CleanVersion);
            }
        }
    }

    private async Task InstallOrDeferAsync(ReleaseArtifact artifact)
    {
        if (_configService.Current.DeferIfChromiumRunning &&
            _localChromiumService.IsBrowserRunning(artifact.Flavor))
        {
            _pendingDeferredArtifact = artifact;
            _deferredInstallTimer.Start();
            StatusText.Text = "The browser is open. The update will install after you close it.";
            return;
        }

        await InstallArtifactAsync(artifact);
    }

    private async Task TryInstallDeferredUpdateAsync()
    {
        if (_isBusy || _pendingDeferredArtifact is not { } artifact)
            return;

        if (_localChromiumService.IsBrowserRunning(artifact.Flavor))
            return;

        _pendingDeferredArtifact = null;
        _deferredInstallTimer.Stop();
        await InstallArtifactAsync(artifact);
    }

    private async Task InstallArtifactAsync(ReleaseArtifact artifact)
    {
        _isBusy = true;
        SetControlsEnabled(false);
        try
        {
            var progress = new Progress<(int Percent, string StatusText)>(update =>
            {
                UpdateProgress.Value = update.Percent;
                ProgressText.Text = update.Percent > 0 ? $"{update.Percent}%" : "";
                StatusText.Text = update.StatusText;
            });
            await _installerService.DownloadVerifyAndInstallAsync(artifact, progress, _lifetime.Token);
            UpdateProgress.Value = 100;
            InstalledVersionText.Text = _localChromiumService.GetInstalledVersion(artifact.Flavor);
            InstallButton.IsEnabled = false;
            _pendingDeferredArtifact = null;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (OperationCanceledException ex)
        {
            StatusText.Text = $"The update download timed out: {ex.Message}";
        }
        catch (Exception ex) when (
            ex is InvalidOperationException or PlatformNotSupportedException or IOException or
            UnauthorizedAccessException or System.ComponentModel.Win32Exception or AggregateException)
        {
            StatusText.Text = $"Installation failed: {ex.Message}";
        }
        finally
        {
            _isBusy = false;
            SetControlsEnabled(true);
        }
    }

    private void SaveConfig()
    {
        _configService.Save();
        if (_configService.LastError is not null)
            StatusText.Text = _configService.LastError;
    }

    private void ResetScheduler()
    {
        _schedulerTimer.Stop();
        _schedulerTimer.Interval = TimeSpan.FromHours(Math.Clamp(_configService.Current.CheckIntervalHours, 1, 72));
        _schedulerTimer.Start();
    }

    private void SetControlsEnabled(bool enabled)
    {
        FlavorSelector.IsEnabled = enabled && FlavorSelector.Items.Count > 0;
        CheckNowButton.IsEnabled = enabled;
        OpenBuildPageButton.IsEnabled = enabled && _latestArtifact is not null;
        InstallButton.IsEnabled = enabled && _latestArtifact is not null &&
                                  _localChromiumService.IsRemoteNewer(
                                      _localChromiumService.GetInstalledVersion(_configService.Current.SelectedFlavor),
                                      _latestArtifact.CleanVersion);
    }
}
