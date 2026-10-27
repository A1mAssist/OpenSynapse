namespace OpenSynapse.Windows.Protocols;

/// <summary>
/// Protocol 30 BLE framing from rzDevice30._createDataSend. Requests use an 8-byte
/// header and no checksum or HID report id. Replies normally use a 20-byte header and
/// retry with an 8-byte header when the declared payload length matches the shorter form.
/// </summary>
public static class RazerReport30Framing
{
    public const int RequestHeaderLength = 8;
    public const int ResponseHeaderLength = 20;
    public const int MaximumPayloadLength = 0xFFFFFF;
    public const byte MaximumTransactionId = 31;
    public const byte StatusSuccessful = 0x02;

    /// <summary>
    /// Builds one request. The first three parameters are the middleware tuple
    /// (command class, command id, payload); the remaining values map to the optional
    /// header selectors and transaction id.
    /// </summary>
    public static byte[] CreateRequest(
        byte commandClass,
        byte commandId,
        ReadOnlySpan<byte> payload,
        byte transactionId = 0,
        byte header6 = 0,
        byte header7 = 0)
    {
        if (transactionId > MaximumTransactionId)
        {
            throw new ArgumentOutOfRangeException(
                nameof(transactionId),
                "Protocol 30 transaction ids are 0..31.");
        }
        if (payload.Length > MaximumPayloadLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(payload),
                "Protocol 30 payloads fit in a 24-bit length.");
        }

        var frame = new byte[RequestHeaderLength + payload.Length];
        frame[0] = transactionId;
        frame[1] = (byte)payload.Length;
        frame[2] = (byte)(payload.Length >> 8);
        frame[3] = (byte)(payload.Length >> 16);
        frame[4] = commandClass;
        frame[5] = commandId;
        frame[6] = header6;
        frame[7] = header7;
        payload.CopyTo(frame.AsSpan(RequestHeaderLength));
        return frame;
    }

    /// <summary>
    /// Validates a successful reply against the request and exposes its payload.
    /// The middleware prefers the 20-byte header; an 8-byte header is accepted when
    /// the declared length matches that shorter form.
    /// </summary>
    public static bool TryValidateResponse(
        ReadOnlySpan<byte> request,
        ReadOnlySpan<byte> response,
        out ReadOnlySpan<byte> payload)
    {
        payload = default;
        if (request.Length < RequestHeaderLength || response.Length < RequestHeaderLength)
        {
            return false;
        }

        var declaredLength = response[1] | (response[2] << 8) | (response[3] << 16);
        var headerLength = -1;
        if (response.Length >= ResponseHeaderLength &&
            declaredLength == response.Length - ResponseHeaderLength)
        {
            headerLength = ResponseHeaderLength;
        }
        else if (declaredLength == response.Length - RequestHeaderLength)
        {
            headerLength = RequestHeaderLength;
        }
        else if (response.Length >= ResponseHeaderLength && declaredLength == 0)
        {
            headerLength = ResponseHeaderLength;
        }

        if (headerLength < 0 ||
            response[0] != request[0] ||
            response[4] != request[4] ||
            response[5] != request[5] ||
            response[7] != StatusSuccessful)
        {
            return false;
        }

        payload = response[headerLength..];
        return true;
    }
}
