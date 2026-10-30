using ChromiumBuildUpdater.Core.Models;
using ChromiumBuildUpdater.Core.Services;

namespace ChromiumBuildUpdater.Tests;

public class UpdateCheckServiceTests
{
    [Theory]
    [InlineData("156.0.8078.73", true)]
    [InlineData("156.0.8078.74", false)]
    [InlineData("Not Installed", true)]
    public async Task CheckAsync_UsesInstalledVersionAndPersistsReleaseMetadata(
        string installedVersion,
        bool expectedUpdateAvailable)
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            var config = new ConfigService(directory);
            config.Current.PreferChromiumBuildsOrgApi = true;
            var provider = new StubReleaseProvider(CreateArtifact());
            var installedBuild = new StubInstalledBuildService(installedVersion);
            var service = new UpdateCheckService(provider, installedBuild, config);

            UpdateCheckResult result = await service.CheckAsync(ChromiumFlavor.Hibbiki);

            Assert.Equal(expectedUpdateAvailable, result.UpdateAvailable);
            Assert.Equal(installedVersion, result.InstalledVersion);
            Assert.Equal(ChromiumFlavor.Hibbiki, provider.RequestedFlavor);
            Assert.True(provider.PreferChromiumBuildsOrg);
            Assert.Equal("156.0.8078.74", config.Current.LastKnownRemoteVersion);
            Assert.Equal(result.ReleaseDateText, config.Current.LastKnownRemoteDate);
            Assert.Equal("Test feed", config.Current.LastKnownSource);
            Assert.Equal(result.LastCheckTime, config.Current.LastCheckTime);
            Assert.Null(result.SettingsError);

            var reloaded = new ConfigService(directory);
            Assert.Equal("156.0.8078.74", reloaded.Current.LastKnownRemoteVersion);
            Assert.Equal("Test feed", reloaded.Current.LastKnownSource);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task CheckAsync_PropagatesProviderFailureWithoutUpdatingCachedRelease()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            var config = new ConfigService(directory);
            config.Current.LastKnownRemoteVersion = "previous";
            var service = new UpdateCheckService(
                new StubReleaseProvider(null, new InvalidOperationException("feed unavailable")),
                new StubInstalledBuildService("156.0.8078.73"),
                config);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.CheckAsync(ChromiumFlavor.Hibbiki));

            Assert.Equal("previous", config.Current.LastKnownRemoteVersion);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static ReleaseArtifact CreateArtifact() =>
        new(
            ChromiumFlavor.Hibbiki,
            "156.0.8078.74",
            "v156.0.8078.74",
            new DateTime(2026, 10, 28, 23, 19, 0, DateTimeKind.Utc),
            "https://example.test/installer.exe",
            new string('a', 64),
            "Test feed");

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

    private sealed class StubReleaseProvider(
        ReleaseArtifact? artifact,
        Exception? failure = null) : IReleaseProvider
    {
        public ChromiumFlavor RequestedFlavor { get; private set; }
        public bool PreferChromiumBuildsOrg { get; private set; }

        public Task<ReleaseArtifact> FetchLatestReleaseAsync(
            ChromiumFlavor flavor,
            bool preferChromiumBuildsOrg = false,
            CancellationToken ct = default)
        {
            RequestedFlavor = flavor;
            PreferChromiumBuildsOrg = preferChromiumBuildsOrg;
            if (failure is not null)
                return Task.FromException<ReleaseArtifact>(failure);
            return Task.FromResult(artifact
                ?? throw new InvalidOperationException("The test release artifact was not provided."));
        }
    }

    private sealed class StubInstalledBuildService(string installedVersion) : IInstalledBuildService
    {
        public string GetInstalledVersion(ChromiumFlavor flavor) => installedVersion;

        public bool IsRemoteNewer(string localVersion, string remoteVersion) =>
            VersionService.IsRemoteNewer(localVersion, remoteVersion);
    }
}
