using System.Buffers.Binary;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Core.Tests;

public sealed class BladeRawInputEventDecoderTests
{
    [Fact]
    public void FiltersDevicePathAndDecodesTheFirstHidReport()
    {
        var decoder = new BladeRawInputEventDecoder("VID_1532&PID_02C6&MI_01&Col04");
        var raw = CreateRawInput([0x04, 0xD3, 0x00]);

        Assert.Empty(decoder.Process("HID\\VID_1532&PID_02C6&MI_01&Col05", raw));
        Assert.Equal(
            [new BladeMappingInputEvent(BladeMappingInputKind.RazerKey, 0xD3, true)],
            decoder.Process("HID\\VID_1532&PID_02C6&MI_01&Col04", raw));
    }

    [Fact]
    public void RejectsNonHidAndTruncatedRawInput()
    {
        Assert.False(RawInputHidReportParser.TryGetFirstReport([0x00, 0x00], out _));
        var truncated = CreateRawInput([0x04]);
        BinaryPrimitives.WriteUInt32LittleEndian(truncated.AsSpan(24), 2);
        Assert.False(RawInputHidReportParser.TryGetFirstReport(truncated, out var report));
        Assert.Empty(report.ToArray());
    }

    private static byte[] CreateRawInput(byte[] report)
    {
        var raw = new byte[24 + 8 + report.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(raw, 2);
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(24), (uint)report.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(28), 1);
        report.CopyTo(raw.AsSpan(32));
        return raw;
    }
}
