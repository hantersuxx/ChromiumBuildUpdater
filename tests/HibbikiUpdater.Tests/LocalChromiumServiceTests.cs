using HibbikiUpdater.Models;
using HibbikiUpdater.Services;

namespace HibbikiUpdater.Tests;

public class LocalChromiumServiceTests
{
    private readonly LocalChromiumService _service = new();

    [Theory]
    [InlineData("156.0.8078.73", "156.0.8078.74", true)]
    [InlineData("156.0.8078.74", "156.0.8078.73", false)]
    [InlineData("v156.0.8078.74", "156.0.8078.74", false)]
    [InlineData("Chromium 156.0.8078.73 Official Build", "v156.0.8078.74", true)]
    [InlineData("156.0.8078.74", "not-a-version", false)]
    public void IsRemoteNewer_ComparesExtractedVersions(
        string localVersion,
        string remoteVersion,
        bool expected)
    {
        Assert.Equal(expected, _service.IsRemoteNewer(localVersion, remoteVersion));
    }

    [Fact]
    public void IsRemoteNewer_TreatsMissingInstallationAsOutdated()
    {
        Assert.True(_service.IsRemoteNewer("Not Installed", "156.0.8078.74"));
    }

    [Fact]
    public void ThoriumCandidatePaths_UseThoriumExecutableName()
    {
        string[] paths = LocalChromiumService.GetCandidatePaths(ChromiumFlavor.Thorium);

        Assert.All(paths, path =>
        {
            Assert.DoesNotContain('\t', path);
            Assert.EndsWith(Path.Combine("Thorium", "Application", "thorium.exe"), path);
        });
    }
}
