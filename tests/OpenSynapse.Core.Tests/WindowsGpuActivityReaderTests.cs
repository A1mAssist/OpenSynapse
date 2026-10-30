using OpenSynapse.Windows.Sensors;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class WindowsGpuActivityReaderTests
{
    [Fact]
    public void SelectDisplayGpuPrefersAnActiveNvidiaAdapter()
    {
        var samples = new[]
        {
            new WindowsGpuSample("AMD Radeon", 0x1002, 40, 40, 1024, 8192, IsIntegrated: false),
            new WindowsGpuSample("NVIDIA RTX", 0x10DE, 20, 20, 2048, 8192, IsIntegrated: false),
        };

        Assert.Equal("NVIDIA RTX", WindowsGpuActivityReader.SelectDisplayGpu(samples)?.Name);
    }

    [Fact]
    public void SelectDisplayGpuPrefersDiscreteAmdOverIntegratedAmd()
    {
        var samples = new[]
        {
            new WindowsGpuSample("AMD Radeon Graphics", 0x1002, 2, 2, 512, 8192, IsIntegrated: true),
            new WindowsGpuSample("AMD Radeon RX", 0x1002, 5, 5, 4096, 8192, IsIntegrated: false),
        };

        Assert.Equal("AMD Radeon RX", WindowsGpuActivityReader.SelectDisplayGpu(samples)?.Name);
    }

    [Fact]
    public void SelectDisplayGpuFallsBackToIntegratedWhenNoDiscreteAdapterIsPresent()
    {
        var samples = new[]
        {
            new WindowsGpuSample("Intel UHD", 0x8086, 3, 3, 512, 8192, IsIntegrated: true),
        };

        Assert.Equal("Intel UHD", WindowsGpuActivityReader.SelectDisplayGpu(samples)?.Name);
    }

    [Fact]
    public void AmdAdapterSelectionPrefersDiscreteAndFallsBackToIntegrated()
    {
        Assert.Equal(0, AmdAdlTelemetryReader.SelectAdapterIndex([(0, 1), (2, 1)]));
        Assert.Equal(1, AmdAdlTelemetryReader.SelectAdapterIndex([(2, 1), (0, 1)]));
        Assert.Equal(0, AmdAdlTelemetryReader.SelectAdapterIndex([(2, 2)]));
        Assert.Equal(-1, AmdAdlTelemetryReader.SelectAdapterIndex([(0, 0)]));
    }
}
