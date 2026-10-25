using System.Text;

namespace OpenSynapse.Windows.Protocols;

public enum RazerAudioSource : byte
{
    None = 0,
    Spdif = 1,
    Aux = 2,
    Bluetooth = 3,
    Usb = 4,
}

public enum RazerAncState : byte
{
    Off = 0,
    On = 1,
    Ambient = 80,
}

/// <summary>
/// Razer's protocol 25 audio commands. The middleware builds them as the same 90-byte
/// OpenRazer envelope used by every other feature-report device: data size at 5, class at 6,
/// command at 7, arguments at 8, XOR checksum at 88 over bytes 2..87. This class emits the
/// 91-byte Windows report (report id prefix) and reads arguments from
/// <see cref="RazerFeatureReport.ArgumentsOffset"/>.
/// </summary>
public static class RazerHeadsetAudioProtocol
{
    public const byte ReadProfile = 0x00;
    public const byte WriteProfile = 0x00;
    public const int TenBandCount = 10;
    public const int EightBandCount = 8;

    public static readonly IReadOnlyList<int> TenBandFrequencies =
        [31, 63, 125, 250, 500, 1_000, 2_000, 4_000, 8_000, 16_000];

    public static readonly IReadOnlyList<int> EightBandFrequencies =
        [31, 63, 125, 250, 500, 1_000, 2_000, 4_000];

    public static byte[] GetFirmware(byte transactionId) =>
        Create(transactionId, 2, 0x00, 0x81);

    public static byte[] GetSerialNumber(byte transactionId) =>
        Create(transactionId, 22, 0x00, 0x82);

    public static byte[] GetBatteryLevel(byte transactionId) =>
        Create(transactionId, 2, 0x07, 0x80);

    public static byte[] GetChargingStatus(byte transactionId) =>
        Create(transactionId, 2, 0x07, 0x84);

    public static byte[] GetEqPreset(byte transactionId, byte profile = ReadProfile) =>
        Create(transactionId, 2, 0x08, 0x82, profile);

    public static byte[] SetEqPreset(byte transactionId, byte preset, byte profile = WriteProfile) =>
        Create(transactionId, 2, 0x08, 0x02, profile, preset);

    public static byte[] GetEqBands(byte transactionId, bool tenBand = true, byte profile = ReadProfile) =>
        Create(transactionId, BandDataSize(tenBand), 0x08, 0x84, profile);

    public static byte[] SetEqBands(
        byte transactionId,
        ReadOnlySpan<byte> bands,
        byte profile = WriteProfile)
    {
        if (bands.Length is not (TenBandCount or EightBandCount))
        {
            throw new ArgumentException("Equalizer data must contain 10 or 8 bands.", nameof(bands));
        }

        var arguments = new byte[bands.Length + 1];
        arguments[0] = profile;
        bands.CopyTo(arguments.AsSpan(1));
        return Create(transactionId, (byte)arguments.Length, 0x08, 0x04, arguments);
    }

    public static byte[] GetBass(byte transactionId, byte profile = ReadProfile) =>
        Create(transactionId, 2, 0x08, 0x87, profile);

    public static byte[] SetBass(byte transactionId, byte level, byte profile = WriteProfile) =>
        Create(transactionId, 2, 0x08, 0x07, profile, level);

    public static byte[] GetMicMute(byte transactionId, byte profile = ReadProfile) =>
        Create(transactionId, 2, 0x08, 0x88, profile);

    public static byte[] SetMicMute(byte transactionId, bool mute, byte profile = WriteProfile) =>
        Create(transactionId, 2, 0x08, 0x08, profile, mute ? (byte)1 : (byte)0);

    public static byte[] GetMicVolume(byte transactionId, byte profile = ReadProfile) =>
        Create(transactionId, 2, 0x08, 0x97, profile);

    public static byte[] SetMicVolume(byte transactionId, byte volume, byte profile = WriteProfile) =>
        Create(transactionId, 2, 0x08, 0x17, profile, volume);

    public static byte[] GetAudioSource(byte transactionId, byte profile = ReadProfile) =>
        Create(transactionId, 2, 0x08, 0x8A, profile);

    public static byte[] SetAudioSource(byte transactionId, RazerAudioSource source, byte profile = WriteProfile) =>
        Create(transactionId, 2, 0x08, 0x0A, profile, (byte)source);

    public static byte[] GetNoiseSuppression(byte transactionId, byte profile = ReadProfile) =>
        Create(transactionId, 2, 0x08, 0x9C, profile);

    public static byte[] SetNoiseSuppression(byte transactionId, bool enabled, byte profile = WriteProfile) =>
        Create(transactionId, 2, 0x08, 0x1C, profile, enabled ? (byte)1 : (byte)0);

    // The middleware declares one argument byte for this read but the firmware answers with
    // profileId, state and level.
    public static byte[] GetAnc(byte transactionId, byte profile = ReadProfile) =>
        Create(transactionId, 1, 0x18, 0x86, profile);

    public static byte[] SetAnc(
        byte transactionId,
        RazerAncState state,
        byte level,
        byte profile = WriteProfile) =>
        Create(transactionId, 3, 0x18, 0x06, profile, (byte)state, level);

    public static Version ReadFirmware(ReadOnlySpan<byte> response)
    {
        var data = Arguments(response);
        Require(data, 2);
        return new Version(data[0], data[1]);
    }

    public static string ReadSerialNumber(ReadOnlySpan<byte> response)
    {
        var data = Arguments(response);
        Require(data, 1);
        var length = Math.Min(data[0], Math.Min(data.Length - 1, 21));
        return Encoding.ASCII.GetString(data.Slice(1, length)).TrimEnd('\0');
    }

    public static byte ReadBatteryLevel(ReadOnlySpan<byte> response)
    {
        var data = Arguments(response);
        Require(data, 2);
        return data[1];
    }

    public static bool ReadChargingStatus(ReadOnlySpan<byte> response)
    {
        var data = Arguments(response);
        Require(data, 2);
        return data[1] != 0;
    }

    public static byte ReadEqPreset(ReadOnlySpan<byte> response)
    {
        var data = Arguments(response);
        Require(data, 2);
        return data[1];
    }

    public static byte[] ReadEqBands(ReadOnlySpan<byte> response, bool tenBand)
    {
        var data = Arguments(response);
        var expected = tenBand ? TenBandCount : EightBandCount;
        Require(data, expected + 1);
        return data.Slice(1, expected).ToArray();
    }

    public static byte ReadBass(ReadOnlySpan<byte> response)
    {
        var data = Arguments(response);
        Require(data, 2);
        return data[1];
    }

    public static bool ReadMicMute(ReadOnlySpan<byte> response)
    {
        var data = Arguments(response);
        Require(data, 2);
        return data[1] != 0;
    }

    public static byte ReadMicVolume(ReadOnlySpan<byte> response)
    {
        var data = Arguments(response);
        Require(data, 2);
        return data[1];
    }

    public static RazerAudioSource ReadAudioSource(ReadOnlySpan<byte> response)
    {
        var data = Arguments(response);
        Require(data, 2);
        return (RazerAudioSource)data[1];
    }

    public static bool ReadNoiseSuppression(ReadOnlySpan<byte> response)
    {
        var data = Arguments(response);
        Require(data, 2);
        return data[1] != 0;
    }

    public static (RazerAncState State, byte Level) ReadAnc(ReadOnlySpan<byte> response)
    {
        var data = Arguments(response);
        Require(data, 3);
        return ((RazerAncState)data[1], data[2]);
    }

    private static byte BandDataSize(bool tenBand) => tenBand ? (byte)11 : (byte)9;

    private static byte[] Create(
        byte transactionId,
        byte dataSize,
        byte commandClass,
        byte commandId,
        params byte[] arguments) =>
        RazerFeatureReport.CreateRequest(transactionId, dataSize, commandClass, commandId, arguments);

    private static ReadOnlySpan<byte> Arguments(ReadOnlySpan<byte> response)
    {
        if (response.Length != RazerFeatureReport.Length)
        {
            throw new InvalidDataException(
                $"Razer feature reports are {RazerFeatureReport.Length} bytes; got {response.Length}.");
        }

        return response[RazerFeatureReport.ArgumentsOffset..];
    }

    private static void Require(ReadOnlySpan<byte> data, int length)
    {
        if (data.Length < length)
        {
            throw new InvalidDataException($"Razer response has {data.Length} data bytes; {length} required.");
        }
    }
}
