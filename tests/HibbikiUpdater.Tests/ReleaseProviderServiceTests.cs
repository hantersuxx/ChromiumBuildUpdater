using System.Net;
using System.Text;
using HibbikiUpdater.Models;
using HibbikiUpdater.Services;

namespace HibbikiUpdater.Tests;

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
        var service = new ReleaseProviderService(httpClient);

        ReleaseArtifact artifact = await service.FetchLatestReleaseAsync(ChromiumFlavor.Hibbiki);

        Assert.Equal(ChromiumFlavor.Hibbiki, artifact.Flavor);
        Assert.Equal("156.0.8078.74", artifact.CleanVersion);
        Assert.Equal(downloadUrl, artifact.DownloadUrl);
        Assert.Equal(checksum, artifact.ExpectedSha256);
        Assert.Equal("GitHub (Hibbiki/chromium-win64)", artifact.SourceName);
        Assert.Equal("https://api.github.com/repos/Hibbiki/chromium-win64/releases/latest", handler.LastRequestUri?.AbsoluteUri);
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public Uri? LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            return Task.FromResult(responseFactory(request));
        }
    }
}
