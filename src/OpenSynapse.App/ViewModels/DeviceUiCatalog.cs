using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Lighting;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

internal static class DeviceUiCatalog
{
    public static readonly BladePerformanceMode[] BladePerformanceModes =
    [
        BladePerformanceMode.Balanced,
        BladePerformanceMode.Performance,
        BladePerformanceMode.Custom,
        BladePerformanceMode.Silent,
        BladePerformanceMode.Hyperboost,
    ];

    public static readonly int[] BladeChargeLimits = [50, 55, 60, 65, 70, 75, 80, 100];

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
    ];

    public static readonly BladeWaveDirection[] BladeWaveDirections =
        [BladeWaveDirection.Right, BladeWaveDirection.Left];
}
