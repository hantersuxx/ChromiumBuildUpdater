using System.Text.Json;
using ChromiumBuildUpdater.Core.Models;
using ChromiumBuildUpdater.Core.Services;

namespace ChromiumBuildUpdater.Tests;

public class ConfigServiceTests
{
    [Fact]
    public void Constructor_CreatesDefaultConfigOnFirstRun()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"ChromiumBuildUpdater.Tests.{Guid.NewGuid():N}");

        try
        {
            var service = new ConfigService(directory);

            string configPath = Path.Combine(directory, "config.json");
            Assert.True(File.Exists(configPath));
            Assert.Equal(new AppConfig().AutoUpdate, service.Current.AutoUpdate);
            Assert.Equal(new AppConfig().CheckIntervalHours, service.Current.CheckIntervalHours);

            var reloadedService = new ConfigService(directory);
            Assert.Equal(service.Current.AutoUpdate, reloadedService.Current.AutoUpdate);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Save_RoundTripsUpdaterPreferences()
    {
        string directory = CreateTemporaryDirectory();

        try
        {
            var service = new ConfigService(directory);
            service.Current.AutoUpdate = false;
            service.Current.CheckIntervalHours = 12;
            service.Current.SelectedFlavor = ChromiumFlavor.Thorium;
            service.Save();

            var reloadedService = new ConfigService(directory);

            Assert.Null(service.LastError);
            Assert.False(reloadedService.Current.AutoUpdate);
            Assert.Equal(12, reloadedService.Current.CheckIntervalHours);
            Assert.Equal(ChromiumFlavor.Thorium, reloadedService.Current.SelectedFlavor);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public void Save_DropsLegacySupportLinkOverrides()
    {
        string directory = CreateTemporaryDirectory();

        try
        {
            string configPath = Path.Combine(directory, "config.json");
            File.WriteAllText(configPath, """
                {
                  "AutoUpdate": false,
                  "SupportUrl": "https://example.test/",
                  "EthereumAddress": "legacy-eth-address",
                  "BitcoinAddress": "legacy-btc-address"
                }
                """);

            var service = new ConfigService(directory);
            Assert.False(service.Current.AutoUpdate);

            service.Save();

            using JsonDocument savedConfig = JsonDocument.Parse(File.ReadAllText(configPath));
            Assert.False(savedConfig.RootElement.TryGetProperty("SupportUrl", out _));
            Assert.False(savedConfig.RootElement.TryGetProperty("EthereumAddress", out _));
            Assert.False(savedConfig.RootElement.TryGetProperty("BitcoinAddress", out _));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public void Constructor_MigratesExistingSettingsToRenamedConfigDirectory()
    {
        string directory = CreateTemporaryDirectory();
        string legacyDirectory = CreateTemporaryDirectory();

        try
        {
            string legacyConfigPath = Path.Combine(legacyDirectory, "config.json");
            File.WriteAllText(legacyConfigPath, """
                {
                  "AutoUpdate": false,
                  "SelectedFlavor": "Thorium",
                  "CheckIntervalHours": 12
                }
                """);

            var service = new ConfigService(directory, legacyConfigPath);

            Assert.False(service.Current.AutoUpdate);
            Assert.Equal(ChromiumFlavor.Thorium, service.Current.SelectedFlavor);
            Assert.Equal(12, service.Current.CheckIntervalHours);
            Assert.True(File.Exists(Path.Combine(directory, "config.json")));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
            DeleteTemporaryDirectory(legacyDirectory);
        }
    }

    [Fact]
    public void Constructor_InvalidJsonUsesDefaultsAndReportsError()
    {
        string directory = CreateTemporaryDirectory();

        try
        {
            File.WriteAllText(Path.Combine(directory, "config.json"), "{ invalid");

            var service = new ConfigService(directory);

            Assert.True(service.Current.AutoUpdate);
            Assert.Contains("defaults are in use", service.LastError);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"ChromiumBuildUpdater.Tests.{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void DeleteTemporaryDirectory(string directory)
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }
}
