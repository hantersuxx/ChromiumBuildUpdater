using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using ChromiumBuildUpdater.Core.Models;
using ChromiumBuildUpdater.Core.Services;

namespace ChromiumBuildUpdater;

public partial class App : Application
{
    public static string? StartupUpdateError { get; set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var configService = new ConfigService();
            RequestedThemeVariant = configService.Current.Theme switch
            {
                AppTheme.Light => ThemeVariant.Light,
                AppTheme.Dark => ThemeVariant.Dark,
                _ => ThemeVariant.Default
            };
            desktop.MainWindow = new MainWindow(configService);
        }

        base.OnFrameworkInitializationCompleted();
    }
}