using HibbikiUpdater.Models;
using HibbikiUpdater.Services;

namespace HibbikiUpdater.Tests;

public class InstallerServiceTests
{
    private const string ValidChecksum = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public void ValidateArtifact_RequiresHttps()
    {
        ReleaseArtifact artifact = CreateArtifact(downloadUrl: "http://example.test/installer.exe");

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => InstallerService.ValidateArtifact(artifact));

        Assert.Contains("HTTPS", error.Message);
    }

    [Fact]
    public void ValidateArtifact_RequiresChecksum()
    {
        ReleaseArtifact artifact = CreateArtifact(checksum: null);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => InstallerService.ValidateArtifact(artifact));

        Assert.Contains("does not provide", error.Message);
    }

    [Fact]
    public void ValidateArtifact_RejectsMalformedChecksum()
    {
        ReleaseArtifact artifact = CreateArtifact(checksum: "not-a-sha256");

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => InstallerService.ValidateArtifact(artifact));

        Assert.Contains("invalid SHA-256", error.Message);
    }

    [Fact]
    public void ValidateArtifact_AcceptsPrefixedSha256AndReturnsUri()
    {
        ReleaseArtifact artifact = CreateArtifact(checksum: $"sha256:{ValidChecksum}");

        Uri uri = InstallerService.ValidateArtifact(artifact);

        Assert.Equal("https://example.test/installer.exe", uri.AbsoluteUri);
    }

    private static ReleaseArtifact CreateArtifact(
        string downloadUrl = "https://example.test/installer.exe",
        string? checksum = ValidChecksum) =>
        new(
            ChromiumFlavor.Hibbiki,
            "156.0.8078.74",
            "156.0.8078.74",
            DateTime.UtcNow,
            downloadUrl,
            checksum,
            "Test feed");
}
