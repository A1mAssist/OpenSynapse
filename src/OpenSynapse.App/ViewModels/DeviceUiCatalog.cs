using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Lighting;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

internal static class DeviceUiCatalog
{
    public static readonly BladePerformanceMode[] BladePerformanceModes =
    [
        .. BladeModelCapabilities.PerformanceModesFor(0x02C6),
    ];

    public static readonly int[] BladeChargeLimits = [50, 55, 60, 65, 70, 75, 80, 100];

    /// <summary>
    /// Every CPU level the protocol can encode. Use
    /// <see cref="BladeModelCapabilities.CpuBoostModesFor"/> for the levels one exact
    /// model admits, because the ComboBox index is shared with the apply path.
    /// </summary>
    public static readonly BladeCpuBoostMode[] BladeCpuBoostModes =
        [BladeCpuBoostMode.Low, BladeCpuBoostMode.Medium, BladeCpuBoostMode.High, BladeCpuBoostMode.Boost, BladeCpuBoostMode.Undervolt];

    public static readonly BladeGpuBoostMode[] BladeGpuBoostModes =
        [BladeGpuBoostMode.Low, BladeGpuBoostMode.Medium, BladeGpuBoostMode.High];

    public static readonly BladeLogoMode[] BladeLogoModes =
        [BladeLogoMode.Off, BladeLogoMode.Static, BladeLogoMode.Breathing];

    public static readonly BladeLightingMode[] BladeLightingModes =
    [
        BladeLightingMode.Off,
        BladeLightingMode.Static,
        BladeLightingMode.Breathing,
        BladeLightingMode.Spectrum,
        BladeLightingMode.Wave,
        BladeLightingMode.Fire,
        BladeLightingMode.Reactive,
        BladeLightingMode.Ripple,
        BladeLightingMode.AudioMeter,
        BladeLightingMode.Ambient,
        BladeLightingMode.Wheel,
        BladeLightingMode.Starlight,
        BladeLightingMode.Tidal,
        BladeLightingMode.ChromaFile,
    ];

    public static readonly BladeWaveDirection[] BladeWaveDirections =
        [BladeWaveDirection.Right, BladeWaveDirection.Left];

    public static readonly byte[] BladeReactiveSpeeds = [1, 2, 3, 4];
    public static readonly byte[] BladeStarlightSpeeds = [1, 2, 3];
    public static readonly byte[] BladeAnimationSpeeds = [1, 2, 3];
    public static readonly BladeStarlightColorMode[] BladeStarlightColorModes =
        [BladeStarlightColorMode.Random, BladeStarlightColorMode.Single, BladeStarlightColorMode.Dual];

    public static int DeviceCategoryOrder(DeviceCategory category) => category switch
    {
        DeviceCategory.Laptop => 0,
        DeviceCategory.Keyboard => 1,
        DeviceCategory.Mouse => 2,
        _ => 3,
    };
}
