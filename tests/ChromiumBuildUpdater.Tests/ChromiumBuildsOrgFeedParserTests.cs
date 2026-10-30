using System.Text.Json;
using ChromiumBuildUpdater.Core.Models;
using ChromiumBuildUpdater.Core.Services;

namespace ChromiumBuildUpdater.Tests;

public class ChromiumBuildsOrgFeedParserTests
{
    [Fact]
    public void TryParse_HandlesBuildsArrayAndSelectsX64Installer()
    {
        const string checksum = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        using JsonDocument document = JsonDocument.Parse(
            $$"""
            {
              "builds": [
                {
                  "name": "hibbiki-chromium",
                  "arch": "x64",
                  "version": "v156.0.8078.74",
                  "published_at": "2026-10-28T23:19:00+02:00",
                  "downloads": [
                    { "url": "https://example.test/arm64-installer.exe", "sha256": "{{checksum}}" },
                    { "url": "https://example.test/installer-32bit.exe", "sha256": "{{checksum}}" },
                    { "url": "https://example.test/installer-x64.exe", "digest": "{{checksum}}" }
                  ]
                }
              ]
            }
            """);

        ReleaseArtifact? artifact = ChromiumBuildsOrgFeedParser.TryParse(
            document.RootElement,
            ChromiumFlavor.Hibbiki,
            "hibbiki",
            "https://chromiumbuilds.org/api/v1/windows.json");

        Assert.NotNull(artifact);
        Assert.Equal("156.0.8078.74", artifact.CleanVersion);
        Assert.Equal("https://example.test/installer-x64.exe", artifact.DownloadUrl);
        Assert.Equal(checksum, artifact.ExpectedSha256);
        Assert.Equal(
            new DateTime(2026, 10, 28, 21, 19, 0, DateTimeKind.Utc),
            artifact.PublishedAtUtc);
        Assert.Equal(
            "Failover (chromiumbuilds.org/api/v1/windows.json)",
            artifact.SourceName);
    }

    [Fact]
    public void TryParse_HandlesFlatLatestFeedRowsAndChecksumAliases()
    {
        const string checksum = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        using JsonDocument document = JsonDocument.Parse(
            $$"""
            [
              {
                "id": "ungoogled-chromium",
                "version": "M156.0.8078.74-beta1",
                "date": "2026-10-28T23:19:00Z",
                "download_url": "https://example.test/ungoogled-installer.exe",
                "checksum": "sha256:{{checksum}}"
              }
            ]
            """);

        ReleaseArtifact? artifact = ChromiumBuildsOrgFeedParser.TryParse(
            document.RootElement,
            ChromiumFlavor.UngoogledChromium,
            "ungoogled",
            "https://chromiumbuilds.org/api/v1/latest.json");

        Assert.NotNull(artifact);
        Assert.Equal("156.0.8078.74", artifact.CleanVersion);
        Assert.Equal(checksum, artifact.ExpectedSha256);
        Assert.Equal(
            "Failover (chromiumbuilds.org/api/v1/latest.json)",
            artifact.SourceName);
    }

    [Fact]
    public void TryParse_HandlesNamedObjectRows()
    {
        using JsonDocument document = JsonDocument.Parse(
            """
            {
              "current": {
                "slug": "thorium",
                "arch": "64-bit",
                "tag": "M156.0.8078.28",
                "installer_url": "https://example.test/thorium.exe",
                "hash": "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc"
              }
            }
            """);

        ReleaseArtifact? artifact = ChromiumBuildsOrgFeedParser.TryParse(
            document.RootElement,
            ChromiumFlavor.Thorium,
            "thorium",
            "https://chromiumbuilds.org/api/v1/latest.json");

        Assert.NotNull(artifact);
        Assert.Equal("156.0.8078.28", artifact.CleanVersion);
        Assert.Equal("https://example.test/thorium.exe", artifact.DownloadUrl);
    }

    [Fact]
    public void TryParse_RejectsRowsWithUnsupportedArchitecture()
    {
        using JsonDocument document = JsonDocument.Parse(
            """
            [
              {
                "name": "hibbiki",
                "arch": "arm64",
                "version": "156.0.8078.74",
                "url": "https://example.test/installer.exe"
              }
            ]
            """);

        ReleaseArtifact? artifact = ChromiumBuildsOrgFeedParser.TryParse(
            document.RootElement,
            ChromiumFlavor.Hibbiki,
            "hibbiki",
            "https://chromiumbuilds.org/api/v1/latest.json");

        Assert.Null(artifact);
    }

    [Fact]
    public void TryParse_RejectsArm64FlatDownloadWhenArchitectureIsNotDeclared()
    {
        using JsonDocument document = JsonDocument.Parse(
            """
            [
              {
                "name": "hibbiki",
                "version": "156.0.8078.74",
                "url": "https://example.test/arm64-installer.exe"
              }
            ]
            """);

        ReleaseArtifact? artifact = ChromiumBuildsOrgFeedParser.TryParse(
            document.RootElement,
            ChromiumFlavor.Hibbiki,
            "hibbiki",
            "https://chromiumbuilds.org/api/v1/latest.json");

        Assert.Null(artifact);
    }
}
