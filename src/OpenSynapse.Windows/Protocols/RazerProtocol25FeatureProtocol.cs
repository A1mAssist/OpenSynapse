namespace OpenSynapse.Windows.Protocols;

/// <summary>
/// Protocol 25 audio devices that use a 64/65-byte feature report instead of the
/// legacy 91-byte report. The envelope keeps the same byte offsets; only the report
/// length, report id and checksum position change.
/// </summary>
internal static class RazerProtocol25FeatureProtocol
{
    internal const int FullReportLength = 65;
    internal const int ShortReportLength = 64;

    internal static byte ReportIdFor(ushort productId) => productId switch
    {
        0x0532 or 0x0548 or 0x05A3 => 0x08,
        _ => 0x07,
    };

    internal static int ReportLengthFor(ushort productId) =>
        productId == 0x05A3 ? ShortReportLength : FullReportLength;

    internal static bool UsesTenBandEqualizer(ushort productId) =>
        productId is 0x0532 or 0x0548 or 0x05A3;

    internal static bool SupportsHidEqualizer(ushort productId) =>
        productId is 0x0532 or 0x0548 or 0x05A3 or 0x058E or 0x05A6 or 0x058C;

    internal static bool SupportsHidBass(ushort productId) =>
        productId is 0x0532 or 0x0548 or 0x05A3 or 0x058E or 0x05A6;

    internal static bool SupportsHidMicrophone(ushort productId) =>
        productId is 0x056A or 0x056F or 0x058E or 0x05A6 or 0x058C;

    internal static bool SupportsHidMicVolume(ushort productId) =>
        productId is 0x058E or 0x05A6 or 0x058C;

    internal static bool SupportsHidAudioInput(ushort productId) =>
        productId is 0x056F or 0x058E or 0x05A6;

    internal static byte[] CreateRequest(ushort productId, byte[] report91)
    {
        ArgumentNullException.ThrowIfNull(report91);
        if (report91.Length != RazerFeatureReport.Length)
        {
            throw new ArgumentException("A protocol 25 source report must be 91 bytes.", nameof(report91));
        }

        var reportLength = ReportLengthFor(productId);
        var report = new byte[reportLength];
        report[0] = ReportIdFor(productId);
        Array.Copy(report91, 1, report, 1, reportLength - 2);
        report[^1] = 0;
        report[^2] = Checksum(report, ChecksumStartFor(productId));
        return report;
    }

    internal static bool Matches(ReadOnlySpan<byte> request, ReadOnlySpan<byte> response) =>
        request.Length == response.Length &&
        request.Length is 64 or 65 &&
        response[0] == request[0] &&
        response[2] == request[2] &&
        response[6] == request[6] &&
        response[7] == request[7] &&
        response[8] == request[8];

    // Noa inherits the 91-byte base loop (i = 2..87), which skips the transaction id;
    // Camy/Linda/T3V2 start at 0. Both ranges are offset by the report id byte here.
    private static int ChecksumStartFor(ushort productId) =>
        productId is 0x0532 or 0x0548 or 0x05A3 ? 3 : 1;

    private static byte Checksum(ReadOnlySpan<byte> report, int start)
    {
        byte checksum = 0;
        for (var index = start; index < report.Length - 2; index++)
        {
            checksum ^= report[index];
        }

        return checksum;
    }
}
