using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

/// <summary>
/// Locks the 023A rzDevice25LedMatrixBlade effects to the 03:0A handler in its
/// middleware. This generation does not use the 03:02 store/region form.
/// </summary>
public sealed class BladeLightingLedMatrixBladeTests
{
    [Fact]
    public void StaticEffectCarriesEffectIdAndRgbWithoutStoreOrRegion()
    {
        var request = BladeLightingLedMatrixBladeProtocol.CreateStaticRequest(
            0x05,
            new RazerRgb(0x12, 0x34, 0x56));

        Assert.Equal(0x05, request[2]);
        Assert.Equal(0x03, request[7]);
        Assert.Equal(0x0A, request[8]);
        Assert.Equal(4, request[6]);
        Assert.Equal(
            [0x06, 0x12, 0x34, 0x56],
            request[RazerFeatureReport.ArgumentsOffset..(RazerFeatureReport.ArgumentsOffset + 4)]);
        Assert.Equal(RazerFeatureReport.CalculateCrc(request), request[89]);
    }

    [Fact]
    public void BreathingUsesTheFiveByteSingleColorPayload()
    {
        var request = BladeLightingLedMatrixBladeProtocol.CreateBreathingSingleRequest(
            0x06,
            new RazerRgb(0x01, 0x02, 0x03));

        Assert.Equal(0x06, request[2]);
        Assert.Equal(5, request[6]);
        Assert.Equal(
            [0x03, 0x01, 0x01, 0x02, 0x03],
            request[RazerFeatureReport.ArgumentsOffset..(RazerFeatureReport.ArgumentsOffset + 5)]);
    }

    [Theory]
    [InlineData(nameof(BladeLightingLedMatrixBladeProtocol.CreateOffRequest), 0x00)]
    [InlineData(nameof(BladeLightingLedMatrixBladeProtocol.CreateSpectrumRequest), 0x04)]
    public void OneByteEffectsUseTheMiddlewareEffectId(string factory, byte expected)
    {
        var request = factory == nameof(BladeLightingLedMatrixBladeProtocol.CreateOffRequest)
            ? BladeLightingLedMatrixBladeProtocol.CreateOffRequest(0x07)
            : BladeLightingLedMatrixBladeProtocol.CreateSpectrumRequest(0x07);

        Assert.Equal(0x07, request[2]);
        Assert.Equal(1, request[6]);
        Assert.Equal(expected, request[RazerFeatureReport.ArgumentsOffset]);
    }

    [Fact]
    public void WaveAndReactiveUseTheMiddlewareEffectIds()
    {
        var wave = BladeLightingLedMatrixBladeProtocol.CreateWaveRequest(
            0x08, BladeWaveDirection.Left);
        Assert.Equal([0x01, 0x01], wave[RazerFeatureReport.ArgumentsOffset..(RazerFeatureReport.ArgumentsOffset + 2)]);

        var reactive = BladeLightingLedMatrixBladeProtocol.CreateReactiveRequest(
            0x09, 3, new RazerRgb(0x11, 0x22, 0x33));
        Assert.Equal(
            [0x02, 0x03, 0x11, 0x22, 0x33],
            reactive[RazerFeatureReport.ArgumentsOffset..(RazerFeatureReport.ArgumentsOffset + 5)]);
    }

    [Fact]
    public void MatrixFramesUseSixteenColumnRowsAndTheCustomFrameLatch()
    {
        var frame = Enumerable.Repeat(new RazerRgb(0x11, 0x22, 0x33), 6 * 16).ToArray();
        var requests = BladeLightingLedMatrixBladeProtocol.CreateMatrixFrameRequests(frame);

        Assert.Equal(7, requests.Length);
        for (byte row = 0; row < 6; row++)
        {
            var arguments = requests[row].AsSpan(RazerFeatureReport.ArgumentsOffset);
            Assert.Equal(0x03, requests[row][7]);
            Assert.Equal(0x0B, requests[row][8]);
            Assert.Equal(0x34, requests[row][6]);
            Assert.Equal(0xFF, arguments[0]);
            Assert.Equal(row, arguments[1]);
            Assert.Equal(0x00, arguments[2]);
            Assert.Equal(0x0F, arguments[3]);
            Assert.Equal([0x11, 0x22, 0x33], arguments[4..7].ToArray());
        }

        Assert.Equal(0x03, requests[6][7]);
        Assert.Equal(0x0A, requests[6][8]);
        Assert.Equal([0x05, 0x00], requests[6][RazerFeatureReport.ArgumentsOffset..(RazerFeatureReport.ArgumentsOffset + 2)]);
    }
}
