namespace OpenSynapse.Windows.Protocols;

public static partial class RazerBleProtocol30
{
    public static byte[] CreateGetKeyList(byte transactionId, byte profileId) =>
        CreateFrame(transactionId, 0x07, 0x80, param6: profileId);

    public static IReadOnlyList<byte> ReadKeyList(RazerBleResponse response)
    {
        RequireKeyMappingReply(response, 0x80);
        var keys = response.Payload;
        if (keys.Any(key => key == 0) || keys.Distinct().Count() != keys.Count)
            throw new InvalidOperationException("The BLE keyboard returned an invalid key list.");
        return keys.ToArray();
    }

    public static byte[] CreateGetKeyAssignment(byte transactionId, byte profileId, byte keyId, byte mode)
    {
        ValidateKeyMapping(keyId, mode);
        return CreateFrame(transactionId, 0x07, 0x81, param6: profileId, param7: keyId);
    }

    public static RazerBleButtonAssignment ReadKeyAssignment(RazerBleResponse response, byte keyId, byte mode)
    {
        ValidateKeyMapping(keyId, mode);
        RequireKeyMappingReply(response, 0x81);
        // V1 replies carry [mode, function, data...]; the key/profile are only in the request.
        var data = response.Payload;
        if (data.Count is < 2 or > 7 || data[0] != mode)
            throw new InvalidOperationException("The BLE key mapping is truncated or belongs to another mode.");
        return new(keyId, mode, data[1], data.Skip(2).ToArray());
    }

    public static byte[] CreateSetKeyAssignment(byte transactionId, byte profileId, byte keyId,
        byte mode, byte functionId, ReadOnlySpan<byte> data)
    {
        ValidateKeyMapping(keyId, mode);
        if (data.Length > 5) throw new ArgumentOutOfRangeException(nameof(data));
        byte[] payload = [functionId, .. data];
        return CreateFrame(transactionId, 0x07, 0x01, param6: profileId, param7: keyId, payload: payload);
    }

    private static void ValidateKeyMapping(byte keyId, byte mode)
    {
        if (keyId == 0) throw new ArgumentOutOfRangeException(nameof(keyId));
        if (mode != 0) throw new NotSupportedException("This BLE keyboard mapping protocol supports only the normal layer.");
    }

    internal static void RequireKeyMappingReply(RazerBleResponse response, byte commandId)
    {
        if (!response.IsSuccess || response.CommandClass != 0x07 || response.CommandId != commandId)
            throw new InvalidOperationException("The BLE reply does not match the keyboard mapping command.");
    }
}
