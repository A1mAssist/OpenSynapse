using System.Text;

namespace OpenSynapse.Windows.Protocols;

/// <summary>
/// AudioKrakenBase / OdessaT2 37-byte HID report channel. The bundle sends a fixed
/// 37-byte report through hid.write/hid.read; the first bytes identify the command and
/// responses use the same frame. This is not the 91-byte feature-report family.
/// </summary>
internal static class RazerKrakenBaseProtocol
{
    internal const int ReportLength = 37;

    internal static byte[] CreateFirmwareRequest() => Frame(4, 32, 2, 0, 48);
    internal static byte[] CreateSerialNumberRequest() => Frame(4, 32, 22, 127, 0);
    internal static byte[] CreateHardwareIdRequest() => Frame(4, 32, 1, 127, 33);

    // The bundle writes 37 bytes through a USB control transfer; the HID collection
    // reports a longer fixed output report, and WriteFile rejects a short buffer.
    internal static byte[] PadOutputReport(byte[] request, int outputReportLength)
    {
        if (outputReportLength <= request.Length) return request;
        var padded = new byte[outputReportLength];
        request.CopyTo(padded, 0);
        return padded;
    }

    internal static Version? ParseFirmware(ReadOnlySpan<byte> response)
    {
        if (response.Length < 3) return null;
        return new Version(
            (response[2] >> 4) & 0xF,
            response[2] & 0xF,
            (response[1] >> 4) & 0xF,
            response[1] & 0xF);
    }

    internal static string? ParseSerialNumber(ReadOnlySpan<byte> response)
    {
        if (response.Length < 23) return null;
        var builder = new StringBuilder(22);
        for (var index = 1; index < 23; index++)
        {
            var value = response[index];
            if (value is >= 32 and < 127) builder.Append((char)value);
        }
        return builder.Length == 0 ? null : builder.ToString();
    }

    internal static byte? ParseHardwareId(ReadOnlySpan<byte> response) =>
        response.Length > 1 ? response[1] : null;

    private static byte[] Frame(params byte[] head)
    {
        var report = new byte[ReportLength];
        head.CopyTo(report, 0);
        return report;
    }
}
