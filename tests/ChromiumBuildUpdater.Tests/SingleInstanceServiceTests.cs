using ChromiumBuildUpdater.Core.Services;

namespace ChromiumBuildUpdater.Tests;

public class SingleInstanceServiceTests
{
    [Fact]
    public void TryCreate_SecondInstanceActivatesPrimaryAndIsRejected()
    {
        string applicationId = $"ChromiumBuildUpdater.Tests.{Guid.NewGuid():N}";
        using SingleInstanceService primary =
            SingleInstanceService.TryCreate(applicationId)!;
        using var activationReceived = new ManualResetEventSlim();
        primary.ActivationRequested += activationReceived.Set;

        SingleInstanceService? second = SingleInstanceService.TryCreate(applicationId);

        Assert.Null(second);
        Assert.True(activationReceived.Wait(TimeSpan.FromSeconds(3)));
    }

    [Fact]
    public void TryCreate_AllowsIndependentApplicationIds()
    {
        using SingleInstanceService first =
            SingleInstanceService.TryCreate($"ChromiumBuildUpdater.Tests.First.{Guid.NewGuid():N}")!;
        using SingleInstanceService second =
            SingleInstanceService.TryCreate($"ChromiumBuildUpdater.Tests.Second.{Guid.NewGuid():N}")!;

        Assert.NotNull(first);
        Assert.NotNull(second);
    }
}
