using System;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using HibbikiUpdater.Services;
using HibbikiUpdater.UI;

namespace HibbikiUpdater;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        const string mutexName = @"Global\HibbikiChromiumUpdater_SingleInstance_Mutex_v2";
        using var singleInstanceMutex = new Mutex(true, mutexName, out bool createdNew);

        if (!createdNew)
        {
            MessageBox.Show(
                "Chromium Tray Updater is already running in the system tray.",
                "Chromium Updater",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();

        var configService = new ConfigService();
        var startupService = new RegistryStartupService();
        var localChromiumService = new LocalChromiumService();
        var releaseProviderService = new ReleaseProviderService();
        var installerService = new InstallerService();

        bool startMinimized = args.Contains("--minimized", StringComparer.OrdinalIgnoreCase);

        try
        {
            Application.Run(new MainForm(
                configService,
                startupService,
                localChromiumService,
                releaseProviderService,
                installerService,
                startMinimized));
        }
        finally
        {
            singleInstanceMutex.ReleaseMutex();
        }
    }
}
