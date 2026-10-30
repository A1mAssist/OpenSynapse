using System;

namespace OpenSynapse.Windows.Protocols;

/// <summary>
/// The safe 053E HID subset decoded from the AudioMixer bundle. The byte layouts are
/// bundle-aligned; no live 053E hardware was available to verify them.
/// </summary>
public static class RazerMixer053EProtocol
{
    public const ushort ProductId = 0x053E;
    public const byte FeatureReportId = 0x07;
    public const byte StatusReportId = 0x09;
    public const int FeatureReportLength = 64;
    public const int StatusReportLength = 16;
    public const int OutputReportLength = 66;
    public const byte CompanionReportId = 0x03;

    public static readonly byte[] AllLightingRegions = [4, 5, 6, 7, 8, 9, 16, 10, 33, 32];

    private static readonly byte[] FirmwareRequest =
        [7, 0, 0, 0, 0, 0, 2, 0, 129, 0, 0, 0, 0, 0];

    private static readonly byte[] SerialRequest =
        [7, 0, 31, 0, 0, 0, 22, 0, 130, 0, 0, 0, 0, 0];

    private static readonly byte[] DeviceModeRequest =
        [7, 0, 0, 0, 0, 0, 2, 0, 132, 0, 0, 0];

    private static readonly byte[] BrightnessRequest =
        [7, 0, 31, 0, 0, 0, 3, 15, 132, 0, 0, 0];

    private static readonly byte[] ChannelMuteRequest =
        [7, 0, 31, 0, 0, 0, 3, 8, 16, 0, 0, 0];

    private static readonly byte[] BrightnessSetRequest =
        [7, 0, 31, 0, 0, 0, 3, 15, 4, 0, 0, 0];

    private static readonly byte[] ConnectRequest = [4, 95, 252, 0, 28];
    private static readonly byte[] MicMuteOnCompanion = [3, 24, 0, 192, 148, 196, 192];
    private static readonly byte[] MicMuteOffCompanion = [3, 24, 0, 192, 148, 68, 64];

    /// <summary>Bundle-aligned connect handshake: 66-byte output report, report id 4.</summary>
    public static byte[] CreateConnectHandshake() => CreateOutput(ConnectRequest);

    /// <summary>Bundle-aligned Get Firmware request.</summary>
    public static byte[] CreateGetFirmware() => CreateFeature(FirmwareRequest);

    /// <summary>Bundle-aligned Get Serial Number request.</summary>
    public static byte[] CreateGetSerialNumber() => CreateFeature(SerialRequest);

    /// <summary>Bundle-aligned Get Device Mode request.</summary>
    public static byte[] CreateGetDeviceMode() => CreateFeature(DeviceModeRequest);

    /// <summary>Bundle-aligned Get Brightness request.</summary>
    public static byte[] CreateGetBrightness() => CreateFeature(BrightnessRequest);

    /// <summary>Bundle-aligned channel mute control. Channels 1..4 are faders; 5 is the microphone.</summary>
    public static byte[] CreateSetChannelMute(int channel, bool muted)
    {
        if (channel is < 1 or > 5)
        {
            throw new ArgumentOutOfRangeException(nameof(channel), "053E channel must be 1..5.");
        }

        var report = CreateFeature(ChannelMuteRequest);
        report[10] = (byte)channel;
        report[11] = muted ? (byte)1 : (byte)0;
        return report;
    }

    /// <summary>
    /// Bundle-aligned brightness set. Region 0 fans out to the ten lighting regions used by
    /// the bundle; any other region produces one frame.
    /// </summary>
    public static IReadOnlyList<byte[]> CreateSetBrightnessFrames(
        byte profileId,
        byte regionId,
        byte brightness)
    {
        if (regionId != 0)
        {
            return [CreateSetBrightnessFrame(profileId, regionId, brightness)];
        }

        var frames = new byte[AllLightingRegions.Length][];
        for (var index = 0; index < AllLightingRegions.Length; index++)
        {
            frames[index] = CreateSetBrightnessFrame(profileId, AllLightingRegions[index], brightness);
        }

        return frames;
    }

    public static byte[] CreateSetBrightnessFrame(byte profileId, byte regionId, byte brightness)
    {
        var report = CreateFeature(BrightnessSetRequest);
        report[9] = profileId;
        report[10] = regionId;
        report[11] = brightness;
        return report;
    }

    /// <summary>
    /// Bundle-aligned seven-byte microphone-mute companion output. The logical frame is
    /// seven bytes; the bundle sends it as a 66-byte output report, so the rest is
    /// zero-filled.
    /// </summary>
    public static byte[] CreateMicMuteCompanion(bool muted) =>
        CreateOutput(muted ? MicMuteOnCompanion : MicMuteOffCompanion);

    public static bool MatchesFirmwareResponse(ReadOnlySpan<byte> response) =>
        MatchesResponse(response, 0x00, 0x81);

    public static bool MatchesSerialNumberResponse(ReadOnlySpan<byte> response) =>
        MatchesResponse(response, 0x00, 0x82) &&
        response.Length >= 9 &&
        response[2] == 0x1F;

    public static bool MatchesDeviceModeResponse(ReadOnlySpan<byte> response) =>
        MatchesResponse(response, 0x00, 0x84);

    public static bool MatchesBrightnessResponse(ReadOnlySpan<byte> response) =>
        MatchesResponse(response, 0x0F, 0x84);

    public static Version ReadFirmware(ReadOnlySpan<byte> response)
    {
        if (!MatchesFirmwareResponse(response) || response.Length < 11)
        {
            throw new InvalidDataException("053E firmware response did not match the bundle header.");
        }

        return new Version(response[9], response[10]);
    }

    public static string ReadSerialNumber(ReadOnlySpan<byte> response)
    {
        if (!MatchesSerialNumberResponse(response) || response.Length < 9)
        {
            throw new InvalidDataException("053E serial response did not match the bundle header.");
        }

        var length = response[6] | (response[7] << 8);
        if (length < 0 || 9 + length > response.Length)
        {
            throw new InvalidDataException("053E serial response length is out of range.");
        }

        var characters = new char[length];
        var count = 0;
        for (var index = 0; index < length; index++)
        {
            var value = response[9 + index];
            if (value is >= 32 and < 127)
            {
                characters[count++] = (char)value;
            }
        }

        return new string(characters, 0, count);
    }

    public static byte ReadDeviceMode(ReadOnlySpan<byte> response)
    {
        if (!MatchesDeviceModeResponse(response) || response.Length < 10)
        {
            throw new InvalidDataException("053E device-mode response did not match the bundle header.");
        }

        return response[9];
    }

    public static byte ReadBrightness(ReadOnlySpan<byte> response)
    {
        if (!MatchesBrightnessResponse(response) || response.Length < 12)
        {
            throw new InvalidDataException("053E brightness response did not match the bundle header.");
        }

        return response[11];
    }

    public static bool TryReadHardwareStatus(
        ReadOnlySpan<byte> report,
        out RazerMixer053EHardwareStatus? status)
    {
        if (report.Length < StatusReportLength || report[0] != StatusReportId)
        {
            status = null;
            return false;
        }

        var buttons = report[2];
        var flags = report[7];
        status = new RazerMixer053EHardwareStatus(
            Mute1: (buttons & 0x01) != 0,
            Mute2: (buttons & 0x02) != 0,
            Mute3: (buttons & 0x04) != 0,
            Mute4: (buttons & 0x08) != 0,
            Beep: (buttons & 0x80) != 0,
            MicMute: (buttons & 0x20) != 0,
            PhantomPower48V: (flags & 0x01) != 0,
            MicJack: (flags & 0x02) != 0,
            LineIn: (flags & 0x08) != 0,
            Slider1: report[3],
            Slider2: report[4],
            Slider3: report[5],
            Slider4: report[6]);
        return true;
    }

    private static bool MatchesResponse(ReadOnlySpan<byte> response, byte commandClass, byte commandId) =>
        response.Length >= 9 &&
        response[0] == FeatureReportId &&
        response[1] == 0x02 &&
        response[7] == commandClass &&
        response[8] == commandId;

    private static byte[] CreateFeature(ReadOnlySpan<byte> prefix)
    {
        var report = new byte[FeatureReportLength];
        prefix.CopyTo(report);
        report[0] = FeatureReportId;
        return report;
    }

    private static byte[] CreateOutput(ReadOnlySpan<byte> prefix)
    {
        var report = new byte[OutputReportLength];
        prefix.CopyTo(report);
        return report;
    }
}

public sealed record RazerMixer053EHardwareStatus(
    bool Mute1,
    bool Mute2,
    bool Mute3,
    bool Mute4,
    bool Beep,
    bool MicMute,
    bool PhantomPower48V,
    bool MicJack,
    bool LineIn,
    byte Slider1,
    byte Slider2,
    byte Slider3,
    byte Slider4)
{
    public bool IsMuted(int channel) => channel switch
    {
        1 => Mute1,
        2 => Mute2,
        3 => Mute3,
        4 => Mute4,
        5 => MicMute,
        _ => throw new ArgumentOutOfRangeException(nameof(channel), "053E channel must be 1..5."),
    };
}
