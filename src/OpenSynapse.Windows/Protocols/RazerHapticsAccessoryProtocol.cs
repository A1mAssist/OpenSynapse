namespace OpenSynapse.Windows.Protocols;

internal sealed record RazerHapticsState(
    int Mode,
    bool IntensityEnabled,
    int Intensity,
    int Gain,
    int Perception,
    int Transient,
    int SignalGain,
    string? Error);

/// <summary>
/// Sensa HD haptics on Freyja (0F45) and Soma Sensa HD (0F6D). The commands come from
/// the bundle's Interhaptics class 0x16 table; scalar gain fields are little-endian on
/// writes and big-endian on reads, matching the middleware helpers.
/// </summary>
internal static class RazerHapticsAccessoryProtocol
{
    internal const byte CommandClass = 0x16;
    internal const byte GetMode = 0x87;
    internal const byte SetMode = 0x07;
    internal const byte GetGain = 0x88;
    internal const byte SetGain = 0x08;
    internal const byte GetIntensity = 0xA5;
    internal const byte SetIntensity = 0x25;
    internal const byte GetPerception = 0x89;
    internal const byte SetPerception = 0x09;
    internal const byte GetTransient = 0x8A;
    internal const byte SetTransient = 0x0A;
    internal const byte GetSignalGain = 0x8B;
    internal const byte SetSignalGain = 0x0B;
    internal const byte GetBandVolume = 0x8C;
    internal const byte SetBandVolume = 0x0C;
    internal const byte GetEqualization = 0x8D;
    internal const byte SetEqualization = 0x0D;

    internal static byte[] CreateGetMode() => Request(GetMode, 0, []);
    internal static byte[] CreateSetMode(int mode) => Request(SetMode, 1, [checked((byte)mode)]);
    internal static byte[] CreateGetIntensity() => Request(GetIntensity, 0, []);
    internal static byte[] CreateSetIntensity(bool enabled, int intensity) =>
        Request(SetIntensity, 2, [enabled ? (byte)1 : (byte)0, checked((byte)intensity)]);
    internal static byte[] CreateGetScalar(byte commandId, int bodyPartId = 0) =>
        Request(commandId, 3, [checked((byte)bodyPartId), 0, 0]);
    internal static byte[] CreateSetScalar(byte commandId, int value, int bodyPartId = 0) =>
        Request(
            commandId,
            5,
            [
                0,
                checked((byte)(bodyPartId & 0xFF)),
                checked((byte)(bodyPartId >> 8)),
                checked((byte)(value & 0xFF)),
                checked((byte)(value >> 8)),
            ]);
    internal static byte[] CreateGetBandVolume(int profileId = 0) =>
        Request(GetBandVolume, 1, [checked((byte)profileId)]);
    internal static byte[] CreateSetBandVolume(IReadOnlyList<int> values, int profileId = 0)
    {
        if (values.Count > 25)
        {
            throw new ArgumentOutOfRangeException(nameof(values));
        }

        var payload = new byte[2 + (values.Count * 2)];
        payload[0] = checked((byte)profileId);
        payload[1] = checked((byte)values.Count);
        for (var index = 0; index < values.Count; index++)
        {
            payload[2 + (index * 2)] = checked((byte)(values[index] & 0xFF));
            payload[3 + (index * 2)] = checked((byte)(values[index] >> 8));
        }

        return Request(SetBandVolume, checked((byte)payload.Length), payload);
    }

    internal static RazerHapticsState ParseState(
        int mode,
        bool intensityEnabled,
        int intensity,
        int gain,
        int perception,
        int transient,
        int signal,
        string? error) =>
        new(mode, intensityEnabled, intensity, gain, perception, transient, signal, error);

    internal static int ReadMode(byte[] response) => Data(response)[0];
    internal static (bool Enabled, int Value) ReadIntensity(byte[] response)
    {
        var data = Data(response);
        return (data[0] != 0, data[1]);
    }

    internal static int ReadBigEndianScalar(byte[] response, int offset = 3)
    {
        var data = Data(response);
        return data.Length < offset + 2
            ? throw new InvalidDataException("Sensa scalar response is too short.")
            : (data[offset] << 8) | data[offset + 1];
    }

    internal static IReadOnlyList<int> ReadBandVolumes(byte[] response)
    {
        var data = Data(response);
        if (data.Length < 2)
        {
            return [];
        }

        var count = data[1];
        if (data.Length < 2 + (count * 2))
        {
            throw new InvalidDataException("Sensa band response is truncated.");
        }

        var values = new int[count];
        for (var index = 0; index < count; index++)
        {
            values[index] = (data[2 + (index * 2)] << 8) | data[3 + (index * 2)];
        }

        return values;
    }

    private static byte[] Request(byte commandId, byte dataSize, ReadOnlySpan<byte> arguments) =>
        RazerAccessoryProtocol.CreateRequest(
            RazerAccessoryProtocol.ReportLength91,
            0,
            0x1F,
            dataSize,
            CommandClass,
            commandId,
            arguments);

    private static ReadOnlySpan<byte> Data(byte[] response) =>
        RazerAccessoryProtocol.Arguments(response);
}
