using OpenSynapse.App.ViewModels;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class BladeMiddlewareModelsTests
{
    [Theory]
    [InlineData(0x029D, "blade-669", "Razer Blade 14 (2023)", 6, 16, true, 3)]
    [InlineData(0x02B6, "blade-694", "Razer Blade 14 (2024)", 6, 16, true, 3)]
    [InlineData(0x02B7, "blade-695", "Razer Blade 16 (2024)", 6, 17, false, 0)]
    [InlineData(0x02B8, "blade-696", "Razer Blade 18 (2024)", 6, 16, true, 0)]
    [InlineData(0x02C5, "blade-709", "Razer Blade 14 (2025)", 6, 16, false, 0)]
    [InlineData(0x02C7, "blade-711", "Razer Blade 18 (2025)", 6, 19, false, 0)]
    public void MiddlewareModelsAreRegisteredWithTheirMatrixAndCapabilities(
        ushort productId,
        string manifestId,
        string displayName,
        int rows,
        int columns,
        bool maxFan,
        int cpuModeCount)
    {
        var manifest = RazerDeviceRegistry.BuiltIn.Find(0x1532, productId);

        Assert.NotNull(manifest);
        Assert.Equal(manifestId, manifest!.Id);
        Assert.Equal(displayName, manifest.DisplayName);
        Assert.Equal("blade-710-chroma", manifest.ProtocolFamily);
        Assert.True(DeviceProtocolFamilies.IsBlade(manifest.ProtocolFamily));
        Assert.True(DeviceProtocolFamilies.UsesBladeMatrix(manifest.ProtocolFamily));
        Assert.Equal(rows, manifest.MatrixRows);
        Assert.Equal(columns, manifest.MatrixColumns);
        Assert.Equal(0x03, manifest.Capabilities["keyboard-brightness.get"].CommandClass);
        Assert.Equal(0x83, manifest.Capabilities["keyboard-brightness.get"].CommandId);
        Assert.Equal(0x03, manifest.Capabilities["keyboard-brightness.set"].CommandClass);
        Assert.Equal(0x03, manifest.Capabilities["keyboard-brightness.set"].CommandId);
        Assert.Equal(maxFan, BladeModelCapabilities.SupportsMaxFan(productId));
        Assert.Equal(cpuModeCount, BladeModelCapabilities.CpuBoostModesFor(productId).Count);
        Assert.True(BladeModelCapabilities.SupportsFixedFan(productId));
        Assert.True(BladeModelCapabilities.SupportsFanCurve(productId));
    }

    [Theory]
    [InlineData(0x029D, false)]
    [InlineData(0x02B6, false)]
    [InlineData(0x02B7, false)]
    [InlineData(0x02B8, false)]
    [InlineData(0x02C5, true)]
    [InlineData(0x02C6, true)]
    [InlineData(0x02C7, true)]
    public void StartupAnimationIsExposedOnlyWhereTheBundlePublishesIt(ushort productId, bool expected)
    {
        var manifest = RazerDeviceRegistry.BuiltIn.Find(0x1532, productId);

        Assert.NotNull(manifest);
        Assert.Equal(expected, manifest!.Capabilities.ContainsKey("startup-animation.get"));
        Assert.Equal(expected, manifest.Capabilities.ContainsKey("startup-animation.set"));
    }

    [Theory]
    [InlineData(0x02B6)]
    [InlineData(0x02B7)]
    [InlineData(0x02B8)]
    [InlineData(0x02C7)]
    [InlineData(0x02E1)]
    [InlineData(0x0255)]
    [InlineData(0x029F)]
    [InlineData(0x02A0)]
    [InlineData(0x02C5)]
    public void TheGamingByteIsParsedTheSameOnEveryBladeModel(ushort productId)
    {
        // Every middleware bundle calls wire value 0x01 Gaming; the 2018-2020 chassis
        // write it as Turbo in their mode table, which is the same byte.
        Assert.Equal(
            BladePerformanceMode.Gaming,
            RazerDeviceTelemetryReader.ParseBladePerformanceMode(0x01));
        // The mode tables that publish it name the same byte, whatever they call it.
        Assert.Equal(
            productId is 0x02B6 or 0x02B7 or 0x02B8 or 0x02C7 or 0x02E1,
            BladeModelCapabilities.PublishesPerformanceMode(productId, BladePerformanceMode.Gaming));
    }

    [Theory]
    [InlineData(6, 16)]
    [InlineData(6, 17)]
    [InlineData(6, 19)]
    public void MatrixFrameReportsUseTheRequestedGeometry(int rows, int columns)
    {
        var color = new RazerRgb(0x12, 0x34, 0x56);
        var frame = Enumerable.Repeat(color, rows * columns).ToArray();

        var requests = BladeLightingProtocol.CreateMatrixFrameRequests(frame, rows, columns);

        Assert.Equal(rows + 1, requests.Length);
        for (var row = 0; row < rows; row++)
        {
            var request = requests[row];
            Assert.Equal((byte)(row + 1), request[2]);
            Assert.Equal((byte)(4 + columns * 3), request[6]);
            Assert.Equal((byte)0x03, request[7]);
            Assert.Equal((byte)0x0B, request[8]);
            Assert.Equal((byte)0xFF, request[RazerFeatureReport.ArgumentsOffset]);
            Assert.Equal((byte)row, request[RazerFeatureReport.ArgumentsOffset + 1]);
            Assert.Equal((byte)0, request[RazerFeatureReport.ArgumentsOffset + 2]);
            Assert.Equal((byte)(columns - 1), request[RazerFeatureReport.ArgumentsOffset + 3]);
        }

        Assert.Equal((byte)0x03, requests[^1][7]);
        Assert.Equal((byte)0x0A, requests[^1][8]);
    }
}
