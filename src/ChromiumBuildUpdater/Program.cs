using Avalonia;
using System;
using ChromiumBuildUpdater.Core.Services;

namespace ChromiumBuildUpdater;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        if (SelfUpdateLauncher.IsApplyRequest(args))
        {
            Environment.ExitCode = SelfUpdateLauncher.RunApplyRequest(args);
            return;
        }

        string[] applicationArgs = args;
        if (SelfUpdateLauncher.IsCleanupHelperRequest(args))
        {
            SelfUpdateLauncher.CleanupHelper(args);
            applicationArgs = [];
        }
        else if (SelfUpdateLauncher.TryGetUpdateFailureMessage(args, out string failureMessage))
        {
            App.StartupUpdateError = failureMessage;
            applicationArgs = [];
        }

        using SingleInstanceService? singleInstance =
            SingleInstanceService.TryCreate("ChromiumBuildUpdater");
        if (singleInstance is null)
            return;

        App.SingleInstance = singleInstance;
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(applicationArgs);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
