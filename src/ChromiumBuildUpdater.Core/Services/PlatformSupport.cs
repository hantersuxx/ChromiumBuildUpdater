using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;
using ChromiumBuildUpdater.Core.Models;

namespace ChromiumBuildUpdater.Core.Services;

public enum HostPlatform
{
    Windows,
    MacOS,
    Linux,
    Unsupported
}

public static class PlatformSupport
{
    public static HostPlatform Current =>
        OperatingSystem.IsWindows() ? HostPlatform.Windows :
        OperatingSystem.IsMacOS() ? HostPlatform.MacOS :
        OperatingSystem.IsLinux() ? HostPlatform.Linux :
        HostPlatform.Unsupported;

    public static Architecture CurrentArchitecture => RuntimeInformation.ProcessArchitecture;

    public static IReadOnlyList<ChromiumFlavor> GetAvailableFlavors(HostPlatform platform) =>
        platform switch
        {
            HostPlatform.Windows => [ChromiumFlavor.Hibbiki, ChromiumFlavor.UngoogledChromium, ChromiumFlavor.Thorium],
            HostPlatform.MacOS => [ChromiumFlavor.UngoogledChromium],
            HostPlatform.Linux => [ChromiumFlavor.Thorium],
            _ => []
        };

    public static bool IsFlavorAvailable(ChromiumFlavor flavor, HostPlatform platform) =>
        GetAvailableFlavors(platform).Contains(flavor);

    public static string GetThoriumLinuxAssetSuffix(Architecture architecture, bool supportsAvx2) =>
        architecture switch
        {
            Architecture.Arm64 => "arm64",
            Architecture.X64 when supportsAvx2 => "AVX2",
            Architecture.X64 => "SSE4",
            _ => throw new PlatformNotSupportedException(
                $"Thorium Linux builds are not available for {architecture}.")
        };

    public static string GetThoriumWindowsAssetSuffix(bool supportsAvx2) =>
        supportsAvx2 ? "AVX2" : "SSE4";

    public static string GetUngoogledMacAssetSuffix(Architecture architecture) =>
        architecture switch
        {
            Architecture.Arm64 => "arm64",
            Architecture.X64 => "x86_64",
            _ => throw new PlatformNotSupportedException(
                $"Ungoogled Chromium macOS builds are not available for {architecture}.")
        };

    public static bool SupportsAvx2 => Avx2.IsSupported;
}
