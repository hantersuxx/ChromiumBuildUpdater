using ChromiumBuildUpdater.Core.Services;

namespace ChromiumBuildUpdater.Tests;

public class SelfUpdateLauncherTests
{
    [Fact]
    public void IsApplyRequest_OnlyAcceptsTheExactHelperSwitch()
    {
        Assert.True(SelfUpdateLauncher.IsApplyRequest(["--apply-update", "123"]));
        Assert.False(SelfUpdateLauncher.IsApplyRequest(["--Apply-Update", "123"]));
        Assert.False(SelfUpdateLauncher.IsApplyRequest(["--cleanup-update-helper"]));
    }

    [Fact]
    public void TryGetUpdateFailureMessage_ReadsOnlyTheExpectedArgumentShape()
    {
        Assert.True(SelfUpdateLauncher.TryGetUpdateFailureMessage(
            ["--update-failed", "Access denied"], out string message));
        Assert.Equal("Access denied", message);
        Assert.False(SelfUpdateLauncher.TryGetUpdateFailureMessage(
            ["--update-failed", "message", "extra"], out _));
    }

    [Fact]
    public void RunApplyRequest_RejectsPathsOutsideItsStagingContract()
    {
        Assert.Equal(2, SelfUpdateLauncher.RunApplyRequest(
            ["--apply-update", "123", "relative-stage", "relative-target", "relative-directory"]));
    }
}
