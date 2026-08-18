using System.Buffers.Binary;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public static class RawInputHidReportParser
{
    public static bool TryGetFirstReport(
        ReadOnlySpan<byte> rawInput,
        out ReadOnlySpan<byte> report)
    {
        report = default;
        var hidHeaderOffset = IntPtr.Size == 8 ? 24 : 16;
        if (rawInput.Length < hidHeaderOffset + 8 ||
            BinaryPrimitives.ReadUInt32LittleEndian(rawInput) != 2)
        {
            return false;
        }

        var reportSize = BinaryPrimitives.ReadUInt32LittleEndian(
            rawInput[hidHeaderOffset..]);
        var reportCount = BinaryPrimitives.ReadUInt32LittleEndian(
            rawInput[(hidHeaderOffset + 4)..]);
        var reportOffset = hidHeaderOffset + 8;
        if (reportSize == 0 || reportCount == 0 ||
            reportSize > int.MaxValue ||
            reportOffset > rawInput.Length ||
            reportSize > (uint)(rawInput.Length - reportOffset))
        {
            return false;
        }

        report = rawInput.Slice(reportOffset, checked((int)reportSize));
        return true;
    }
}

public sealed class BladeRawInputEventDecoder
{
    private readonly string _devicePathFragment;
    private readonly BladeRazerKeyReportDecoder _reportDecoder = new();

    public BladeRawInputEventDecoder(string devicePathFragment)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(devicePathFragment);
        _devicePathFragment = devicePathFragment;
    }

    public IReadOnlyList<BladeMappingInputEvent> Process(
        string devicePath,
        ReadOnlySpan<byte> rawInput)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(devicePath);
        if (!devicePath.Contains(_devicePathFragment, StringComparison.OrdinalIgnoreCase) ||
            !RawInputHidReportParser.TryGetFirstReport(rawInput, out var report))
        {
            return [];
        }

        return _reportDecoder.Process(report);
    }

    public IReadOnlyList<BladeMappingInputEvent> Reset() => _reportDecoder.Reset();
}
