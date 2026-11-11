namespace OpenSynapse.Windows.Protocols;

// Local vendor source: bundles/056B/rzDevice25ValkyrieT3.6a7d2e9e843af8d2634a.js,
// _createDataSend / _getUSBTransferInResult. This is NOT the MXIC 50 41 envelope.
internal static class RazerValkyrieT3Protocol
{
    internal const int ReportLength = 64;
    internal const byte ReportId = 7;

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

    internal static bool Matches(ReadOnlySpan<byte> request, ReadOnlySpan<byte> reply) =>
        reply.Length == ReportLength && reply[0] == ReportId &&
        reply[2] == request[2] && reply[7] == request[7] && reply[8] == request[8];

    internal static byte[] ReadPayload(ReadOnlySpan<byte> reply)
    {
        if (reply.Length != ReportLength || reply[6] > 53)
            throw new InvalidDataException("Invalid Valkyrie T3 feature report length.");
        if (reply[1] != 2)
            throw new InvalidOperationException($"Valkyrie T3 command failed: status 0x{reply[1]:X2}.");
        return reply.Slice(9, reply[6]).ToArray();
    }

}
