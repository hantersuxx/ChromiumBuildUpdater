using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using ChromiumBuildUpdater.Core.Models;
using ChromiumBuildUpdater.Core.Services;

namespace ChromiumBuildUpdater;

public partial class MainWindow : Window
{
    private const string PayPalSupportUrl = "https://paypal.me/AliaksandrJoya";

    private readonly ConfigService _configService;
    private readonly PlatformStartupService _startupService = new();
    private readonly LocalChromiumService _localChromiumService = new();
    private readonly ReleaseProviderService _releaseProviderService = new();
    private readonly UpdateCheckService _updateCheckService;
    private readonly AppUpdateService _appUpdateService = new();
    private readonly InstallerService _installerService = new();
    private readonly DispatcherTimer _schedulerTimer = new();
    private readonly DispatcherTimer _deferredInstallTimer = new();
    private readonly CancellationTokenSource _lifetime = new();
    private FlavorChoice[] _flavorChoices = [];
    private TrayIcon? _trayIcon;

    private ReleaseArtifact? _pendingDeferredArtifact;
    private ReleaseArtifact? _latestArtifact;
    private AppUpdateRelease? _availableAppUpdate;
    private bool _isBusy;
    private bool _initializing;
    private bool _minimizeToTrayOnClose;
    private bool _allowClose;
    private bool _didInitialCheck;

    public MainWindow() : this(new ConfigService())
    {
    }

    public MainWindow(ConfigService configService)
    {
        InitializeComponent();

        _configService = configService;
        _updateCheckService = new UpdateCheckService(
            _releaseProviderService,
            _localChromiumService,
            _configService);
        PlatformText.Text = GetPlatformDescription();
        PopulateFlavorSelector();
        BindConfigToUi();
        HookEvents();
        ConfigureTrayIcon();

        if (_configService.LastError is not null)
            StatusText.Text = _configService.LastError;

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
        Opened += async (_, _) =>
        {
            if (!_didInitialCheck)
            {
                _didInitialCheck = true;
                await RunUpdateCycleAsync(allowAutomaticInstall: true);
                if (App.StartupUpdateError is null)
                {
                    await CheckForAppUpdateAsync();
                }
                else
                {
                    AppUpdateStatusText.Text =
                        $"The previous app update did not finish: {App.StartupUpdateError}";
                }
            }
        };
        Closing += (_, args) =>
        {
            if (!_allowClose && _minimizeToTrayOnClose)
            {
                args.Cancel = true;
                Hide();
            }
        };
        Closed += (_, _) =>
        {
            _schedulerTimer.Stop();
            _deferredInstallTimer.Stop();
            _lifetime.Cancel();
            _lifetime.Dispose();
            if (_trayIcon is not null)
            {
                TrayIcon.SetIcons(Application.Current!, new TrayIcons());
                _trayIcon.Dispose();
                _trayIcon = null;
            }
        };
        ResetScheduler();
    }

    private void PopulateFlavorSelector()
    {
        _flavorChoices = PlatformSupport.GetAvailableFlavors(PlatformSupport.Current)
            .Select(flavor => new FlavorChoice(flavor, GetDisplayName(flavor)))
            .ToArray();
        FlavorSelector.ItemsSource = _flavorChoices;

        if (_flavorChoices.Length == 0)
        {
            FlavorSelector.IsEnabled = false;
            InstallButton.IsEnabled = false;
            CheckNowButton.IsEnabled = false;
            StatusText.Text = $"No supported browser builds are available for {PlatformSupport.Current}.";
        }
    }

    private void BindConfigToUi()
    {
        _initializing = true;
        var config = _configService.Current;
        FlavorChoice? selectedChoice = _flavorChoices.FirstOrDefault(
            choice => choice.Flavor == config.SelectedFlavor);

        if (selectedChoice is null && _flavorChoices.Length > 0)
        {
            selectedChoice = _flavorChoices[0];
            config.SelectedFlavor = selectedChoice.Flavor;
            _configService.Save();
        }

        FlavorSelector.SelectedItem = selectedChoice;
        AutoUpdateCheckBox.IsChecked = config.AutoUpdate;
        DeferWhileRunningCheckBox.IsChecked = config.DeferIfChromiumRunning;
        StartWithSystemCheckBox.IsChecked = config.StartWithWindows;
        MinimizeToTrayCheckBox.IsVisible = OperatingSystem.IsWindows();
        MinimizeToTrayCheckBox.IsChecked = config.MinimizeToTrayOnClose;
        _minimizeToTrayOnClose = OperatingSystem.IsWindows() && config.MinimizeToTrayOnClose;

        AddIntervalIfMissing(config.CheckIntervalHours);
        SelectInterval(config.CheckIntervalHours);
        SelectTheme(config.Theme);
        UpdateInstalledVersion();
        UpdateAvailabilityNote();
        UpdateLastCheckedText();
        _initializing = false;

        if (PlatformSupport.Current != HostPlatform.Windows)
            _configService.Current.PreferChromiumBuildsOrgApi = false;
    }

    private void HookEvents()
    {
        FlavorSelector.SelectionChanged += async (_, _) =>
        {
            if (_initializing || FlavorSelector.SelectedItem is not FlavorChoice choice)
                return;

            _configService.Current.SelectedFlavor = choice.Flavor;
            _latestArtifact = null;
            _pendingDeferredArtifact = null;
            _deferredInstallTimer.Stop();
            _configService.Save();
            UpdateInstalledVersion();
            UpdateAvailabilityNote();
            await RunUpdateCycleAsync(allowAutomaticInstall: false);
        };

        AutoUpdateCheckBox.Click += (_, _) =>
        {
            if (_initializing)
                return;
            _configService.Current.AutoUpdate = AutoUpdateCheckBox.IsChecked == true;
            SaveConfig();
        };

        DeferWhileRunningCheckBox.Click += (_, _) =>
        {
            if (_initializing)
                return;
            _configService.Current.DeferIfChromiumRunning = DeferWhileRunningCheckBox.IsChecked == true;
            SaveConfig();
        };

        StartWithSystemCheckBox.Click += (_, _) =>
        {
            if (_initializing)
                return;
            _configService.Current.StartWithWindows = StartWithSystemCheckBox.IsChecked == true;
            SaveConfig();
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
        };

        MinimizeToTrayCheckBox.Click += (_, _) =>
        {
            if (_initializing)
                return;
            _configService.Current.MinimizeToTrayOnClose = MinimizeToTrayCheckBox.IsChecked == true;
            _minimizeToTrayOnClose = OperatingSystem.IsWindows() &&
                                     _configService.Current.MinimizeToTrayOnClose;
            SaveConfig();
        };

        IntervalSelector.SelectionChanged += (_, _) =>
        {
            if (_initializing || IntervalSelector.SelectedItem is not ComboBoxItem item ||
                !int.TryParse(item.Content?.ToString()?.Split(' ')[0], out int hours))
            {
                return;
            }

            _configService.Current.CheckIntervalHours = hours;
            SaveConfig();
            ResetScheduler();
        };

        ThemeSelector.SelectionChanged += (_, _) =>
        {
            if (_initializing ||
                ThemeSelector.SelectedItem is not ComboBoxItem item ||
                !Enum.TryParse(item.Tag?.ToString(), out AppTheme theme))
            {
                return;
            }

            _configService.Current.Theme = theme;
            Application.Current!.RequestedThemeVariant = theme switch
            {
                AppTheme.Light => Avalonia.Styling.ThemeVariant.Light,
                AppTheme.Dark => Avalonia.Styling.ThemeVariant.Dark,
                _ => Avalonia.Styling.ThemeVariant.Default
            };
            SaveConfig();
        };

        InstallButton.Click += async (_, _) =>
        {
            if (_latestArtifact is not null)
                await InstallOrDeferAsync(_latestArtifact);
        };
        CheckAppUpdatesButton.Click += async (_, _) => await CheckForAppUpdateAsync();
        InstallAppUpdateButton.Click += async (_, _) => await InstallAppUpdateAsync();
        CheckNowButton.Click += async (_, _) =>
            await RunUpdateCycleAsync(allowAutomaticInstall: false);
        OpenBuildPageButton.Click += (_, _) => OpenReleasePage();
        PayPalButton.Click += (_, _) => OpenSupportPage();
    }

    private async Task CheckForAppUpdateAsync()
    {
        CheckAppUpdatesButton.IsEnabled = false;
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
            stagedUpdate = await _appUpdateService.DownloadAndVerifyAsync(
                _availableAppUpdate, _lifetime.Token);
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

    private void UpdateInstalledVersion() =>
        InstalledVersionText.Text = _localChromiumService.GetInstalledVersion(_configService.Current.SelectedFlavor);

    private void UpdateAvailabilityNote()
    {
        bool isLinuxThorium = PlatformSupport.Current == HostPlatform.Linux &&
                              _configService.Current.SelectedFlavor == ChromiumFlavor.Thorium;
        AvailabilityNote.IsVisible = isLinuxThorium;
        AvailabilityNote.Text = isLinuxThorium
            ? "Thorium Linux assets are beta builds published by a community contributor."
            : "";
    }

    private void UpdateLastCheckedText()
    {
        string value = _configService.Current.LastCheckTime;
        LastCheckedText.Text = value == "Never" ? "Last checked: never" : $"Last checked: {value}";
    }

    private static string AppendSettingsWarning(string status, string? settingsError) =>
        string.IsNullOrWhiteSpace(settingsError)
            ? status
            : $"{status} Settings warning: {settingsError}";

    private void AddIntervalIfMissing(int hours)
    {
        string label = $"{hours} hour{(hours == 1 ? "" : "s")}";
        bool exists = IntervalSelector.Items
            .OfType<ComboBoxItem>()
            .Any(item => string.Equals(item.Content?.ToString(), label, StringComparison.Ordinal));
        if (!exists)
            IntervalSelector.Items.Add(new ComboBoxItem { Content = label });
    }

    private void SelectInterval(int hours)
    {
        IntervalSelector.SelectedItem = IntervalSelector.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(item =>
                int.TryParse(item.Content?.ToString()?.Split(' ')[0], out int itemHours) &&
                itemHours == hours);
    }

    private void SelectTheme(AppTheme theme)
    {
        ThemeSelector.SelectedItem = ThemeSelector.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(item =>
                Enum.TryParse(item.Tag?.ToString(), out AppTheme itemTheme) &&
                itemTheme == theme);
    }

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
            ToolTipText = "Chromium Build Updater"
        };
        var trayMenu = new NativeMenu();
        var openItem = new NativeMenuItem("Open dashboard");
        openItem.Click += (_, _) => RestoreFromTray();
        var checkItem = new NativeMenuItem("Check for updates");
        checkItem.Click += async (_, _) => await RunUpdateCycleAsync(allowAutomaticInstall: false);
        var exitItem = new NativeMenuItem("Exit");
        exitItem.Click += (_, _) => ExitApplication();
        trayMenu.Add(openItem);
        trayMenu.Add(checkItem);
        trayMenu.Add(new NativeMenuItemSeparator());
        trayMenu.Add(exitItem);
        _trayIcon.Menu = trayMenu;
        _trayIcon.Clicked += (_, _) => RestoreFromTray();
        TrayIcon.SetIcons(Application.Current!, new TrayIcons { _trayIcon });
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

    private static string GetDisplayName(ChromiumFlavor flavor) =>
        flavor switch
        {
            ChromiumFlavor.Hibbiki => "Hibbiki Chromium",
            ChromiumFlavor.UngoogledChromium => "Ungoogled Chromium",
            ChromiumFlavor.Thorium => "Thorium",
            _ => flavor.ToString()
        };

    private static string GetPlatformDescription()
    {
        string platform = PlatformSupport.Current switch
        {
            HostPlatform.Windows => "Windows",
            HostPlatform.MacOS => "macOS",
            HostPlatform.Linux => "Linux",
            _ => "Unsupported OS"
        };
        string architecture = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "ARM64",
            _ => RuntimeInformation.ProcessArchitecture.ToString()
        };
        return $"{platform} · {architecture}";
    }

    private sealed record FlavorChoice(ChromiumFlavor Flavor, string DisplayName)
    {
        public override string ToString() => DisplayName;
    }
}
