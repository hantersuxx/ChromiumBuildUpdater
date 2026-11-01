using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Threading;
using ChromiumBuildUpdater.Core.Models;
using ChromiumBuildUpdater.Core.Services;

namespace ChromiumBuildUpdater;

public partial class MainWindow : Window
{
    private readonly ConfigService _configService;
    private readonly PlatformStartupService _startupService = new();
    private readonly LocalChromiumService _localChromiumService = new();
    private readonly UpdateCheckService _updateCheckService;
    private readonly InstallerService _installerService = new();
    private readonly DispatcherTimer _schedulerTimer = new();
    private readonly DispatcherTimer _deferredInstallTimer = new();
    private readonly CancellationTokenSource _lifetime = new();
    private FlavorChoice[] _flavorChoices = [];

    private ReleaseArtifact? _pendingDeferredArtifact;
    private ReleaseArtifact? _latestArtifact;
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
            new ReleaseProviderService(),
            _localChromiumService,
            _configService);
        PlatformText.Text = GetPlatformDescription();
        PopulateFlavorSelector();
        BindConfigToUi();
        HookEvents();
        ConfigureTrayIcon();

        if (_configService.LastError is not null)
            StatusText.Text = _configService.LastError;

        InitializeLifecycle();
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

        HookPreferenceEvents();
        HookAppUpdateEvents();

        InstallButton.Click += async (_, _) =>
        {
            if (_latestArtifact is not null)
                await InstallOrDeferAsync(_latestArtifact);
        };
        CheckNowButton.Click += async (_, _) =>
            await RunUpdateCycleAsync(allowAutomaticInstall: false);
        OpenBuildPageButton.Click += (_, _) => OpenReleasePage();
        PayPalButton.Click += (_, _) => OpenSupportPage();
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
