using System.Runtime.InteropServices;
using ChromiumBuildUpdater.Core.Models;
using ChromiumBuildUpdater.Core.Services;

namespace ChromiumBuildUpdater.Tests;

public class PlatformSupportTests
{
    [Theory]
    [InlineData(HostPlatform.Windows, ChromiumFlavor.Hibbiki, true)]
    [InlineData(HostPlatform.Windows, ChromiumFlavor.UngoogledChromium, true)]
    [InlineData(HostPlatform.Windows, ChromiumFlavor.Thorium, true)]
    [InlineData(HostPlatform.MacOS, ChromiumFlavor.Hibbiki, false)]
    [InlineData(HostPlatform.MacOS, ChromiumFlavor.UngoogledChromium, true)]
    [InlineData(HostPlatform.MacOS, ChromiumFlavor.Thorium, false)]
    [InlineData(HostPlatform.Linux, ChromiumFlavor.Hibbiki, false)]
    [InlineData(HostPlatform.Linux, ChromiumFlavor.UngoogledChromium, false)]
    [InlineData(HostPlatform.Linux, ChromiumFlavor.Thorium, true)]
    public void IsFlavorAvailable_ReflectsVerifiedFeedCoverage(
        HostPlatform platform,
        ChromiumFlavor flavor,
        bool expected)
    {
        Assert.Equal(expected, PlatformSupport.IsFlavorAvailable(flavor, platform));
    }

    [Theory]
    [InlineData(Architecture.X64, true, "AVX2")]
    [InlineData(Architecture.X64, false, "SSE4")]
    [InlineData(Architecture.Arm64, false, "arm64")]
    public void GetThoriumLinuxAssetSuffix_SelectsCompatibleAsset(
        Architecture architecture,
        bool supportsAvx2,
        string expected)
    {
        Assert.Equal(expected, PlatformSupport.GetThoriumLinuxAssetSuffix(architecture, supportsAvx2));
    }
}
