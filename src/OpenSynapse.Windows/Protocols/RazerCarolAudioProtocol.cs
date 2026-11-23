namespace OpenSynapse.Windows.Protocols;

public readonly record struct RazerCarolPowerState(bool Enabled, ushort Seconds, byte Status);

/// <summary>0570/05A2 rzDevice25Carol overrides and inherited power commands.</summary>
public static class RazerCarolAudioProtocol
{
    public static bool IsSupported(ushort productId) => productId is 0x0570 or 0x05A2;
    public static bool HasBattery(ushort productId) => productId == 0x0570;
    public static bool IsPreset(byte preset) => preset is 1 or 2 or 3 or 16;

    // Carol's JS uses Math.round(level * 10), including ties toward positive infinity.
    public static byte EncodeBand(double decibels)
    {
        if (!double.IsFinite(decibels) || decibels is < -12.8 or > 12.7)
            throw new ArgumentOutOfRangeException(nameof(decibels));
        return unchecked((byte)(sbyte)Math.Floor(decibels * 10 + 0.5));
    }

    public static double DecodeBand(byte value) => unchecked((sbyte)value) / 10d;

    public static byte[] GetLink(byte transaction) => Create(transaction, 1, 0, 0xB8);
    public static byte[] GetExternalPower(byte transaction) => Create(transaction, 1, 0, 0xB7);
    public static byte[] GetAutoSleep(byte transaction) => Create(transaction, 3, 7, 0x88);
    public static byte[] SetAutoSleep(byte transaction, bool enabled) =>
        Create(transaction, 3, 7, 8, [0, enabled ? (byte)1 : (byte)0, 0]);
    public static byte[] GetSleepSeconds(byte transaction) => Create(transaction, 2, 7, 0x83);
    public static byte[] SetSleepSeconds(byte transaction, ushort seconds) =>
        Create(transaction, 2, 7, 3, [(byte)(seconds >> 8), (byte)seconds]);
    public static byte[] KeepAwake(byte transaction) => Create(transaction, 0, 7, 0xA1);

    public static byte ReadLink(ReadOnlySpan<byte> response) => Data(response, 1)[0];
    public static byte ReadExternalPower(ReadOnlySpan<byte> response) => Data(response, 1)[0];

    public static (bool Enabled, byte Status) ReadAutoSleep(ReadOnlySpan<byte> response)
    {
        var data = Data(response, 3);
        if (data[0] != 0 || data[1] > 1) throw new InvalidDataException("Invalid Carol auto-sleep state.");
        return (data[1] == 1, data[2]);
    }

    public static ushort ReadSleepSeconds(ReadOnlySpan<byte> response)
    {
        var data = Data(response, 2);
        return (ushort)(data[0] * 256 + data[1]);
    }

    public static bool ReadCharging(ReadOnlySpan<byte> response)
    {
        var data = Data(response, 2);
        if (data[0] != 0) throw new InvalidDataException("Carol returned another battery ID.");
        return data[1] is 1 or 4 or 5;
    }

    internal static void ValidateResponse(byte[] request, byte[] response)
    {
        if (!RazerProtocol25FeatureProtocol.Matches(request, response) || response[1] != 2)
            throw new InvalidDataException("Carol returned an unrelated or unsuccessful response.");
        // EQ and battery/power selectors are profile/battery 0 in the actual middleware calls.
        if ((request[7] == 8 || request[7] == 7 && request[8] is 0x80 or 0x84 or 0x88 or 8) &&
            response[9] != request[9])
            throw new InvalidDataException("Carol returned another profile or battery ID.");
    }

    private static ReadOnlySpan<byte> Data(ReadOnlySpan<byte> response, byte size)
    {
        if (response.Length != 65 || response[0] != 7 || response[1] != 2 || response[6] != size)
            throw new InvalidDataException("Incomplete Carol response.");
        return response.Slice(9, size);
    }

    private static byte[] Create(byte transaction, byte size, byte commandClass, byte command, byte[]? data = null) =>
        RazerFeatureReport.CreateRequest(transaction, size, commandClass, command, data ?? []);
}
