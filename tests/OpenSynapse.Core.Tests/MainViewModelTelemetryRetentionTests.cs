using System.Reflection;
using OpenSynapse.App.ViewModels;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Sensors;
using OpenSynapse.Windows.Devices;

namespace OpenSynapse.Core.Tests;

public sealed class MainViewModelTelemetryRetentionTests
{
    [Fact]
    public void DoesNotCountLocalDimmingForAnOledBlade()
    {
        var telemetry = new RazerDeviceTelemetry(
            255, BladePerformanceMode.Balanced, BladeFanMode.Automatic, null, 80,
            null, null, null, null, null, [], DateTimeOffset.UtcNow,
            BladeCpuBoostMode: BladeCpuBoostMode.Medium,
            BladeGpuBoostMode: BladeGpuBoostMode.Low,
            BladeMaxFanMode: BladeMaxFanMode.Disabled,
            BladeCurrentFanCpuRpm: 2500,
            BladeCurrentFanGpuRpm: 2400,
            BladeAdvancedFanCpuModeRaw: 1,
            BladeAdvancedFanGpuModeRaw: 0,
            BladeLogoMode: BladeLogoMode.Static,
            BladeGameMode: new(0, 0, 0),
            BladeStartupAnimationEnabled: true,
            BladeNativeDisplayMode: BladeNativeDisplayMode.Uhd,
            BladeSkuHardwareConfiguration: new(false, false, false, 0),
            BladeOneTimeFullChargeEnabled: false);

        var countCapabilities = typeof(DeviceRowViewModel).GetMethod(
            "CountCapabilities",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(typeof(DeviceRowViewModel).FullName, "CountCapabilities");
        var result = ((int Successful, int Total))countCapabilities.Invoke(null, ["blade-710", telemetry])!;

        Assert.Equal((17, 17), result);
    }

    [Fact]
    public void RetainsLastSuccessfulDeviceValuesWhenARefreshOmitsFields()
    {
        var viewModel = new MainViewModel(
            discovery: null!,
            deviceTelemetryReader: new RazerDeviceTelemetryReader(new FakeRazerFeatureTransport()),
            performanceMonitor: new RetentionTestPerformanceMonitor());
        var apply = typeof(MainViewModel).GetMethod(
            "ApplyDeviceTelemetry",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(nameof(MainViewModel), "ApplyDeviceTelemetry");

        apply.Invoke(viewModel, [new RazerDeviceTelemetry(
            null, null, null, null, null, null, null, null, null, null, [], DateTimeOffset.UtcNow,
            BladeStartupAnimationEnabled: true,
            BladeNativeDisplayMode: BladeNativeDisplayMode.Fhd,
            BladeSkuHardwareConfiguration: new(false, false, false, 0),
            BladeOneTimeFullChargeEnabled: true)]);

        apply.Invoke(viewModel, [new RazerDeviceTelemetry(
            null, null, null, null, null, null, null, null, null, null, ["temporary query failure"], DateTimeOffset.UtcNow)]);

        Assert.Equal("已启用", viewModel.BladeStartupAnimationText);
        Assert.Equal("FHD", viewModel.BladeNativeDisplayModeText);
        Assert.Contains("0x00", viewModel.BladeSkuHardwareText, StringComparison.Ordinal);
        Assert.Equal("已启用", viewModel.BladeOneTimeFullChargeText);
    }

    private sealed class RetentionTestPerformanceMonitor : IPerformanceMonitor
    {
        public ValueTask<PerformanceSnapshot> SampleAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
