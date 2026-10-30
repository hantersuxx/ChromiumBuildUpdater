using System.Net;
using ChromiumBuildUpdater.Core.Models;
using ChromiumBuildUpdater.Core.Services;

namespace ChromiumBuildUpdater.Tests;

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
    public void ValidateArtifact_RejectsHttpsUrlsWithCredentials()
    {
        ReleaseArtifact artifact = CreateArtifact(
            downloadUrl: "https://user:password@example.test/installer.exe");

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => InstallerService.ValidateArtifact(artifact, HostPlatform.Windows));

        Assert.Contains("valid HTTPS URL", error.Message);
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

        Uri uri = InstallerService.ValidateArtifact(artifact, HostPlatform.Windows);

        Assert.Equal("https://example.test/installer.exe", uri.AbsoluteUri);
    }

    [Fact]
    public void ValidateArtifact_AllowsThoriumAppImageOnLinux()
    {
        ReleaseArtifact artifact = CreateArtifact(
            downloadUrl: "https://example.test/Thorium.AppImage",
            flavor: ChromiumFlavor.Thorium);

        Uri uri = InstallerService.ValidateArtifact(artifact, HostPlatform.Linux);

        Assert.EndsWith(".AppImage", uri.AbsolutePath);
    }

    [Fact]
    public void ValidateArtifact_AllowsUngoogledDiskImageOnMacOS()
    {
        ReleaseArtifact artifact = CreateArtifact(
            downloadUrl: "https://example.test/ungoogled.dmg",
            flavor: ChromiumFlavor.UngoogledChromium);

        Uri uri = InstallerService.ValidateArtifact(artifact, HostPlatform.MacOS);

        Assert.EndsWith(".dmg", uri.AbsolutePath);
    }

    [Fact]
    public void ValidateArtifact_RejectsWrongPlatformAssetType()
    {
        ReleaseArtifact artifact = CreateArtifact(
            downloadUrl: "https://example.test/Thorium.AppImage",
            flavor: ChromiumFlavor.Thorium);

        Assert.Throws<PlatformNotSupportedException>(
            () => InstallerService.ValidateArtifact(artifact, HostPlatform.Windows));
    }

    [Theory]
    [InlineData(HostPlatform.Linux, ChromiumFlavor.UngoogledChromium, "https://example.test/installer.AppImage")]
    [InlineData(HostPlatform.MacOS, ChromiumFlavor.Thorium, "https://example.test/installer.dmg")]
    public void ValidateArtifact_RejectsBuildsWithoutVerifiedPlatformSupport(
        HostPlatform platform,
        ChromiumFlavor flavor,
        string downloadUrl)
    {
        ReleaseArtifact artifact = CreateArtifact(downloadUrl, flavor: flavor);

        Assert.Throws<PlatformNotSupportedException>(
            () => InstallerService.ValidateArtifact(artifact, platform));
    }

    [Fact]
    public async Task DownloadVerifyAndInstallAsync_StopsBeforeInstallWhenChecksumDoesNotMatch()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("this is not the expected installer")
            }));
        var service = new InstallerService(httpClient, HostPlatform.Windows);
        ReleaseArtifact artifact = CreateArtifact();

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.DownloadVerifyAndInstallAsync(artifact, new EmptyProgress()));

        Assert.Contains("checksum mismatch", error.Message);
    }

    private static ReleaseArtifact CreateArtifact(
        string downloadUrl = "https://example.test/installer.exe",
        string? checksum = ValidChecksum,
        ChromiumFlavor flavor = ChromiumFlavor.Hibbiki) =>
        new(
            flavor,
            "156.0.8078.74",
            "156.0.8078.74",
            DateTime.UtcNow,
            downloadUrl,
            checksum,
            "Test feed");

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(responseFactory(request));
    }

    private sealed class EmptyProgress : IProgress<(int Percent, string StatusText)>
    {
        public void Report((int Percent, string StatusText) value)
        {
        }
    }
}
