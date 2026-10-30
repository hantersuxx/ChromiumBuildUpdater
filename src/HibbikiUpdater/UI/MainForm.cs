using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using HibbikiUpdater.Models;
using HibbikiUpdater.Services;

namespace HibbikiUpdater.UI;

public partial class MainForm : Form
{
    private readonly ConfigService _configService;
    private readonly RegistryStartupService _startupService;
    private readonly LocalChromiumService _localChromiumService;
    private readonly ReleaseProviderService _releaseProviderService;
    private readonly InstallerService _installerService;

    private readonly NotifyIcon _trayIcon;
    private readonly System.Windows.Forms.Timer _schedulerTimer;
    private readonly System.Windows.Forms.Timer _deferredInstallWatchdogTimer;

    private ReleaseArtifact? _pendingDeferredArtifact;
    private bool _isBusy;
    private bool _currentArtifactInstallable;
    private bool _suppressUiEvents;

    public MainForm(
        ConfigService configService,
        RegistryStartupService startupService,
        LocalChromiumService localChromiumService,
        ReleaseProviderService releaseProviderService,
        InstallerService installerService,
        bool startMinimized)
    {
        _configService = configService;
        _startupService = startupService;
        _localChromiumService = localChromiumService;
        _releaseProviderService = releaseProviderService;
        _installerService = installerService;

        InitializeComponent();
        BindConfigToUi();
        _lnkPayPal.Enabled = TryGetSupportUri(_configService.Current.SupportUrl, out _);
        _lnkEthereum.Enabled = !string.IsNullOrWhiteSpace(_configService.Current.EthereumAddress);
        _lnkBitcoin.Enabled = !string.IsNullOrWhiteSpace(_configService.Current.BitcoinAddress);
        SetButtonsEnabled(true);
        if (_configService.LastError is not null)
            _lblStatus.Text = _configService.LastError;

        _startupService.SyncStartupState(_configService.Current.StartWithWindows);

        // System Tray Icon & Context Menu
        var trayMenu = new ContextMenuStrip();
        trayMenu.Items.Add("Open Dashboard", null, (_, _) => RestoreFromTray());
        trayMenu.Items.Add("Check Only", null, async (_, _) => await RunUpdateCycleAsync(forceInstall: false, manualCheckOnly: true));
        trayMenu.Items.Add("Force Update Now", null, async (_, _) => await RunUpdateCycleAsync(forceInstall: true));
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add("Exit Completely", null, (_, _) => ExitApplication());

        _trayIcon = new NotifyIcon
        {
            Icon = SystemIcons.Shield,
            Text = "Chromium Tray Updater v2",
            ContextMenuStrip = trayMenu,
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) => RestoreFromTray();

        if (_configService.LastError is not null)
            ShowNotification("Configuration warning", _configService.LastError, ToolTipIcon.Warning);

        // Periodic Background Scheduler Timer
        _schedulerTimer = new System.Windows.Forms.Timer
        {
            Interval = Math.Max(1, _configService.Current.CheckIntervalHours) * 3600 * 1000
        };
        _schedulerTimer.Tick += async (_, _) => await RunUpdateCycleAsync(forceInstall: false);
        _schedulerTimer.Start();

        // Active Process Guard Watchdog: checks every 60s if a deferred update can now be installed because browser closed
        _deferredInstallWatchdogTimer = new System.Windows.Forms.Timer
        {
            Interval = 60 * 1000
        };
        _deferredInstallWatchdogTimer.Tick += async (_, _) => await TryInstallPendingDeferredUpdateAsync();

        if (startMinimized)
        {
            WindowState = FormWindowState.Minimized;
            ShowInTaskbar = false;
            Load += (_, _) => Hide();
        }

        Shown += async (_, _) => await RunUpdateCycleAsync(forceInstall: false);
    }

    private void BindConfigToUi()
    {
        _suppressUiEvents = true;
        var cfg = _configService.Current;

        int selectedFlavorIndex = Enum.IsDefined(cfg.SelectedFlavor) ? (int)cfg.SelectedFlavor : 0;
        cfg.SelectedFlavor = (ChromiumFlavor)selectedFlavorIndex;
        _cmbFlavor.SelectedIndex = selectedFlavorIndex;
        _lblInstalledVer.Text = _localChromiumService.GetInstalledVersion(cfg.SelectedFlavor);
        _lblLatestVer.Text = cfg.LastKnownRemoteVersion;
        _lblReleaseDate.Text = cfg.LastKnownRemoteDate;
        _lblSourceEndpoint.Text = cfg.LastKnownSource;
        _lblLastChecked.Text = cfg.LastCheckTime;

        _chkAutoUpdate.Checked = cfg.AutoUpdate;
        _chkNotifications.Checked = cfg.EnableNotifications;
        _chkMinimizeOnClose.Checked = cfg.MinimizeToTrayOnClose;
        _chkStartWithWindows.Checked = cfg.StartWithWindows;
        _chkDeferIfRunning.Checked = cfg.DeferIfChromiumRunning;
        _chkPreferFallbackApi.Checked = cfg.PreferChromiumBuildsOrgApi;
        _numIntervalHours.Value = Math.Clamp(cfg.CheckIntervalHours, 1, 72);
        _suppressUiEvents = false;

        EventHandler onSettingChanged = (_, _) =>
        {
            if (_suppressUiEvents) return;

            cfg.AutoUpdate = _chkAutoUpdate.Checked;
            cfg.EnableNotifications = _chkNotifications.Checked;
            cfg.MinimizeToTrayOnClose = _chkMinimizeOnClose.Checked;
            cfg.StartWithWindows = _chkStartWithWindows.Checked;
            cfg.DeferIfChromiumRunning = _chkDeferIfRunning.Checked;
            cfg.PreferChromiumBuildsOrgApi = _chkPreferFallbackApi.Checked;
            cfg.CheckIntervalHours = (int)_numIntervalHours.Value;

            _configService.Save();
            if (_configService.LastError is not null)
            {
                _lblStatus.Text = _configService.LastError;
                ShowNotification("Settings not saved", _configService.LastError, ToolTipIcon.Error);
            }
            _startupService.SyncStartupState(cfg.StartWithWindows);
            _schedulerTimer.Interval = cfg.CheckIntervalHours * 3600 * 1000;
        };

        _chkAutoUpdate.CheckedChanged += onSettingChanged;
        _chkNotifications.CheckedChanged += onSettingChanged;
        _chkMinimizeOnClose.CheckedChanged += onSettingChanged;
        _chkStartWithWindows.CheckedChanged += onSettingChanged;
        _chkDeferIfRunning.CheckedChanged += onSettingChanged;
        _chkPreferFallbackApi.CheckedChanged += onSettingChanged;
        _numIntervalHours.ValueChanged += onSettingChanged;

        _cmbFlavor.SelectedIndexChanged += async (_, _) =>
        {
            if (_suppressUiEvents) return;
            cfg.SelectedFlavor = (ChromiumFlavor)_cmbFlavor.SelectedIndex;
            _pendingDeferredArtifact = null;
            _currentArtifactInstallable = false;
            _deferredInstallWatchdogTimer.Stop();
            _configService.Save();

            _lblInstalledVer.Text = _localChromiumService.GetInstalledVersion(cfg.SelectedFlavor);
            await RunUpdateCycleAsync(forceInstall: false, manualCheckOnly: true);
        };

        _btnManualUpdate.Click += async (_, _) => await RunUpdateCycleAsync(forceInstall: true);
        _btnCheckNow.Click += async (_, _) => await RunUpdateCycleAsync(forceInstall: false, manualCheckOnly: true);
        _lnkPayPal.LinkClicked += (_, _) => OpenSupportPage();
        _lnkEthereum.LinkClicked += (_, _) => CopySupportAddress(
            _configService.Current.EthereumAddress,
            "Ethereum address");
        _lnkBitcoin.LinkClicked += (_, _) => CopySupportAddress(
            _configService.Current.BitcoinAddress,
            "Bitcoin address");
    }

    private static bool TryGetSupportUri(string? supportUrl, out Uri? uri) =>
        Uri.TryCreate(supportUrl, UriKind.Absolute, out uri) &&
        uri.Scheme == Uri.UriSchemeHttps &&
        string.IsNullOrEmpty(uri.UserInfo);

    private void OpenSupportPage()
    {
        if (!TryGetSupportUri(_configService.Current.SupportUrl, out Uri? supportUri))
            return;

        try
        {
            Process.Start(new ProcessStartInfo(supportUri!.AbsoluteUri) { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            _lblStatus.Text = $"Could not open support page: {ex.Message}";
        }
    }

    private void CopySupportAddress(string? address, string currency)
    {
        if (string.IsNullOrWhiteSpace(address))
            return;

        try
        {
            Clipboard.SetText(address);
            _lblStatus.Text = $"{currency} copied to clipboard.";
        }
        catch (System.Runtime.InteropServices.ExternalException ex)
        {
            _lblStatus.Text = $"Could not copy {currency}: {ex.Message}";
        }
    }

    private async Task RunUpdateCycleAsync(bool forceInstall, bool manualCheckOnly = false)
    {
        if (_isBusy) return;
        _isBusy = true;
        SetButtonsEnabled(false);
        _progressBar.Value = 0;

        try
        {
            var cfg = _configService.Current;
            var flavor = cfg.SelectedFlavor;

            string localVer = _localChromiumService.GetInstalledVersion(flavor);
            _lblInstalledVer.Text = localVer;
            _lblStatus.Text = $"Querying latest {flavor} release...";

            var artifact = await _releaseProviderService.FetchLatestReleaseAsync(
                flavor,
                cfg.PreferChromiumBuildsOrgApi);

            string formattedDate = artifact.PublishedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
            string checkTimestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            try
            {
                InstallerService.ValidateArtifact(artifact);
                _currentArtifactInstallable = true;
            }
            catch (InvalidOperationException)
            {
                _currentArtifactInstallable = false;
            }

            _lblLatestVer.Text = _currentArtifactInstallable
                ? $"{artifact.CleanVersion} (SHA-256 ready)"
                : $"{artifact.CleanVersion} (install unavailable: invalid release metadata)";
            _lblLatestVer.ForeColor = _currentArtifactInstallable
                ? Color.FromArgb(0, 102, 204)
                : Color.DarkRed;
            _lblReleaseDate.Text = formattedDate;
            _lblSourceEndpoint.Text = artifact.SourceName;
            _lblLastChecked.Text = checkTimestamp;

            cfg.LastKnownRemoteVersion = _lblLatestVer.Text;
            cfg.LastKnownRemoteDate = formattedDate;
            cfg.LastKnownSource = artifact.SourceName;
            cfg.LastCheckTime = checkTimestamp;
            _configService.Save();

            bool isNewer = _localChromiumService.IsRemoteNewer(localVer, artifact.CleanVersion);

            if (!_currentArtifactInstallable && (isNewer || forceInstall))
            {
                _lblStatus.Text = $"Cannot install {flavor} {artifact.CleanVersion}: the download URL or SHA-256 checksum is invalid or missing.";
                ShowNotification("Update blocked", _lblStatus.Text, ToolTipIcon.Error);
                return;
            }

            if (manualCheckOnly && !forceInstall)
            {
                _lblStatus.Text = isNewer
                    ? $"Update available for {flavor}: {artifact.CleanVersion}!"
                    : $"{flavor} is up to date ({localVer}).";

                if (isNewer)
                {
                    ShowNotification(
                        $"{flavor} Update Available",
                        $"Version {artifact.CleanVersion} (released {formattedDate}) is ready via {artifact.SourceName}.");
                }
                return;
            }

            if (!isNewer && !forceInstall)
            {
                _lblStatus.Text = $"{flavor} is up to date (checked {DateTime.Now:HH:mm}).";
                return;
            }

            if (forceInstall || (cfg.AutoUpdate && isNewer))
            {
                // Extra Feature #7: Active Process Guard
                if (!forceInstall && cfg.DeferIfChromiumRunning && _localChromiumService.IsBrowserRunning(flavor))
                {
                    _pendingDeferredArtifact = artifact;
                    _deferredInstallWatchdogTimer.Start();
                    _lblStatus.Text = $"Update {artifact.CleanVersion} queued — waiting for {flavor} to close.";
                    ShowNotification(
                        $"{flavor} Update Queued",
                        $"Version {artifact.CleanVersion} is ready and will auto-install as soon as you close the browser.");
                    return;
                }

                await ExecuteInstallationAsync(artifact);
            }
            else if (isNewer)
            {
                _lblStatus.Text = $"New {flavor} version {artifact.CleanVersion} is available.";
                ShowNotification(
                    $"{flavor} Update Available",
                    $"Version {artifact.CleanVersion} is available. Open the tray dashboard to install.");
            }
        }
        catch (Exception ex)
        {
            _lblStatus.Text = $"Error: {ex.Message}";
            ShowNotification("Chromium Updater Error", ex.Message, ToolTipIcon.Error);
        }
        finally
        {
            _isBusy = false;
            SetButtonsEnabled(true);
        }
    }

    private async Task TryInstallPendingDeferredUpdateAsync()
    {
        if (_isBusy || _pendingDeferredArtifact == null)
            return;

        var flavor = _pendingDeferredArtifact.Flavor;
        if (_localChromiumService.IsBrowserRunning(flavor))
            return;

        // Browser was closed! Run the deferred installation now.
        _deferredInstallWatchdogTimer.Stop();
        var artifactToInstall = _pendingDeferredArtifact;
        _pendingDeferredArtifact = null;

        _isBusy = true;
        SetButtonsEnabled(false);
        try
        {
            await ExecuteInstallationAsync(artifactToInstall);
        }
        catch (Exception ex)
        {
            _lblStatus.Text = $"Deferred install error: {ex.Message}";
        }
        finally
        {
            _isBusy = false;
            SetButtonsEnabled(true);
        }
    }

    private async Task ExecuteInstallationAsync(ReleaseArtifact artifact)
    {
        var progress = new Progress<(int Percent, string StatusText)>(report =>
        {
            _progressBar.Value = Math.Clamp(report.Percent, 0, 100);
            _lblStatus.Text = report.StatusText;
        });

        await _installerService.DownloadVerifyAndInstallAsync(artifact, progress);

        _lblInstalledVer.Text = _localChromiumService.GetInstalledVersion(artifact.Flavor);
        ShowNotification(
            $"{artifact.Flavor} Updated",
            $"{artifact.Flavor} was verified (SHA-256) and updated to {artifact.CleanVersion}.");
    }

    private void SetButtonsEnabled(bool enabled)
    {
        _btnManualUpdate.Enabled = enabled && _currentArtifactInstallable;
        _btnCheckNow.Enabled = enabled;
        _cmbFlavor.Enabled = enabled;
    }

    private void ShowNotification(string title, string body, ToolTipIcon icon = ToolTipIcon.Info)
    {
        if (!_configService.Current.EnableNotifications) return;
        _trayIcon.ShowBalloonTip(4000, title, body, icon);
    }

    private void RestoreFromTray()
    {
        Show();
        WindowState = FormWindowState.Normal;
        ShowInTaskbar = true;
        Activate();
        _lblInstalledVer.Text = _localChromiumService.GetInstalledVersion(_configService.Current.SelectedFlavor);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing && _configService.Current.MinimizeToTrayOnClose)
        {
            e.Cancel = true;
            Hide();
            ShowInTaskbar = false;
            return;
        }

        _trayIcon.Visible = false;
        base.OnFormClosing(e);
    }

    private void ExitApplication()
    {
        _trayIcon.Visible = false;
        Application.Exit();
    }
}
