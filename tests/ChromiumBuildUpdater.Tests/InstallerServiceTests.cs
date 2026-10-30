using System.Net;
using System.Security.Cryptography;
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
        var installer = new RecordingInstallStrategy();
        using var httpClient = new HttpClient(new StubHttpMessageHandler(
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("this is not the expected installer")
            }));
        var service = new InstallerService(httpClient, HostPlatform.Windows, installer);
        ReleaseArtifact artifact = CreateArtifact();

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.DownloadVerifyAndInstallAsync(artifact, new EmptyProgress()));

        Assert.Contains("checksum mismatch", error.Message);
        Assert.Equal(0, installer.InvocationCount);
    }

    [Fact]
    public async Task DownloadVerifyAndInstallAsync_PassesVerifiedBytesToPlatformInstallerAndCleansUp()
    {
        byte[] payload = "verified browser installer"u8.ToArray();
        string checksum = Convert.ToHexString(SHA256.HashData(payload));
        var installer = new RecordingInstallStrategy();
        using var httpClient = new HttpClient(new StubHttpMessageHandler(
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(payload)
            }));
        var service = new InstallerService(httpClient, HostPlatform.Windows, installer);
        ReleaseArtifact artifact = CreateArtifact(checksum: checksum);

        await service.DownloadVerifyAndInstallAsync(artifact, new EmptyProgress());

        Assert.Equal(1, installer.InvocationCount);
        Assert.Equal(payload, installer.InstalledBytes);
        Assert.Equal(HostPlatform.Windows, installer.Platform);
        Assert.NotNull(installer.ArtifactPath);
        Assert.False(File.Exists(installer.ArtifactPath));
    }

    [Fact]
    public async Task DownloadVerifyAndInstallAsync_RejectsHttpsRedirectToHttp()
    {
        var installer = new RecordingInstallStrategy();
        using var httpClient = new HttpClient(new StubHttpMessageHandler(
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = new HttpRequestMessage(
                    HttpMethod.Get,
                    "http://insecure.example.test/installer.exe"),
                Content = new StringContent("installer")
            }));
        var service = new InstallerService(httpClient, HostPlatform.Windows, installer);

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.DownloadVerifyAndInstallAsync(
                CreateArtifact(),
                new EmptyProgress()));

        Assert.Contains("non-HTTPS", error.Message);
        Assert.Equal(0, installer.InvocationCount);
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

    private sealed class RecordingInstallStrategy : IArtifactInstallStrategy
    {
        public int InvocationCount { get; private set; }
        public byte[]? InstalledBytes { get; private set; }
        public string? ArtifactPath { get; private set; }
        public HostPlatform Platform { get; private set; }

        public Task InstallAsync(
            ReleaseArtifact artifact,
            string artifactPath,
            IProgress<(int Percent, string StatusText)> progress,
            HostPlatform platform,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            InvocationCount++;
            ArtifactPath = artifactPath;
            InstalledBytes = File.ReadAllBytes(artifactPath);
            Platform = platform;
            return Task.CompletedTask;
        }
    }
}
