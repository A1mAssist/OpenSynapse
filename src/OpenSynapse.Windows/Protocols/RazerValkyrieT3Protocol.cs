namespace OpenSynapse.Windows.Protocols;

// Local vendor source: bundles/056B/rzDevice25ValkyrieT3.6a7d2e9e843af8d2634a.js,
// _createDataSend / _getUSBTransferInResult. This is NOT the MXIC 50 41 envelope.
internal static class RazerValkyrieT3Protocol
{
    internal const int ReportLength = 64;
    internal const byte ReportId = 7;
    // AudioLindaT3 (Mako X) declares reportLength 65 in the bundle: report id +
    // 64 bytes, checksum at 63, trailing zero at 64.
    internal const int MakoXReportLength = 65;
    internal const int MakoXMaxPayload = 54;

    internal static byte[] Create(byte command, ReadOnlySpan<byte> payload) =>
        Create(0x18, command, payload);

    internal static byte[] Create(byte commandClass, byte command, ReadOnlySpan<byte> payload)
    {
        if (payload.Length > 53) throw new ArgumentOutOfRangeException(nameof(payload));
        var report = new byte[ReportLength];
        report[0] = ReportId;
        report[2] = 1; // Replaced by the serialized transport for each exchange.
        report[6] = (byte)payload.Length;
        report[7] = commandClass;
        report[8] = command;
        payload.CopyTo(report.AsSpan(9));
        for (var i = 3; i < 62; i++) report[62] ^= report[i];
        return report;
    }

    internal static byte[] CreateMakoX(byte commandClass, byte command, ReadOnlySpan<byte> payload)
    {
        if (payload.Length > MakoXMaxPayload) throw new ArgumentOutOfRangeException(nameof(payload));
        var report = new byte[MakoXReportLength];
        report[0] = ReportId;
        report[2] = 0; // Replaced by the serialized transport for each exchange.
        report[6] = (byte)payload.Length;
        report[7] = commandClass;
        report[8] = command;
        payload.CopyTo(report.AsSpan(9));
        SealMakoX(report);
        return report;
    }

    internal static byte[] CreateMakoX(byte command, ReadOnlySpan<byte> payload) =>
        CreateMakoX(0x18, command, payload);

    internal static void SealMakoX(Span<byte> report)
    {
        report[63] = 0;
        for (var i = 1; i < 63; i++) report[63] ^= report[i];
    }

    internal static bool Matches(ReadOnlySpan<byte> request, ReadOnlySpan<byte> reply) =>
        reply.Length == ReportLength && reply[0] == ReportId &&
        reply[2] == request[2] && reply[7] == request[7] && reply[8] == request[8];

    internal static bool MatchesMakoX(ReadOnlySpan<byte> request, ReadOnlySpan<byte> reply) =>
        reply.Length == MakoXReportLength && reply[0] == ReportId &&
        reply[2] == request[2] && reply[7] == request[7] && reply[8] == request[8];

    internal static byte[] ReadPayload(ReadOnlySpan<byte> reply)
    {
        if (reply.Length != ReportLength || reply[6] > 53)
            throw new InvalidDataException("Invalid Valkyrie T3 feature report length.");
        if (reply[1] != 2)
            throw new InvalidOperationException($"Valkyrie T3 command failed: status 0x{reply[1]:X2}.");
        return reply.Slice(9, reply[6]).ToArray();
    }

    internal static byte[] ReadPayloadMakoX(ReadOnlySpan<byte> reply)
    {
        if (reply.Length != MakoXReportLength || reply[6] > MakoXMaxPayload)
            throw new InvalidDataException("Invalid Mako X feature report length.");
        if (reply[1] != 2)
            throw new InvalidOperationException($"Mako X command failed: status 0x{reply[1]:X2}.");
        return reply.Slice(9, reply[6]).ToArray();
    }
}
