using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using ChromiumBuildUpdater.Core.Models;
using ChromiumBuildUpdater.Core.Services;

namespace ChromiumBuildUpdater.Tests;

public class ReleaseProviderServiceTests
{
    [Fact]
    public async Task FetchLatestReleaseAsync_ParsesGitHubReleaseAndDigest()
    {
        const string checksum = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        const string downloadUrl = "https://github.com/Hibbiki/chromium-win64/releases/download/v156.0.8078.74/mini_installer.exe";
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                $$"""
                {
                  "tag_name": "v156.0.8078.74",
                  "published_at": "2026-10-28T23:19:00Z",
                  "body": "SHA-256: {{checksum}}",
                  "assets": [
                    {
                      "name": "mini_installer.exe",
                      "browser_download_url": "{{downloadUrl}}",
                      "digest": null,
                      "size": 1234
                    }
                  ]
                }
                """,
                Encoding.UTF8,
                "application/json")
        });
        using var httpClient = new HttpClient(handler);
        var service = new ReleaseProviderService(httpClient, HostPlatform.Windows, Architecture.X64, true);

        ReleaseArtifact artifact = await service.FetchLatestReleaseAsync(ChromiumFlavor.Hibbiki);

        Assert.Equal(ChromiumFlavor.Hibbiki, artifact.Flavor);
        Assert.Equal("156.0.8078.74", artifact.CleanVersion);
        Assert.Equal(downloadUrl, artifact.DownloadUrl);
        Assert.Equal(checksum, artifact.ExpectedSha256);
        Assert.Equal("GitHub (Hibbiki/chromium-win64)", artifact.SourceName);
        Assert.Equal(
            "https://api.github.com/repos/Hibbiki/chromium-win64/releases/latest",
            handler.LastRequestUri?.AbsoluteUri);
    }

    [Fact]
    public async Task FetchLatestReleaseAsync_FallsBackToChromiumBuildsWhenGitHubFails()
    {
        const string checksum = "abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789";
        const string githubUrl = "https://api.github.com/repos/Hibbiki/chromium-win64/releases/latest";
        const string fallbackUrl = "https://chromiumbuilds.org/api/v1/windows.json";
        const string downloadUrl = "https://downloads.example.test/installer.exe";

        var handler = new StubHttpMessageHandler(request =>
        {
            if (request.RequestUri?.AbsoluteUri == githubUrl)
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    $$"""
                    [
                      {
                        "id": "hibbiki-chromium",
                        "arch": "x64",
                        "version": "v156.0.8078.74",
                        "updated": "2026-10-28T23:19:00+02:00",
                        "downloads": [
                          {
                            "url": "{{downloadUrl}}",
                            "sha256": "{{checksum}}"
                          }
                        ]
                      }
                    ]
                    """,
                    Encoding.UTF8,
                    "application/json")
            };
        });
        using var httpClient = new HttpClient(handler);
        var service = new ReleaseProviderService(httpClient, HostPlatform.Windows, Architecture.X64, true);

        ReleaseArtifact artifact = await service.FetchLatestReleaseAsync(ChromiumFlavor.Hibbiki);

        Assert.Equal("156.0.8078.74", artifact.CleanVersion);
        Assert.Equal(new DateTime(2026, 10, 28, 21, 19, 0, DateTimeKind.Utc), artifact.PublishedAtUtc);
        Assert.Equal(downloadUrl, artifact.DownloadUrl);
        Assert.Equal(checksum, artifact.ExpectedSha256);
        Assert.Equal("Failover (chromiumbuilds.org/api/v1/windows.json)", artifact.SourceName);
        Assert.Collection(
            handler.RequestUris,
            uri => Assert.Equal(githubUrl, uri.AbsoluteUri),
            uri => Assert.Equal(fallbackUrl, uri.AbsoluteUri));
    }

    [Fact]
    public async Task FetchLatestReleaseAsync_UsesLatestFeedWhenWindowsFeedHasNoMatchingBuild()
    {
        const string latestUrl = "https://chromiumbuilds.org/api/v1/latest.json";
        const string downloadUrl = "https://downloads.example.test/installer.exe";
        const string checksum = "abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789";
        var handler = new StubHttpMessageHandler(request =>
        {
            if (request.RequestUri?.AbsoluteUri == latestUrl)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        $$"""[{"name":"hibbiki-chromium","version":"156.0.8078.74","url":"{{downloadUrl}}","sha256":"{{checksum}}"}]""",
                        Encoding.UTF8,
                        "application/json")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"builds":[]}""", Encoding.UTF8, "application/json")
            };
        });
        using var httpClient = new HttpClient(handler);
        var service = new ReleaseProviderService(httpClient, HostPlatform.Windows, Architecture.X64, true);

        ReleaseArtifact artifact = await service.FetchLatestReleaseAsync(
            ChromiumFlavor.Hibbiki,
            preferChromiumBuildsOrg: true);

        Assert.Equal(downloadUrl, artifact.DownloadUrl);
        Assert.Equal(checksum, artifact.ExpectedSha256);
        Assert.Equal("Failover (chromiumbuilds.org/api/v1/latest.json)", artifact.SourceName);
        Assert.Collection(
            handler.RequestUris,
            uri => Assert.Equal("https://chromiumbuilds.org/api/v1/windows.json", uri.AbsoluteUri),
            uri => Assert.Equal(latestUrl, uri.AbsoluteUri));
    }

    [Fact]
    public async Task FetchLatestReleaseAsync_IncludesAllFallbackErrorsInFinalException()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(
            _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        var service = new ReleaseProviderService(httpClient, HostPlatform.Windows, Architecture.X64, true);

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.FetchLatestReleaseAsync(ChromiumFlavor.Hibbiki));

        Assert.Contains("chromiumbuilds.org/api/v1/windows.json", error.Message);
        Assert.Contains("chromiumbuilds.org/api/v1/latest.json", error.Message);
        Assert.Contains("GitHub API", error.Message);
    }

    [Fact]
    public async Task FetchLatestReleaseAsync_ReportsEachFailedThoriumFeed()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(
            _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        var service = new ReleaseProviderService(httpClient, HostPlatform.Windows, Architecture.X64, true);

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.FetchLatestReleaseAsync(ChromiumFlavor.Thorium));

        Assert.Contains("gz83/thorium", error.Message);
        Assert.Contains("Alex313031/Thorium-Win", error.Message);
    }

    [Fact]
    public async Task FetchLatestReleaseAsync_SelectsNativeUngoogledMacAsset()
    {
        const string checksum = "1111111111111111111111111111111111111111111111111111111111111111";
        const string arm64Url = "https://github.com/example/releases/download/mac/ungoogled_arm64-macos.dmg";
        var handler = CreateReleaseHandler(
            "156.0.8078.47-1.1",
            ("ungoogled_x86_64-macos.dmg", "https://example.test/x64.dmg", $"sha256:{checksum}"),
            ("ungoogled_arm64-macos.dmg", arm64Url, $"sha256:{checksum}"));
        using var httpClient = new HttpClient(handler);
        var service = new ReleaseProviderService(httpClient, HostPlatform.MacOS, Architecture.Arm64, false);

        ReleaseArtifact artifact = await service.FetchLatestReleaseAsync(ChromiumFlavor.UngoogledChromium);

        Assert.Equal("ungoogled_arm64-macos.dmg", artifact.AssetName);
        Assert.Equal(arm64Url, artifact.DownloadUrl);
        Assert.Equal(checksum, artifact.ExpectedSha256);
        Assert.Equal(
            "https://api.github.com/repos/ungoogled-software/ungoogled-chromium-macos/releases/latest",
            handler.LastRequestUri?.AbsoluteUri);
    }

    [Fact]
    public async Task FetchLatestReleaseAsync_SelectsCompatibleThoriumLinuxAppImage()
    {
        const string checksum = "2222222222222222222222222222222222222222222222222222222222222222";
        const string appImageUrl = "https://github.com/example/releases/download/linux/Thorium_AVX2.AppImage";
        var handler = CreateReleaseHandler(
            "M156.0.8078.28",
            ("Thorium_Browser_156.0.8078.28_SSE4.AppImage", "https://example.test/sse4.AppImage", $"sha256:{checksum}"),
            ("Thorium_Browser_156.0.8078.28_AVX2.AppImage", appImageUrl, $"sha256:{checksum}"));
        using var httpClient = new HttpClient(handler);
        var service = new ReleaseProviderService(httpClient, HostPlatform.Linux, Architecture.X64, true);

        ReleaseArtifact artifact = await service.FetchLatestReleaseAsync(ChromiumFlavor.Thorium);

        Assert.Equal("Thorium_Browser_156.0.8078.28_AVX2.AppImage", artifact.AssetName);
        Assert.Equal(appImageUrl, artifact.DownloadUrl);
        Assert.Equal(checksum, artifact.ExpectedSha256);
        Assert.Contains("beta", artifact.SourceName, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(HostPlatform.Windows, ChromiumFlavor.Hibbiki, Architecture.X64, false, "mini_installer.exe")]
    [InlineData(HostPlatform.Windows, ChromiumFlavor.UngoogledChromium, Architecture.X64, false, "ungoogled_installer_x64.exe")]
    [InlineData(HostPlatform.Windows, ChromiumFlavor.Thorium, Architecture.X64, true, "thorium_mini_installer_AVX2.exe")]
    [InlineData(HostPlatform.Windows, ChromiumFlavor.Thorium, Architecture.X64, false, "thorium_mini_installer_SSE4.exe")]
    [InlineData(HostPlatform.MacOS, ChromiumFlavor.UngoogledChromium, Architecture.X64, false, "ungoogled_x86_64-macos.dmg")]
    [InlineData(HostPlatform.MacOS, ChromiumFlavor.UngoogledChromium, Architecture.Arm64, false, "ungoogled_arm64-macos.dmg")]
    [InlineData(HostPlatform.Linux, ChromiumFlavor.Thorium, Architecture.X64, true, "Thorium_AVX2.AppImage")]
    [InlineData(HostPlatform.Linux, ChromiumFlavor.Thorium, Architecture.X64, false, "Thorium_SSE4.AppImage")]
    [InlineData(HostPlatform.Linux, ChromiumFlavor.Thorium, Architecture.Arm64, false, "Thorium_arm64.AppImage")]
    public async Task FeedContract_SelectsAssetMatchingSupportedPlatformAndArchitecture(
        HostPlatform platform,
        ChromiumFlavor flavor,
        Architecture architecture,
        bool supportsAvx2,
        string expectedAsset)
    {
        var assets = new[]
        {
            ("mini_installer.exe", "https://example.test/mini_installer.exe", "sha256:" + new string('a', 64)),
            ("ungoogled_installer_x64.exe", "https://example.test/ungoogled_installer_x64.exe", "sha256:" + new string('a', 64)),
            ("ungoogled_installer_arm64.exe", "https://example.test/ungoogled_installer_arm64.exe", "sha256:" + new string('a', 64)),
            ("thorium_mini_installer_AVX2.exe", "https://example.test/thorium_mini_installer_AVX2.exe", "sha256:" + new string('a', 64)),
            ("thorium_mini_installer_SSE4.exe", "https://example.test/thorium_mini_installer_SSE4.exe", "sha256:" + new string('a', 64)),
            ("ungoogled_x86_64-macos.dmg", "https://example.test/ungoogled_x86_64-macos.dmg", "sha256:" + new string('a', 64)),
            ("ungoogled_arm64-macos.dmg", "https://example.test/ungoogled_arm64-macos.dmg", "sha256:" + new string('a', 64)),
            ("Thorium_AVX2.AppImage", "https://example.test/Thorium_AVX2.AppImage", "sha256:" + new string('a', 64)),
            ("Thorium_SSE4.AppImage", "https://example.test/Thorium_SSE4.AppImage", "sha256:" + new string('a', 64)),
            ("Thorium_arm64.AppImage", "https://example.test/Thorium_arm64.AppImage", "sha256:" + new string('a', 64))
        };
        using var httpClient = new HttpClient(CreateReleaseHandler("156.0.8078.74", assets));
        var service = new ReleaseProviderService(httpClient, platform, architecture, supportsAvx2);

        ReleaseArtifact artifact = await service.FetchLatestReleaseAsync(flavor);

        Assert.Equal(expectedAsset, artifact.AssetName);
    }

    [Fact]
    public async Task FetchLatestReleaseAsync_RejectsFlavorWithoutVerifiedPlatformFeed()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(
            _ => throw new InvalidOperationException("The HTTP handler should not be called.")));
        var service = new ReleaseProviderService(httpClient, HostPlatform.Linux, Architecture.X64, true);

        await Assert.ThrowsAsync<PlatformNotSupportedException>(
            () => service.FetchLatestReleaseAsync(ChromiumFlavor.UngoogledChromium));
    }

    private static StubHttpMessageHandler CreateReleaseHandler(
        string tag,
        params (string Name, string Url, string Digest)[] assets)
    {
        string assetJson = string.Join(
            ",",
            assets.Select(asset =>
                $$"""{"name":"{{asset.Name}}","browser_download_url":"{{asset.Url}}","digest":"{{asset.Digest}}","size":100}"""));
        return new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                $$"""{"tag_name":"{{tag}}","published_at":"2026-10-28T23:19:00Z","body":"","assets":[{{assetJson}}]}""",
                Encoding.UTF8,
                "application/json")
        });
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public Uri? LastRequestUri { get; private set; }
        public List<Uri> RequestUris { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            RequestUris.Add(request.RequestUri!);
            return Task.FromResult(responseFactory(request));
        }
    }
}
