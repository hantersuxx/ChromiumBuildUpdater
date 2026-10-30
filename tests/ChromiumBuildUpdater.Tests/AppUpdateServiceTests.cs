using System.Net;
using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ChromiumBuildUpdater.Core.Services;

namespace ChromiumBuildUpdater.Tests;

public class AppUpdateServiceTests
{
    [Theory]
    [InlineData(HostPlatform.Windows, Architecture.X64, "ChromiumBuildUpdater-v4.2.0-win-x64.exe")]
    [InlineData(HostPlatform.Linux, Architecture.X64, "ChromiumBuildUpdater-v4.2.0-linux-x64.tar.gz")]
    [InlineData(HostPlatform.Linux, Architecture.Arm64, "ChromiumBuildUpdater-v4.2.0-linux-arm64.tar.gz")]
    [InlineData(HostPlatform.MacOS, Architecture.X64, "ChromiumBuildUpdater-v4.2.0-osx-x64.tar.gz")]
    [InlineData(HostPlatform.MacOS, Architecture.Arm64, "ChromiumBuildUpdater-v4.2.0-osx-arm64.tar.gz")]
    public void GetAssetName_MapsSupportedRuntimeToReleaseAsset(
        HostPlatform platform,
        Architecture architecture,
        string expected)
    {
        Assert.Equal(expected, AppUpdateService.GetAssetName("v4.2.0", platform, architecture));
    }

    [Fact]
    public void ParseSha256_RequiresExactAssetAndValidDigest()
    {
        const string assetName = "ChromiumBuildUpdater-v4.2.0-win-x64.exe";
        string digest = new string('a', 64);

        Assert.Equal(digest, AppUpdateService.ParseSha256(
            $"{new string('b', 64)}  other.exe\n{digest} *{assetName}\n", assetName));
        Assert.Throws<InvalidDataException>(() =>
            AppUpdateService.ParseSha256($"{digest}  another.exe\n", assetName));
        Assert.Throws<InvalidDataException>(() =>
            AppUpdateService.ParseSha256($"not-a-hash  {assetName}\n", assetName));
    }

    [Fact]
    public async Task CheckForUpdateAsync_SelectsCurrentRidAndRequiresManifestEntry()
    {
        const string assetName = "ChromiumBuildUpdater-v3.2.0-win-x64.exe";
        const string digest = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        var handler = new StubHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/releases/latest", StringComparison.Ordinal))
            {
                string json = JsonSerializer.Serialize(new
                {
                    tag_name = "v3.2.0",
                    published_at = DateTime.UtcNow,
                    body = "",
                    assets = new[]
                    {
                        new { name = assetName, browser_download_url = "https://github.com/hantersuxx/ChromiumBuildUpdater/releases/download/v3.2.0/app.exe", digest = $"sha256:{digest}", size = 10 },
                        new { name = "SHA256SUMS.txt", browser_download_url = "https://github.com/hantersuxx/ChromiumBuildUpdater/releases/download/v3.2.0/SHA256SUMS.txt", digest = "", size = 80 }
                    }
                });
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($"{digest}  {assetName}\n")
            };
        });
        using var client = new HttpClient(handler);
        var service = new AppUpdateService(client, HostPlatform.Windows, Architecture.X64, "3.1.0");

        var update = await service.CheckForUpdateAsync();

        Assert.NotNull(update);
        Assert.Equal("3.2.0", update.Version);
        Assert.Equal(assetName, update.AssetName);
        Assert.Equal(digest, update.Sha256);
        Assert.EndsWith("/SHA256SUMS.txt", handler.RequestedUris.Last().AbsolutePath);
    }

    [Fact]
    public async Task CheckForUpdateAsync_ReturnsNullWhenAlreadyCurrent()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
                {"tag_name":"v3.1.0","published_at":"2026-01-01T00:00:00Z","body":"","assets":[]}
                """, Encoding.UTF8, "application/json")
        });
        using var client = new HttpClient(handler);
        var service = new AppUpdateService(client, HostPlatform.Windows, Architecture.X64, "3.1.0");

        Assert.Null(await service.CheckForUpdateAsync());
        Assert.Single(handler.RequestedUris);
    }

    [Fact]
    public async Task DownloadAndVerifyAsync_RejectsChecksumMismatch()
    {
        byte[] binary = Encoding.UTF8.GetBytes("untrusted app bytes");
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(binary)
        });
        using var client = new HttpClient(handler);
        var service = new AppUpdateService(client, HostPlatform.Windows, Architecture.X64, "3.1.0");
        var update = new AppUpdateRelease(
            "3.2.0",
            "ChromiumBuildUpdater-v3.2.0-win-x64.exe",
            new Uri("https://github.com/hantersuxx/ChromiumBuildUpdater/releases/download/v3.2.0/app.exe"),
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("different bytes"))));

        await Assert.ThrowsAsync<InvalidDataException>(() => service.DownloadAndVerifyAsync(update));
    }

    [Fact]
    public async Task DownloadAndVerifyAsync_ExtractsOnlyExpectedUnixExecutable()
    {
        byte[] binary = Encoding.UTF8.GetBytes("verified executable");
        string escapedFileName = $"escaped-{Guid.NewGuid():N}";
        byte[] archive = CreateArchive(binary, escapedFileName);
        string digest = Convert.ToHexString(SHA256.HashData(archive));
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(archive)
        });
        using var client = new HttpClient(handler);
        var service = new AppUpdateService(client, HostPlatform.Linux, Architecture.X64, "3.1.0");
        var update = new AppUpdateRelease(
            "3.2.0",
            "ChromiumBuildUpdater-v3.2.0-linux-x64.tar.gz",
            new Uri("https://github.com/hantersuxx/ChromiumBuildUpdater/releases/download/v3.2.0/app.tar.gz"),
            digest);

        StagedAppUpdate staged = await service.DownloadAndVerifyAsync(update);
        try
        {
            Assert.Equal(binary, await File.ReadAllBytesAsync(staged.ExecutablePath));
            Assert.False(File.Exists(Path.Combine(
                Directory.GetParent(staged.WorkingDirectory)!.FullName, escapedFileName)));
        }
        finally
        {
            Directory.Delete(staged.WorkingDirectory, recursive: true);
        }
    }

    private static byte[] CreateArchive(byte[] executable, string escapedFileName)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
        using (var writer = new TarWriter(gzip, leaveOpen: true))
        {
            writer.WriteEntry(CreateEntry($"../{escapedFileName}", Encoding.UTF8.GetBytes("do not extract")));
            writer.WriteEntry(CreateEntry("ChromiumBuildUpdater", executable));
        }

        return output.ToArray();
    }

    private static PaxTarEntry CreateEntry(string name, byte[] contents) =>
        new(TarEntryType.RegularFile, name) { DataStream = new MemoryStream(contents) };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        public List<Uri> RequestedUris { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestedUris.Add(request.RequestUri!);
            return Task.FromResult(responseFactory(request));
        }
    }
}
