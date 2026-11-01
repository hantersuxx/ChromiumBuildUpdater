using Avalonia;
using Avalonia.Controls;
using ChromiumBuildUpdater.Core.Models;
using ChromiumBuildUpdater.Core.Services;

namespace ChromiumBuildUpdater;

public partial class MainWindow
{
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
            config.PreferChromiumBuildsOrgApi = false;
    }

    private void HookPreferenceEvents()
    {
        BindToggle(AutoUpdateCheckBox, value => _configService.Current.AutoUpdate = value);
        BindToggle(
            DeferWhileRunningCheckBox,
            value => _configService.Current.DeferIfChromiumRunning = value);
        BindToggle(StartWithSystemCheckBox, SetStartupEnabled);
        BindToggle(MinimizeToTrayCheckBox, value =>
        {
            _configService.Current.MinimizeToTrayOnClose = value;
            _minimizeToTrayOnClose = OperatingSystem.IsWindows() && value;
        });
        IntervalSelector.SelectionChanged += (_, _) =>
        {
            if (_initializing || IntervalSelector.SelectedItem is not ComboBoxItem item ||
                !int.TryParse(item.Tag?.ToString(), out int hours))
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
            Application.Current!.RequestedThemeVariant = App.GetThemeVariant(theme);
            SaveConfig();
        };
    }

    private void BindToggle(CheckBox checkBox, Action<bool> update)
    {
        checkBox.Click += (_, _) =>
        {
            if (_initializing)
                return;

            update(checkBox.IsChecked == true);
            SaveConfig();
        };
    }

    private void SetStartupEnabled(bool enabled)
    {
        _configService.Current.StartWithWindows = enabled;
        try
        {
            _startupService.SyncStartupState(enabled);
        }
        catch (Exception ex) when (
            ex is InvalidOperationException or IOException or UnauthorizedAccessException or
            System.ComponentModel.Win32Exception or PlatformNotSupportedException)
        {
            StatusText.Text = $"Startup setting could not be applied: {ex.Message}";
        }
    }

    private void AddIntervalIfMissing(int hours)
    {
        string label = $"{hours} hour{(hours == 1 ? "" : "s")}";
        bool exists = IntervalSelector.Items
            .OfType<ComboBoxItem>()
            .Any(item => string.Equals(item.Content?.ToString(), label, StringComparison.Ordinal));
        if (!exists)
            IntervalSelector.Items.Add(new ComboBoxItem { Content = label, Tag = hours });
    }

    private void SelectInterval(int hours)
    {
        IntervalSelector.SelectedItem = IntervalSelector.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(item =>
                int.TryParse(item.Tag?.ToString(), out int itemHours) &&
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
}
