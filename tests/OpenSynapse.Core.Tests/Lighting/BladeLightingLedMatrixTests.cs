using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

/// <summary>
/// Locks the rzDevice25LedMatrixBlade2 lighting bytes to the 02E0 middleware
/// handler: firmware effects use 03:0A and software frames use 03:0B.
/// </summary>
public sealed class BladeLightingLedMatrixTests
{
    [Fact]
    public void Blade16_2026DeclaresTheMatrixAndThermalFamily()
    {
        var manifest = RazerDeviceRegistry.BuiltIn.Find(0x1532, 0x02E0);

        Assert.NotNull(manifest);
        Assert.Equal("blade-671", manifest.ProtocolFamily);
        Assert.Equal(6, manifest.MatrixRows);
        Assert.Equal(17, manifest.MatrixColumns);
        Assert.Equal(0x83, manifest.GetRequiredCapability("keyboard-brightness.get").CommandId);
        Assert.Equal(0x03, manifest.GetRequiredCapability("keyboard-brightness.get").MaximumDataSize);
    }

    [Fact]
    public void Blade16_2026KeepsBothFirmwareAndFrameChannels()
    {
        // The 2023 selector-only admission must not swallow the 2026 matrix keyboard.
        Assert.False(BladeLighting2023Admitted.UsesFirmwareKeyboardLighting(
            RazerDeviceRegistry.BuiltIn.Find(0x1532, 0x02E0)!.LightingDialect));
    }

    [Fact]
    public void StaticEffectUsesTheMiddlewareLedEffectCommand()
    {
        var request = BladeLightingLedMatrixProtocol.CreateStaticRequest(new RazerRgb(0x12, 0x34, 0x56));

        Assert.Equal(0xFF, request[2]);
        Assert.Equal(0x03, request[7]);
        Assert.Equal(0x0A, request[8]);
        Assert.Equal(4, request[6]);
        Assert.Equal(
            [0x06, 0x12, 0x34, 0x56],
            request[RazerFeatureReport.ArgumentsOffset..(RazerFeatureReport.ArgumentsOffset + 4)]);
        Assert.Equal(RazerFeatureReport.CalculateCrc(request), request[89]);
    }

    [Theory]
    [InlineData(nameof(BladeLightingLedMatrixProtocol.CreateOffRequest), 0x00)]
    [InlineData(nameof(BladeLightingLedMatrixProtocol.CreateSpectrumRequest), 0x04)]
    [InlineData(nameof(BladeLightingLedMatrixProtocol.CreateCustomFrameEffectRequest), 0x05)]
    public void EffectCommandsCarryTheMiddlewareEffectId(string factory, byte expectedEffect)
    {
        var request = factory switch
        {
            nameof(BladeLightingLedMatrixProtocol.CreateOffRequest) =>
                BladeLightingLedMatrixProtocol.CreateOffRequest(),
            nameof(BladeLightingLedMatrixProtocol.CreateSpectrumRequest) =>
                BladeLightingLedMatrixProtocol.CreateSpectrumRequest(),
            _ => BladeLightingLedMatrixProtocol.CreateCustomFrameEffectRequest(),
        };

        Assert.Equal(0x03, request[7]);
        Assert.Equal(0x0A, request[8]);
        Assert.Equal(expectedEffect == 0x05 ? 2 : 1, request[6]);
        Assert.Equal(
            expectedEffect == 0x05 ? [expectedEffect, 0x00] : [expectedEffect],
            request[RazerFeatureReport.ArgumentsOffset..(RazerFeatureReport.ArgumentsOffset + request[6])]);
    }

    [Fact]
    public void WaveCarriesItsDirectionAsTheFirstArgument()
    {
        var request = BladeLightingLedMatrixProtocol.CreateWaveRequest(BladeWaveDirection.Left);

        Assert.Equal(2, request[6]);
        Assert.Equal(
            [0x01, 0x01],
            request[RazerFeatureReport.ArgumentsOffset..(RazerFeatureReport.ArgumentsOffset + 2)]);
    }

    [Fact]
    public void FrameRowUsesTheMatrixRowCommandAndInclusiveColumns()
    {
        var colors = Enumerable.Repeat(new RazerRgb(0x0A, 0x0B, 0x0C), 17).ToArray();
        var request = BladeLightingLedMatrixProtocol.CreateMatrixRowRequest(2, 0, colors);

        Assert.Equal(3, request[2]);
        Assert.Equal(0x03, request[7]);
        Assert.Equal(0x0B, request[8]);
        Assert.Equal(4 + 17 * 3, request[6]);
        Assert.Equal(0xFF, request[RazerFeatureReport.ArgumentsOffset]);
        Assert.Equal(2, request[RazerFeatureReport.ArgumentsOffset + 1]);
        Assert.Equal(0, request[RazerFeatureReport.ArgumentsOffset + 2]);
        Assert.Equal(16, request[RazerFeatureReport.ArgumentsOffset + 3]);
        Assert.Equal(0x0A, request[RazerFeatureReport.ArgumentsOffset + 4]);
        Assert.Equal(RazerFeatureReport.CalculateCrc(request), request[89]);
    }

    [Fact]
    public void CompleteFrameEndsWithTheCustomFrameEffect()
    {
        var frame = Enumerable.Repeat(new RazerRgb(1, 2, 3), 6 * 17).ToArray();
        var requests = BladeLightingLedMatrixProtocol.CreateMatrixFrameRequests(frame);

        Assert.Equal(7, requests.Length);
        for (var row = 0; row < 6; row++)
        {
            Assert.Equal(0x03, requests[row][7]);
            Assert.Equal(0x0B, requests[row][8]);
            Assert.Equal(row, requests[row][RazerFeatureReport.ArgumentsOffset + 1]);
        }
        Assert.Equal(0x03, requests[6][7]);
        Assert.Equal(0x0A, requests[6][8]);
        Assert.Equal(2, requests[6][6]);
        Assert.Equal(
            [0x05, 0x00],
            requests[6][RazerFeatureReport.ArgumentsOffset..(RazerFeatureReport.ArgumentsOffset + 2)]);
    }
}
