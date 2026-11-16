namespace OpenSynapse.Windows.Protocols;

/// <summary>
/// Frame helpers for standalone accessories that use either the 65-byte protocol-25
/// feature report or the legacy 91-byte feature report.
/// </summary>
internal static class RazerAccessoryProtocol
{
    internal const int ReportLength65 = 65;
    internal const int ReportLength91 = RazerFeatureReport.Length;
    internal const int ArgumentsOffset = 9;

    internal static byte[] CreateRequest(
        int reportLength,
        byte reportId,
        byte transactionId,
        byte dataSize,
        byte commandClass,
        byte commandId,
        ReadOnlySpan<byte> arguments)
    {
        if (reportLength == ReportLength91)
        {
            var report = RazerFeatureReport.CreateRequest(
                transactionId, dataSize, commandClass, commandId, arguments);
            report[0] = reportId;
            return report;
        }

        if (reportLength != ReportLength65)
        {
            throw new ArgumentOutOfRangeException(
                nameof(reportLength),
                reportLength,
                "Accessory reports are 65 or 91 bytes.");
        }
        if (dataSize > 55 || arguments.Length > 55)
        {
            throw new ArgumentOutOfRangeException(nameof(dataSize));
        }

        var frame = new byte[ReportLength65];
        frame[0] = reportId;
        frame[2] = transactionId;
        frame[6] = dataSize;
        frame[7] = commandClass;
        frame[8] = commandId;
        arguments.CopyTo(frame.AsSpan(ArgumentsOffset));
        frame[^1] = 0;
        frame[^2] = Checksum(frame);
        return frame;
    }

    internal static byte[] CreateProfileRequest(
        byte transactionId,
        byte profileId,
        byte ledId,
        byte dataSize,
        byte commandClass,
        byte commandId,
        ReadOnlySpan<byte> arguments)
    {
        var report = RazerFeatureReport.CreateRequest(
            transactionId,
            dataSize,
            commandClass,
            commandId,
            arguments);
        report[3] = profileId;
        report[4] = ledId;
        report[89] = RazerFeatureReport.CalculateCrc(report);
        return report;
    }

    internal static ReadOnlySpan<byte> Arguments(ReadOnlySpan<byte> response)
    {
        if (response.Length is not (ReportLength65 or ReportLength91))
        {
            throw new InvalidDataException(
                $"Accessory reports are 65 or 91 bytes; got {response.Length}.");
        }

        var length = response[6];
        var maximum = response.Length - ArgumentsOffset - 2;
        if (length > maximum)
        {
            throw new InvalidDataException(
                $"Accessory response declares {length} argument bytes; {maximum} maximum.");
        }

        return response.Slice(ArgumentsOffset, length);
    }

    internal static int ReadUInt16BigEndian(ReadOnlySpan<byte> source, int offset) =>
        (source[offset] << 8) | source[offset + 1];

    internal static void WriteUInt16BigEndian(Span<byte> destination, int offset, int value)
    {
        if (value is < 0 or > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        destination[offset] = (byte)(value >> 8);
        destination[offset + 1] = (byte)value;
    }

    internal static bool MatchesCommand(
        ReadOnlySpan<byte> response,
        byte commandClass,
        byte commandId) =>
        response.Length is (ReportLength65 or ReportLength91) &&
        response[7] == commandClass &&
        response[8] == commandId;

    internal static bool MatchesProfileCommand(
        ReadOnlySpan<byte> response,
        byte commandClass,
        byte commandId,
        byte profileId,
        byte ledId) =>
        MatchesCommand(response, commandClass, commandId) &&
        response[3] == profileId &&
        response[4] == ledId;

    internal static byte Checksum(ReadOnlySpan<byte> report)
    {
        byte checksum = 0;
        for (var index = 1; index < report.Length - 2; index++)
        {
            checksum ^= report[index];
        }

        return checksum;
    }
}
