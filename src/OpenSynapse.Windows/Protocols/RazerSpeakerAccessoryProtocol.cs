namespace OpenSynapse.Windows.Protocols;

internal sealed record RazerSpeakerState(
    int? Volume,
    bool? Muted,
    int? Bass,
    int? EqPreset,
    int? InputSource,
    int? OutputSource,
    int? MicVolume,
    bool? MicMuted,
    string? Error);

/// <summary>
/// Class 0x08 audio controls shared by Soma Chroma (0F64) and the Nommo Chroma/Pro
/// families (0517/0518). The two-byte command pair is [profileId, value]; the 65-byte
/// devices keep the same command table on the shorter report.
/// </summary>
internal static class RazerSpeakerAccessoryProtocol
{
    internal const byte CommandClass = 0x08;
    internal const byte GetVolume = 0x86;
    internal const byte SetVolume = 0x06;
    internal const byte GetBass = 0x87;
    internal const byte SetBass = 0x07;
    internal const byte GetEqPreset = 0x82;
    internal const byte SetEqPreset = 0x02;
    internal const byte GetEqSetting = 0x84;
    internal const byte SetEqSetting = 0x04;
    internal const byte GetMute = 0x89;
    internal const byte SetMute = 0x09;
    internal const byte GetInputSource = 0x8A;
    internal const byte SetInputSource = 0x0A;
    internal const byte GetOutputSource = 0x91;
    internal const byte SetOutputSource = 0x11;
    internal const byte GetMicVolume = 0x97;
    internal const byte SetMicVolume = 0x17;
    internal const byte GetMicMute = 0x88;
    internal const byte SetMicMute = 0x08;
    internal const byte GetInputControl = 0x96;
    internal const byte SetInputControl = 0x16;
    internal const byte GetAudioIo = 0x8B;

    internal static byte[] CreateGet(byte commandId, int reportLength) =>
        Request(reportLength, commandId, 2, [0, 0]);

    internal static byte[] CreateSet(byte commandId, int value, int reportLength) =>
        Request(reportLength, commandId, 2, [0, checked((byte)value)]);

    internal static byte[] CreateSetBool(byte commandId, bool value, int reportLength) =>
        CreateSet(commandId, value ? 1 : 0, reportLength);

    internal static byte[] CreateSetEqSetting(
        int reportLength,
        IReadOnlyList<int> bands)
    {
        if (bands.Count is < 1 or > 10)
        {
            throw new ArgumentOutOfRangeException(nameof(bands));
        }

        var payload = new byte[1 + bands.Count];
        payload[0] = 0;
        for (var index = 0; index < bands.Count; index++)
        {
            payload[index + 1] = checked((byte)bands[index]);
        }

        return Request(reportLength, SetEqSetting, checked((byte)payload.Length), payload);
    }

    internal static int ReadScalar(byte[] response)
    {
        var data = RazerAccessoryProtocol.Arguments(response);
        return data.Length switch
        {
            0 => 0,
            1 => data[0],
            _ => data[1],
        };
    }

    internal static bool ReadBool(byte[] response) => ReadScalar(response) != 0;

    internal static IReadOnlyList<int> ReadEqBands(byte[] response)
    {
        var data = RazerAccessoryProtocol.Arguments(response);
        return data.Length <= 1
            ? []
            : data[1..].ToArray().Select(value => (int)value).ToArray();
    }

    private static byte[] Request(
        int reportLength,
        byte commandId,
        byte dataSize,
        ReadOnlySpan<byte> arguments) =>
        RazerAccessoryProtocol.CreateRequest(
            reportLength,
            reportLength == RazerAccessoryProtocol.ReportLength65 ? (byte)0x07 : (byte)0,
            0x1F,
            dataSize,
            CommandClass,
            commandId,
            arguments);
}
