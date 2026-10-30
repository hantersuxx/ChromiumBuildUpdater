using ChromiumBuildUpdater.Core.Models;

namespace ChromiumBuildUpdater.Core.Services;

public interface IInstalledBuildService
{
    string GetInstalledVersion(ChromiumFlavor flavor);
    bool IsRemoteNewer(string localVersion, string remoteVersion);
}
