using ChromiumBuildUpdater.Core.Services;

namespace ChromiumBuildUpdater.Tests;

public class ChecksumServiceTests
{
    [Fact]
    public void ExtractSha256_SelectsChecksumAssociatedWithTheRequestedAsset()
    {
        const string firstChecksum = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        const string secondChecksum = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        const string body = """
            Checksums:
            first-file.dmg
            SHA-256: aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa
            second-file.dmg
            SHA-256: bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb
            """;

        string? checksum = ChecksumService.ExtractSha256(null, body, "second-file.dmg");

        Assert.Equal(secondChecksum, checksum);
        Assert.NotEqual(firstChecksum, checksum);
    }

    [Fact]
    public void ExtractSha256_DoesNotGuessWhenMultipleUnassociatedChecksumsExist()
    {
        const string body = """
            aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa
            bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb
            """;

        Assert.Null(ChecksumService.ExtractSha256(null, body, "not-listed.dmg"));
    }
}
