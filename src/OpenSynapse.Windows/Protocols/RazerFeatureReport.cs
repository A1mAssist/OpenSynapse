namespace OpenSynapse.Windows.Protocols;

public static class RazerFeatureReport
{
    public const int Length = 91;
    public const int ArgumentsOffset = 9;
    internal const int OpenRazerLogicalLength = Length - 1;

    public static byte[] CreateRequest(
        byte transactionId,
        byte dataSize,
        byte commandClass,
        byte commandId,
        ReadOnlySpan<byte> arguments)
        => CreateRequestCore(
            transactionId,
            dataSize,
            commandClass,
            commandId,
            arguments,
            allowArgumentsBeyondDeclaredSize: false);

    internal static byte[] CreateRequestWithDeclaredSize(
        byte transactionId,
        byte dataSize,
        byte commandClass,
        byte commandId,
        ReadOnlySpan<byte> arguments)
        => CreateRequestCore(
            transactionId,
            dataSize,
            commandClass,
            commandId,
            arguments,
            allowArgumentsBeyondDeclaredSize: true);

    internal static byte[] CreateRequestFromOpenRazerLogical(
        ReadOnlySpan<byte> logicalReport,
        byte reportId = 0)
    {
        if (logicalReport.Length != OpenRazerLogicalLength)
        {
            throw new ArgumentException(
                $"OpenRazer logical reports must be {OpenRazerLogicalLength} bytes.",
                nameof(logicalReport));
        }
        if (logicalReport[0] != 0 ||
            logicalReport[2] != 0 ||
            logicalReport[3] != 0 ||
            logicalReport[4] != 0 ||
            logicalReport[5] > 80 ||
            logicalReport[89] != 0 ||
            logicalReport[88] != CalculateOpenRazerCrc(logicalReport))
        {
            throw new ArgumentException("OpenRazer logical request header or CRC is invalid.", nameof(logicalReport));
        }

        // Linux report/response indexes select its USB transport path; they are not report bytes.
        var windowsReport = new byte[Length];
        windowsReport[0] = reportId;
        logicalReport.CopyTo(windowsReport.AsSpan(1));
        return windowsReport;
    }

    internal static void ValidatePreparedStarlightRequest(ReadOnlySpan<byte> report)
    {
        ValidatePreparedRequest(report);
        if (report[1] != 0x00 ||
            report[2] != 0xFF ||
            report[3] != 0x00 ||
            report[4] != 0x00 ||
            report[5] != 0x00 ||
            report[6] != 0x01 ||
            report[7] != 0x03 ||
            report[8] != 0x0A ||
            report[ArgumentsOffset] != 0x19 ||
            report[ArgumentsOffset + 1] is < 0x01 or > 0x03 ||
            report[ArgumentsOffset + 2] is < 0x01 or > 0x03 ||
            report[18..89].ContainsAnyExcept((byte)0x00) ||
            report[90] != 0x00 ||
            report[89] != CalculateCrc(report))
        {
            throw new ArgumentException("The prebuilt Starlight report is invalid.", nameof(report));
        }

        var colorMode = report[ArgumentsOffset + 1];
        var colors = report[(ArgumentsOffset + 3)..(ArgumentsOffset + 9)];
        if ((colorMode == 0x03 && colors.ContainsAnyExcept((byte)0x00)) ||
            (colorMode == 0x01 && colors[3..].ContainsAnyExcept((byte)0x00)))
        {
            throw new ArgumentException("The prebuilt Starlight report has an invalid color mode.", nameof(report));
        }
    }

    internal static void ValidatePreparedRequest(ReadOnlySpan<byte> report)
    {
        if (report.Length != Length || report[0] != 0x00 || report[1] != 0x00 ||
            report[3] != 0x00 || report[4] != 0x00 || report[5] != 0x00 ||
            report[6] > 80 || report[90] != 0x00 || report[89] != CalculateCrc(report))
        {
            throw new ArgumentException("Prepared Razer feature request is invalid.", nameof(report));
        }
    }

    private static byte[] CreateRequestCore(
        byte transactionId,
        byte dataSize,
        byte commandClass,
        byte commandId,
        ReadOnlySpan<byte> arguments,
        bool allowArgumentsBeyondDeclaredSize)
    {
        if (dataSize > 80 || arguments.Length > 80 ||
            (!allowArgumentsBeyondDeclaredSize && arguments.Length > dataSize))
        {
            throw new ArgumentOutOfRangeException(nameof(dataSize));
        }

        var report = new byte[Length];
        report[2] = transactionId;
        report[6] = dataSize;
        report[7] = commandClass;
        report[8] = commandId;
        arguments.CopyTo(report.AsSpan(ArgumentsOffset));
        report[89] = CalculateCrc(report);
        return report;
    }

    public static byte CalculateCrc(ReadOnlySpan<byte> report)
    {
        if (report.Length != Length)
        {
            throw new ArgumentException($"Razer feature reports must be {Length} bytes.", nameof(report));
        }

        byte crc = 0;
        for (var index = 3; index <= 88; index++)
        {
            crc ^= report[index];
        }

        return crc;
    }

    private static byte CalculateOpenRazerCrc(ReadOnlySpan<byte> logicalReport)
    {
        byte crc = 0;
        for (var index = 2; index <= 87; index++)
        {
            crc ^= logicalReport[index];
        }

        return crc;
    }

    public static bool Matches(
        ReadOnlySpan<byte> request,
        ReadOnlySpan<byte> response,
        bool allowRemainingPacketsMismatch = false)
    {
        return request.Length == Length &&
               response.Length == Length &&
               response[2] == request[2] &&
               (allowRemainingPacketsMismatch ||
                (response[3] == request[3] && response[4] == request[4])) &&
               response[7] == request[7] &&
               response[8] == request[8] &&
               response[89] == CalculateCrc(response);
    }

    internal static bool MatchesReportId(ReadOnlySpan<byte> response, byte reportId) =>
        response.Length == Length && response[0] == reportId;

    internal static bool IsSuccessfulResponse(
        ReadOnlySpan<byte> request,
        ReadOnlySpan<byte> response,
        byte minimumArguments,
        bool allowRemainingPacketsMismatch = false) =>
        request.Length == Length &&
        response.Length == Length &&
        response[1] == 0x02 &&
        response[6] == request[6] &&
        minimumArguments <= response[6] &&
        Matches(request, response, allowRemainingPacketsMismatch);
}
