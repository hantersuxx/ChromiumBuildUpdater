using ChromiumBuildUpdater.Core.Services;

namespace ChromiumBuildUpdater.Tests;

public class VersionServiceTests
{
    [Theory]
    [InlineData("156.0.8078.74", "156.0.8078.75", true)]
    [InlineData("Chromium 156.0.8078.74 Official Build", "v156.0.8078.75", true)]
    [InlineData("156.0.8078.74", "156.0.8078.74", false)]
    [InlineData("156.0.8078.75", "156.0.8078.74", false)]
    [InlineData("unknown version", "156.0.8078.74", false)]
    [InlineData("156.0.8078.74", "unknown version", false)]
    public void IsRemoteNewer_ComparesValidNumericVersions(
        string localVersion,
        string remoteVersion,
        bool expected)
    {
        Assert.Equal(expected, VersionService.IsRemoteNewer(localVersion, remoteVersion));
    }

    [Theory]
    [InlineData("Not Installed")]
    [InlineData("Unknown")]
    public void IsRemoteNewer_TreatsUnknownInstalledVersionAsNeedingUpdate(string localVersion)
    {
        Assert.True(VersionService.IsRemoteNewer(localVersion, "156.0.8078.74"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NormalizeReleaseVersion_UsesZeroVersionForMissingTag(string? tag)
    {
        Assert.Equal("0.0.0.0", VersionService.NormalizeReleaseVersion(tag!));
    }
}
