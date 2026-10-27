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

/// <summary>One 08:9A parametric EQ record; gain is signed tenths of a decibel and Q is tenths.</summary>
public readonly record struct RazerParametricEqBand(
    byte Profile,
    byte Direction,
    byte Index,
    byte BandStatus,
    ushort Frequency,
    sbyte GainTenths,
    byte QFactorTenths,
    byte FilterType)
{
    public bool Enabled => BandStatus == 1;
}

/// <summary>One 08:9D compressor setting; db is zero or negative and ratio is tenths.</summary>
public readonly record struct RazerMicCompressor(
    byte Profile,
    byte Mode,
    sbyte Db,
    ushort RatioTenths,
    ushort AttackTime,
    ushort ReleaseTime)
{
    public bool Enabled => Mode != 0;
}

public enum RazerMicReverbRoomMode : byte
{
    Small = 0,
    Medium = 1,
    Large = 2,
    Plate = 3,
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
    public const int ParametricEqBandCount = 10;
    public const int ParametricEqSettingSize = 9;
    public const int MicCompressorSize = 9;
    public const int MicReverbSize = 3;
    public const byte MicDeEsserMax = 100;
    public const byte MicReverbIntensityMax = 100;
    public const sbyte MicLimiterMinDb = -60;
    public const int DolbyStateSize = 11;
    public const byte AudioPromptAll = 0xFF;

    /// <summary>Audio-input control source id the middleware uses for AI noise suppression.</summary>
    public const byte NoiseSuppressionSource = 3;

    /// <summary>AI noise suppression is an amount, not a switch; 100 is full strength.</summary>
    public const byte NoiseSuppressionFullAmount = 100;

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

    // The middleware allocates nine bytes for this read but seeds only the selector.
    public static byte[] GetParametricEqSetting(
        byte transactionId,
        byte band,
        byte direction = 0x00,
        byte profile = ReadProfile) =>
        Create(transactionId, ParametricEqSettingSize, 0x08, 0x9A, profile, direction, band);

    public static byte[] SetParametricEqSetting(byte transactionId, RazerParametricEqBand band) =>
        Create(
            transactionId,
            ParametricEqSettingSize,
            0x08,
            0x1A,
            band.Profile,
            band.Direction,
            band.Index,
            band.BandStatus,
            (byte)(band.Frequency >> 8),
            (byte)band.Frequency,
            unchecked((byte)band.GainTenths),
            band.QFactorTenths,
            band.FilterType);

    // The middleware allocates the full nine-byte compressor payload for reads and seeds the profile.
    public static byte[] GetMicCompressor(byte transactionId, byte profile = ReadProfile) =>
        Create(transactionId, MicCompressorSize, 0x08, 0x9D, profile);

    public static byte[] SetMicCompressor(byte transactionId, RazerMicCompressor compressor)
    {
        if (compressor.Db > 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(compressor), "Razer mic compressor db must be zero or negative.");
        }

        return Create(
            transactionId,
            MicCompressorSize,
            0x08,
            0x1D,
            compressor.Profile,
            compressor.Mode,
            unchecked((byte)compressor.Db),
            (byte)(compressor.RatioTenths >> 8),
            (byte)compressor.RatioTenths,
            (byte)(compressor.AttackTime >> 8),
            (byte)compressor.AttackTime,
            (byte)(compressor.ReleaseTime >> 8),
            (byte)compressor.ReleaseTime);
    }

    public static byte[] GetMicDeEsser(byte transactionId, byte profile = ReadProfile) =>
        Create(transactionId, 2, 0x08, 0x9E, profile);

    public static byte[] SetMicDeEsser(byte transactionId, byte value, byte profile = WriteProfile)
    {
        if (value > MicDeEsserMax)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value), $"Razer mic de-esser value must be between 0 and {MicDeEsserMax}.");
        }

        return Create(transactionId, 2, 0x08, 0x1E, profile, value);
    }

    public static byte[] GetMicReverb(byte transactionId, byte profile = ReadProfile) =>
        Create(transactionId, MicReverbSize, 0x08, 0x9F, profile);

    public static byte[] SetMicReverb(
        byte transactionId,
        RazerMicReverbRoomMode roomMode,
        byte intensity,
        byte profile = WriteProfile)
    {
        if (!Enum.IsDefined(roomMode))
        {
            throw new ArgumentOutOfRangeException(nameof(roomMode));
        }

        if (intensity > MicReverbIntensityMax)
        {
            throw new ArgumentOutOfRangeException(
                nameof(intensity), $"Razer mic reverb intensity must be between 0 and {MicReverbIntensityMax}.");
        }

        return Create(transactionId, MicReverbSize, 0x08, 0x1F, profile, (byte)roomMode, intensity);
    }

    public static byte[] GetMicLimiter(byte transactionId, byte profile = ReadProfile) =>
        Create(transactionId, 2, 0x08, 0xA0, profile);

    public static byte[] SetMicLimiter(
        byte transactionId,
        sbyte thresholdDb,
        byte profile = WriteProfile)
    {
        if (thresholdDb is < MicLimiterMinDb or > 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(thresholdDb), $"Razer mic limiter threshold must be between {MicLimiterMinDb} and 0 dB.");
        }

        return Create(transactionId, 2, 0x08, 0x20, profile, unchecked((byte)thresholdDb));
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

    // The switch lives on the audio-input control command; 08:1C only carries the amount.
    public static byte[] GetAudioInputControlState(
        byte transactionId,
        byte sourceId = NoiseSuppressionSource,
        byte profile = ReadProfile) =>
        Create(transactionId, 3, 0x08, 0x96, profile, sourceId);

    public static byte[] SetAudioInputControlState(
        byte transactionId,
        bool enabled,
        byte sourceId = NoiseSuppressionSource,
        byte profile = WriteProfile) =>
        Create(transactionId, 3, 0x08, 0x16, profile, sourceId, enabled ? (byte)1 : (byte)0);

    public static byte[] SetNoiseSuppression(byte transactionId, bool enabled, byte profile = WriteProfile) =>
        Create(transactionId, 2, 0x08, 0x1C, profile,
            enabled ? NoiseSuppressionFullAmount : (byte)0);

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

    // The family's device handlers write 24:05; the shared rzDevice25 table's 24:15 is
    // not referenced by those handlers.
    public static byte[] GetAudioDnd(byte transactionId, byte profile = ReadProfile) =>
        Create(transactionId, 1, 0x18, 0x85, profile);

    public static byte[] SetAudioDnd(
        byte transactionId,
        byte state,
        byte profile = WriteProfile)
    {
        RequireOnOff(state, nameof(state));
        return Create(transactionId, 2, 0x18, 0x05, profile, state);
    }

    // Prompt index 255 addresses every prompt; responses carry profile, index and state.
    public static byte[] GetAudioVoicePrompts(
        byte transactionId,
        byte promptIndex = AudioPromptAll,
        byte profile = ReadProfile) =>
        Create(transactionId, 2, 0x18, 0x90, profile, promptIndex);

    public static byte[] SetAudioVoicePrompts(
        byte transactionId,
        byte state,
        byte promptIndex = AudioPromptAll,
        byte profile = WriteProfile)
    {
        RequireOnOff(state, nameof(state));
        return Create(transactionId, 3, 0x18, 0x10, profile, promptIndex, state);
    }

    public static byte[] GetAudioAutoPause(byte transactionId, byte profile = ReadProfile) =>
        Create(transactionId, 1, 0x18, 0x9E, profile);

    public static byte[] SetAudioAutoPause(
        byte transactionId,
        byte state,
        byte profile = WriteProfile)
    {
        RequireOnOff(state, nameof(state));
        return Create(transactionId, 2, 0x18, 0x1E, profile, state);
    }

    // The handler allocates two bytes for this read but seeds only the profile.
    public static byte[] GetAudioGestureControl(byte transactionId, byte profile = ReadProfile) =>
        Create(transactionId, 2, 0x18, 0xA2, profile);

    public static byte[] SetAudioGestureControl(
        byte transactionId,
        byte state,
        byte profile = WriteProfile)
    {
        RequireOnOff(state, nameof(state));
        return Create(transactionId, 2, 0x18, 0x22, profile, state);
    }

    // The handler allocates the full eleven-byte Dolby payload and seeds two bytes.
    public static byte[] GetDolbyState(byte transactionId, byte profile = ReadProfile) =>
        Create(transactionId, DolbyStateSize, 0x08, 0x8C, profile);

    public static byte[] SetDolbyState(
        byte transactionId,
        byte state,
        byte profile = WriteProfile)
    {
        RequireOnOff(state, nameof(state));
        return Create(transactionId, DolbyStateSize, 0x08, 0x0C, profile, state);
    }

    public static byte[] GetEqNightMode(byte transactionId, byte profile = ReadProfile) =>
        Create(transactionId, 2, 0x08, 0x92, profile);

    public static byte[] SetEqNightMode(
        byte transactionId,
        byte state,
        byte profile = WriteProfile)
    {
        RequireOnOff(state, nameof(state));
        return Create(transactionId, 2, 0x08, 0x12, profile, state);
    }

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

    // Class 7 responses carry the battery id at data[0] and the value at data[1];
    // the middleware scales that raw value (0..255) to percent.
    public static byte ReadBatteryPercent(ReadOnlySpan<byte> response)
    {
        var data = Arguments(response);
        Require(data, 2);
        return (byte)(data[1] * 100 / 255);
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

    public static RazerParametricEqBand ReadParametricEqSetting(ReadOnlySpan<byte> response)
    {
        var data = Arguments(response, ParametricEqSettingSize);
        return new RazerParametricEqBand(
            data[0],
            data[1],
            data[2],
            data[3],
            (ushort)((data[4] << 8) | data[5]),
            unchecked((sbyte)data[6]),
            data[7],
            data[8]);
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

    public static RazerMicCompressor ReadMicCompressor(ReadOnlySpan<byte> response)
    {
        var data = Arguments(response, MicCompressorSize);
        return new RazerMicCompressor(
            data[0],
            data[1],
            unchecked((sbyte)data[2]),
            (ushort)((data[3] << 8) | data[4]),
            (ushort)((data[5] << 8) | data[6]),
            (ushort)((data[7] << 8) | data[8]));
    }

    public static byte ReadMicDeEsser(ReadOnlySpan<byte> response)
    {
        var data = Arguments(response);
        Require(data, 2);
        return data[1];
    }

    public static (RazerMicReverbRoomMode RoomMode, byte Intensity) ReadMicReverb(
        ReadOnlySpan<byte> response)
    {
        var data = Arguments(response, MicReverbSize);
        return ((RazerMicReverbRoomMode)data[1], data[2]);
    }

    public static sbyte ReadMicLimiter(ReadOnlySpan<byte> response)
    {
        var data = Arguments(response);
        Require(data, 2);
        return unchecked((sbyte)data[1]);
    }

    public static bool ReadAudioInputControlState(ReadOnlySpan<byte> response)
    {
        var data = Arguments(response);
        Require(data, 3);
        return data[2] != 0;
    }

    public static (RazerAncState State, byte Level) ReadAnc(ReadOnlySpan<byte> response)
    {
        var data = Arguments(response);
        Require(data, 3);
        return ((RazerAncState)data[1], data[2]);
    }

    public static bool ReadAudioDnd(ReadOnlySpan<byte> response) => ReadOnOff(response);

    public static (byte PromptIndex, bool Enabled) ReadAudioVoicePrompts(
        ReadOnlySpan<byte> response)
    {
        var data = Arguments(response);
        Require(data, 3);
        return (data[1], data[2] != 0);
    }

    public static bool ReadAudioAutoPause(ReadOnlySpan<byte> response) => ReadOnOff(response);

    public static bool ReadAudioGestureControl(ReadOnlySpan<byte> response) => ReadOnOff(response);

    public static bool ReadDolbyState(ReadOnlySpan<byte> response) => ReadOnOff(response);

    public static bool ReadEqNightMode(ReadOnlySpan<byte> response) => ReadOnOff(response);

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

    private static ReadOnlySpan<byte> Arguments(ReadOnlySpan<byte> response, int minimum)
    {
        var data = Arguments(response);
        Require(data, minimum);
        if (response[RazerFeatureReport.DataSizeOffset] < minimum)
        {
            throw new InvalidDataException(
                $"Razer response declares {response[RazerFeatureReport.DataSizeOffset]} data bytes; {minimum} required.");
        }

        return data;
    }

    private static bool ReadOnOff(ReadOnlySpan<byte> response)
    {
        var data = Arguments(response);
        Require(data, 2);
        return data[1] != 0;
    }

    private static void RequireOnOff(byte value, string paramName)
    {
        if (value > 1)
        {
            throw new ArgumentOutOfRangeException(paramName, "Razer audio switches accept 0 or 1.");
        }
    }

    private static void Require(ReadOnlySpan<byte> data, int length)
    {
        if (data.Length < length)
        {
            throw new InvalidDataException($"Razer response has {data.Length} data bytes; {length} required.");
        }
    }
}
