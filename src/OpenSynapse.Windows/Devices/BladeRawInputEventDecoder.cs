using System.Buffers.Binary;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public static class RawInputHidReportParser
{
    public static bool TryGetReports(ReadOnlySpan<byte> rawInput, out IReadOnlyList<byte[]> reports)
    {
        reports = [];
        var hidHeaderOffset = IntPtr.Size == 8 ? 24 : 16;
        if (rawInput.Length < hidHeaderOffset + 8 || BinaryPrimitives.ReadUInt32LittleEndian(rawInput) != 2)
            return false;
        var reportSize = BinaryPrimitives.ReadUInt32LittleEndian(rawInput[hidHeaderOffset..]);
        var reportCount = BinaryPrimitives.ReadUInt32LittleEndian(rawInput[(hidHeaderOffset + 4)..]);
        var reportOffset = hidHeaderOffset + 8;
        if (reportSize == 0 || reportCount == 0 || reportSize > int.MaxValue || reportCount > int.MaxValue || reportOffset > rawInput.Length)
            return false;
        var requiredLength = (ulong)reportSize * reportCount;
        if (requiredLength > (ulong)(rawInput.Length - reportOffset))
            return false;
        var size = (int)reportSize;
        var parsed = new List<byte[]>((int)reportCount);
        for (var index = 0; index < (int)reportCount; index++)
            parsed.Add(rawInput.Slice(reportOffset + index * size, size).ToArray());
        reports = parsed;
        return true;
    }

    public static bool TryGetFirstReport(
        ReadOnlySpan<byte> rawInput,
        out ReadOnlySpan<byte> report)
    {
        report = default;
        if (!TryGetReports(rawInput, out var reports) || reports.Count == 0)
            return false;
        report = reports[0];
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
            !RawInputHidReportParser.TryGetReports(rawInput, out var reports))
        {
            return [];
        }

        var events = new List<BladeMappingInputEvent>();
        foreach (var report in reports)
            events.AddRange(_reportDecoder.Process(report));
        return events;
    }

    public IReadOnlyList<BladeMappingInputEvent> Reset() => _reportDecoder.Reset();
}
