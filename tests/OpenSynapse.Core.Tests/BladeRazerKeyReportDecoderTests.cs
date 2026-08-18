using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Core.Tests;

public sealed class BladeRazerKeyReportDecoderTests
{
    [Fact]
    public void DecodesPhysicallyVerifiedM3M4M5PressReleaseReports()
    {
        var decoder = new BladeRazerKeyReportDecoder();

        Assert.Equal(
            [new BladeMappingInputEvent(BladeMappingInputKind.RazerKey, 0x03, true)],
            decoder.Process([0x04, 0x03, 0x00, 0x00]));
        Assert.Equal(
            [new BladeMappingInputEvent(BladeMappingInputKind.RazerKey, 0x03, false)],
            decoder.Process([0x04, 0x00, 0x00, 0x00]));
        Assert.Equal(
            [new BladeMappingInputEvent(BladeMappingInputKind.RazerKey, 0xD3, true)],
            decoder.Process([0x04, 0xD3, 0x00, 0x00]));
        Assert.Equal(
            [new BladeMappingInputEvent(BladeMappingInputKind.RazerKey, 0xD3, false)],
            decoder.Process([0x04, 0x00, 0x00, 0x00]));
        Assert.Equal(
            [new BladeMappingInputEvent(BladeMappingInputKind.RazerKey, 0xD4, true)],
            decoder.Process([0x04, 0xD4, 0x00, 0x00]));
    }

    [Fact]
    public void IgnoresReport05AndResetReleasesHeldKeys()
    {
        var decoder = new BladeRazerKeyReportDecoder();
        Assert.Empty(decoder.Process([0x05, 0x33, 0x0C, 0x0C]));
        decoder.Process([0x04, 0xD3, 0xD4, 0x00]);

        Assert.Equal(
            [
                new BladeMappingInputEvent(BladeMappingInputKind.RazerKey, 0xD3, false),
                new BladeMappingInputEvent(BladeMappingInputKind.RazerKey, 0xD4, false),
            ],
            decoder.Reset());
    }
}
