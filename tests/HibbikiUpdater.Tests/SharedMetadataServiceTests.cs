using HibbikiUpdater.Services;

namespace HibbikiUpdater.Tests;

public class SharedMetadataServiceTests
{
    [Theory]
    [InlineData("v156.0.8078.74", "156.0.8078.74")]
    [InlineData("M156.0.8078.74-beta1", "156.0.8078.74")]
    [InlineData("156.0.8078.74", "156.0.8078.74")]
    public void NormalizeReleaseVersion_UsesCanonicalNumericVersion(string input, string expected)
    {
        Assert.Equal(expected, VersionService.NormalizeReleaseVersion(input));
    }

    [Fact]
    public void IsValidSha256_AcceptsPrefixedHexDigest()
    {
        Assert.True(ChecksumService.IsValidSha256($"sha256:{new string('a', 64)}"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("sha256:bad")]
    public void IsValidSha256_RejectsMissingOrMalformedDigest(string? checksum)
    {
        Assert.False(ChecksumService.IsValidSha256(checksum));
    }

    [Fact]
    public void ExtractSha256_PrefersValidAssetDigest()
    {
        string digest = new('a', 64);

        string? result = ChecksumService.ExtractSha256(
            $"sha256:{digest}",
            $"A different checksum: {new string('b', 64)}");

        Assert.Equal(digest, result);
    }

    [Fact]
    public void ExtractSha256_FallsBackToReleaseBody()
    {
        string digest = new('b', 64);

        string? result = ChecksumService.ExtractSha256("invalid", $"SHA-256: {digest}");

        Assert.Equal(digest, result);
    }
}
