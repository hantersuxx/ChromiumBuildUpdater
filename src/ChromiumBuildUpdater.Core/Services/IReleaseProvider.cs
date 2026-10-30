using ChromiumBuildUpdater.Core.Models;

namespace ChromiumBuildUpdater.Core.Services;

public interface IReleaseProvider
{
    Task<ReleaseArtifact> FetchLatestReleaseAsync(
        ChromiumFlavor flavor,
        bool preferChromiumBuildsOrg = false,
        CancellationToken ct = default);
}
