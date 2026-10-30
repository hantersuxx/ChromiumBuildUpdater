using System;

namespace ChromiumBuildUpdater.Core.Models;

/// <summary>
/// Normalized release metadata returned by ReleaseProviderService (from GitHub primary API or chromiumbuilds.org fallback API).
/// </summary>
public record ReleaseArtifact(
    ChromiumFlavor Flavor,
    string CleanVersion,
    string FullTag,
    DateTime PublishedAtUtc,
    string DownloadUrl,
    string? ExpectedSha256,
    string SourceName,
    string? AssetName = null
);
