using System;
using System.Text.Json.Serialization;

namespace HibbikiUpdater.Models;

public record GitHubReleaseResponse(
    [property: JsonPropertyName("tag_name")] string TagName,
    [property: JsonPropertyName("published_at")] DateTime PublishedAt,
    [property: JsonPropertyName("body")] string? Body,
    [property: JsonPropertyName("assets")] GitHubAssetResponse[] Assets
);

public record GitHubAssetResponse(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("browser_download_url")] string BrowserDownloadUrl,
    [property: JsonPropertyName("digest")] string? Digest,
    [property: JsonPropertyName("size")] long Size
);
