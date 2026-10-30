using ChromiumBuildUpdater.Core.Models;

namespace ChromiumBuildUpdater.Core.Services;

public sealed class UpdateCheckService
{
    private readonly IReleaseProvider _releaseProvider;
    private readonly IInstalledBuildService _installedBuildService;
    private readonly ConfigService _configService;

    public UpdateCheckService(
        IReleaseProvider releaseProvider,
        IInstalledBuildService installedBuildService,
        ConfigService configService)
    {
        _releaseProvider = releaseProvider ?? throw new ArgumentNullException(nameof(releaseProvider));
        _installedBuildService = installedBuildService
            ?? throw new ArgumentNullException(nameof(installedBuildService));
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
    }

    public async Task<UpdateCheckResult> CheckAsync(
        ChromiumFlavor flavor,
        CancellationToken cancellationToken = default)
    {
        ReleaseArtifact artifact = await _releaseProvider.FetchLatestReleaseAsync(
            flavor,
            _configService.Current.PreferChromiumBuildsOrgApi,
            cancellationToken);

        string installedVersion = _installedBuildService.GetInstalledVersion(flavor);
        string releaseDateText = artifact.PublishedAtUtc.ToLocalTime().ToString("d");
        string lastCheckTime = DateTime.Now.ToString("g");

        _configService.Current.LastKnownRemoteVersion = artifact.CleanVersion;
        _configService.Current.LastKnownRemoteDate = releaseDateText;
        _configService.Current.LastKnownSource = artifact.SourceName;
        _configService.Current.LastCheckTime = lastCheckTime;
        _configService.Save();

        return new UpdateCheckResult(
            artifact,
            installedVersion,
            _installedBuildService.IsRemoteNewer(installedVersion, artifact.CleanVersion),
            releaseDateText,
            lastCheckTime,
            _configService.LastError);
    }
}

public sealed record UpdateCheckResult(
    ReleaseArtifact Artifact,
    string InstalledVersion,
    bool UpdateAvailable,
    string ReleaseDateText,
    string LastCheckTime,
    string? SettingsError);
