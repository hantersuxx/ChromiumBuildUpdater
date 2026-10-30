using HibbikiUpdater.Models;
using HibbikiUpdater.Services;

namespace HibbikiUpdater.Tests;

public class ConfigServiceTests
{
    [Fact]
    public void Constructor_CreatesDefaultConfigOnFirstRun()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"HibbikiUpdater.Tests.{Guid.NewGuid():N}");

        try
        {
            var service = new ConfigService(directory);

            string configPath = Path.Combine(directory, "config.json");
            Assert.True(File.Exists(configPath));
            Assert.Equal(new AppConfig().AutoUpdate, service.Current.AutoUpdate);
            Assert.Equal(new AppConfig().CheckIntervalHours, service.Current.CheckIntervalHours);

            var reloadedService = new ConfigService(directory);
            Assert.Equal(service.Current.AutoUpdate, reloadedService.Current.AutoUpdate);
            Assert.Equal(service.Current.SupportUrl, reloadedService.Current.SupportUrl);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }
}
