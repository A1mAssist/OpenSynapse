namespace OpenSynapse.Windows.Protocols;

public readonly record struct RazerWirelessDeviceConnectionStatus(byte Status, ushort ProductId);

public readonly record struct RazerWirelessConnectionState(byte ConnectionType, byte Status);

/// <summary>
/// Protocol 25 class 0x00 pairing and wireless-state commands from rzDevice25.
/// The middleware registers set pairing [3,0,65], unpair [2,0,66], multiple
/// wireless status V2 [80,0,191], get active state [2,0,199] and set active
/// state [2,0,71].
/// </summary>
public static class RazerPairingProtocol
{
    private const byte CommandClass = 0x00;
    private const byte SetDevicePairingModeCommand = 0x41;
    private const byte SetDeviceUnpairCommand = 0x42;
    private const byte GetMultipleDeviceWirelessConnectionStatusV2Command = 0xBF;
    private const byte GetActiveWirelessConnectionStateCommand = 0xC7;
    private const byte SetActiveWirelessConnectionStateCommand = 0x47;
    private const byte MultipleDeviceWirelessStatusSize = 80;

    public static byte[] CreateSetDevicePairingModeRequest(
        byte transactionId,
        byte mode,
        ushort productId) =>
        RazerFeatureReport.CreateRequest(
            transactionId,
            3,
            CommandClass,
            SetDevicePairingModeCommand,
            new byte[] { mode, (byte)(productId >> 8), (byte)productId });

    public static byte[] CreateSetDeviceUnpairRequest(byte transactionId, ushort productId) =>
        RazerFeatureReport.CreateRequest(
            transactionId,
            2,
            CommandClass,
            SetDeviceUnpairCommand,
            new byte[] { (byte)(productId >> 8), (byte)productId });

    public static byte[] CreateGetMultipleDeviceWirelessConnectionStatusV2Request(byte transactionId) =>
        RazerFeatureReport.CreateRequest(
            transactionId,
            MultipleDeviceWirelessStatusSize,
            CommandClass,
            GetMultipleDeviceWirelessConnectionStatusV2Command,
            ReadOnlySpan<byte>.Empty);

    public static byte[] CreateGetActiveWirelessConnectionStateRequest(byte transactionId) =>
        RazerFeatureReport.CreateRequest(
            transactionId,
            2,
            CommandClass,
            GetActiveWirelessConnectionStateCommand,
            new byte[2]);

    public static byte[] CreateSetActiveWirelessConnectionStateRequest(
        byte transactionId,
        byte connectionType,
        byte state) =>
        RazerFeatureReport.CreateRequest(
            transactionId,
            2,
            CommandClass,
            SetActiveWirelessConnectionStateCommand,
            new byte[] { connectionType, state });

    public static byte ParsePairingMode(ReadOnlySpan<byte> response, ReadOnlySpan<byte> request) =>
        Payload(response, request, 1)[0];

    public static IReadOnlyList<RazerWirelessDeviceConnectionStatus> ParseMultipleDeviceWirelessConnectionStatusV2(
        ReadOnlySpan<byte> response,
        ReadOnlySpan<byte> request)
    {
        var arguments = Payload(response, request, 1);
        var count = arguments[0];
        if (1 + (count * 3) > arguments.Length)
        {
            throw new InvalidOperationException("Razer multiple-device wireless status is truncated.");
        }

        var statuses = new RazerWirelessDeviceConnectionStatus[count];
        for (var index = 0; index < count; index++)
        {
            var offset = 1 + (index * 3);
            statuses[index] = new RazerWirelessDeviceConnectionStatus(
                arguments[offset],
                (ushort)((arguments[offset + 1] << 8) | arguments[offset + 2]));
        }

        return statuses;
    }

    public static RazerWirelessConnectionState ParseActiveWirelessConnectionState(
        ReadOnlySpan<byte> response,
        ReadOnlySpan<byte> request)
    {
        var arguments = Payload(response, request, 2);
        return new RazerWirelessConnectionState(arguments[0], arguments[1]);
    }

    private static ReadOnlySpan<byte> Payload(
        ReadOnlySpan<byte> response,
        ReadOnlySpan<byte> request,
        byte minimumArguments)
    {
        if (!RazerFeatureReport.Matches(request, response) ||
            !RazerFeatureReport.IsAcceptedStatus(response[1]) ||
            response[6] < minimumArguments ||
            response[6] > 80)
        {
            throw new InvalidOperationException("Razer wireless command returned an invalid or unsuccessful feature report.");
        }

        return response.Slice(RazerFeatureReport.ArgumentsOffset, response[6]);
    }
}
