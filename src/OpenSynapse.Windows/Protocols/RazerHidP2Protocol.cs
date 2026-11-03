namespace OpenSynapse.Windows.Protocols;

/// <summary>
/// Synapse 2 P2 profile packets use a six-byte header. The RzUdd 0x88883024
/// transfer wraps that packet in a 91-byte P2 envelope; it is not the P1 class/id
/// frame. Only the four confirmed read commands are exposed. The builders in this
/// file have no write flag and the transport rejects a packet whose read flag is
/// clear.
/// </summary>
internal static class RazerHidP2Protocol
{
    internal const int HeaderLength = 6;
    internal const byte ReadFlag = 0x01;

    internal const byte GetActiveProfileCommand = 0x05;
    internal const byte GetProfileDataCommand = 0x06;
    internal const byte GetProfileNameCommand = 0x22;
    internal const byte GetCallbackStateCommand = 0x29;

    internal const int ActiveProfilePayloadLength = 5;
    internal const int ProfileDataPayloadLength = 72;
    // The get/set name packets carry the profile index plus the 40-byte name.
    internal const int ProfileNamePayloadLength = 41;
    internal const int CallbackStatePayloadLength = 1;

    internal const int TransportLength = RazerFeatureReport.Length;
    private const int TransportDataOffset = RazerFeatureReport.ArgumentsOffset;
    private const int TransportDataLength = TransportLength - TransportDataOffset - 2;

    internal static byte[] CreateReadActiveProfilePacket() =>
        CreateReadPacket(GetActiveProfileCommand, new byte[ActiveProfilePayloadLength]);

    internal static byte[] CreateReadProfileDataPacket(byte profileId)
    {
        var payload = new byte[ProfileDataPayloadLength];
        payload[0] = profileId;
        return CreateReadPacket(GetProfileDataCommand, payload);
    }

    internal static byte[] CreateReadProfileNamePacket(byte profileId)
    {
        var payload = new byte[ProfileNamePayloadLength];
        payload[0] = profileId;
        return CreateReadPacket(GetProfileNameCommand, payload);
    }

    internal static byte[] CreateReadCallbackStatePacket() =>
        CreateReadPacket(GetCallbackStateCommand, new byte[CallbackStatePayloadLength]);

    internal static byte[] CreateReadPacket(byte command, ReadOnlySpan<byte> payload)
    {
        if (payload.Length > byte.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(payload));
        }

        var packet = new byte[HeaderLength + payload.Length];
        packet[0] = command;
        packet[1] = (byte)payload.Length;
        packet[2] = ReadFlag;
        payload.CopyTo(packet.AsSpan(HeaderLength));
        return packet;
    }

    /// <summary>
    /// Guard for the read-only transport: a packet with the set flag, or a payload
    /// length that does not match the buffer, never reaches the driver.
    /// </summary>
    internal static void ValidateReadPacket(ReadOnlySpan<byte> packet)
    {
        if (packet.Length < HeaderLength ||
            packet[1] != packet.Length - HeaderLength ||
            packet[2] != ReadFlag)
        {
            throw new ArgumentException(
                "The P2 packet must be a read with a payload length matching its buffer.",
                nameof(packet));
        }
    }

    /// <summary>
    /// Wraps the six-byte P2 packet in the 91-byte envelope CRzIoctl sends through
    /// 0x88883024. The mapping is the one in rzdevicedll FUN_100f2fe0: command at
    /// 4, payload length at 5, payload at 9, checksum at 89 and the trailing 1 at 90.
    /// </summary>
    internal static byte[] CreateTransportFrame(ReadOnlySpan<byte> packet)
    {
        ValidateReadPacket(packet);
        if (packet[1] > TransportDataLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(packet),
                "A P2 transport frame carries at most 80 payload bytes.");
        }

        var frame = new byte[TransportLength];
        frame[1] = 0x01;
        frame[3] = packet[2];
        frame[4] = packet[0];
        frame[5] = packet[1];
        frame[6] = packet[4];
        frame[7] = packet[3];
        frame[8] = packet[5];
        packet[HeaderLength..].CopyTo(frame.AsSpan(TransportDataOffset));
        frame[TransportLength - 2] = RazerFeatureReport.CalculateCrc(frame);
        frame[TransportLength - 1] = 0x01;
        return frame;
    }

    /// <summary>
    /// Converts a 91-byte P2 response envelope back to the six-byte packet header
    /// shape so callers can use the same parsers as the handoff packet table.
    /// </summary>
    internal static byte[] ParseTransportFrame(ReadOnlySpan<byte> frame, ReadOnlySpan<byte> requestPacket)
    {
        ValidateReadPacket(requestPacket);
        if (frame.Length != TransportLength ||
            frame[0] != 0 ||
            !RazerFeatureReport.IsAcceptedStatus(frame[1]) ||
            frame[3] != ReadFlag ||
            frame[4] != requestPacket[0] ||
            frame[5] != requestPacket[1] ||
            !RazerFeatureReport.IsAcceptedChecksum(frame))
        {
            throw new InvalidDataException("The P2 transport response header or checksum is invalid.");
        }
        if (frame[5] > TransportDataLength)
        {
            throw new InvalidDataException("The P2 transport response payload is too long.");
        }

        var response = new byte[HeaderLength + frame[5]];
        response[0] = frame[4];
        response[1] = frame[5];
        response[2] = frame[3];
        response[3] = frame[7];
        response[4] = frame[6];
        response[5] = frame[8];
        frame.Slice(TransportDataOffset, frame[5]).CopyTo(response.AsSpan(HeaderLength));
        return response;
    }

    internal static byte[] ParseActiveProfile(ReadOnlySpan<byte> response) =>
        ReadPayload(response, GetActiveProfileCommand, ActiveProfilePayloadLength).ToArray();

    internal static byte[] ParseProfileData(ReadOnlySpan<byte> response) =>
        ReadPayload(response, GetProfileDataCommand, ProfileDataPayloadLength).ToArray();

    internal static byte[] ParseProfileName(ReadOnlySpan<byte> response)
    {
        var payload = ReadPayload(response, GetProfileNameCommand, ProfileNamePayloadLength);
        return payload[1..].ToArray();
    }

    internal static bool ParseCallbackState(ReadOnlySpan<byte> response) =>
        ReadPayload(response, GetCallbackStateCommand, CallbackStatePayloadLength)[0] != 0;

    private static ReadOnlySpan<byte> ReadPayload(
        ReadOnlySpan<byte> response,
        byte command,
        int payloadLength)
    {
        if (response.Length != HeaderLength + payloadLength ||
            response[0] != command ||
            response[1] != payloadLength ||
            response[2] != ReadFlag)
        {
            throw new InvalidDataException("The P2 response header or length is invalid.");
        }

        return response[HeaderLength..];
    }
}
