using System.Text;

namespace OpenSynapse.Windows.Protocols;

/// <summary>
/// AudioAvnera's 65-byte feature-report channel (report id 255). The JS bundle
/// writes a 64-byte frame with the FF 0A transport header and reads a 65-byte
/// feature response with the same header. This is not the protocol-25 91-byte
/// class/command envelope.
/// </summary>
internal static class RazerAvnera65Protocol
{
    internal const int WriteLength = 64;
    internal const int ReadLength = 65;
    internal const byte ReportId = 0xFF;
    internal const byte Sync = 0x0A;

    internal static readonly byte[] FirmwareHeader = [0xFE, 0x07, 0x04, 0x1F, 0x14];
    internal static readonly byte[] SerialHeader = [0xFE, 0x00, 0x04, 0x1F, 0x14, 0x81, 0xA0, 0xB0];
    internal static readonly byte[] EditionHeader = [0xFE, 0x00, 0x04, 0x1F, 0x05, 0x81, 0xA0, 0xB0];
    internal static readonly byte[] DockFirmwareHeader = [0xFE, 0x07, 0x04, 0x0F, 0x14];
    internal static readonly byte[] DockSerialHeader = [0xFE, 0x00, 0x04, 0x0F, 0x14, 0x81, 0x60, 0xC0];
    internal static readonly byte[] DockEditionHeader = [0xFE, 0x00, 0x04, 0x0F, 0x05, 0x81, 0x60, 0xC0];
    internal static readonly byte[] LinkStatusHeader = [0xFE, 0x00, 0x04, 0x0F, 0x05, 0x81, 0xAA, 0xC6];
    internal static readonly byte[] BatteryHeader = [0xFE, 0x12, 0x04, 0x1F, 0x08, 0x05];
    internal static readonly byte[] SidetoneHeader = [0xFE, 0x00, 0x04, 0x1F, 0x05, 0x81, 0x60, 0xB4];
    internal static readonly byte[] SilenceDetectionHeader = [0xFE, 0x12, 0x04, 0x1F, 0x05, 0x41];
    internal static readonly byte[] HapticsHeader = [0xFE, 0x02, 0x04, 0x1F, 0x06, 0x32];
    internal static readonly byte[] ChromaEffectHeader = [0xFE, 0x12, 0x04, 0x1F, 0x06, 0x70];
    internal static readonly byte[] ChromaBrightnessHeader = [0xFE, 0x12, 0x04, 0x1F, 0x03, 0x71];

    // The get tuples are copied from module 77337 in the 0528 bundle. The first
    // byte is the JS direction marker (0xFD read, 0xFF write); 0x04 is the
    // command class and the next byte is the command id.
    internal static byte[] CreateFirmwareRequest() =>
        Frame([0xFD, 0x04, 0x07, 0xF1, 0x05, 0x02, 0x05, 0x00, 0x00]);

    internal static byte[][] CreateSerialRequests() =>
    [
        Frame([0xFF, 0x04, 0x00, 0xF1, 0x0C, 0x00, 0x3B, 0xB5, 0x00, 0x7F, 0x00, 0x00, 0xA0, 0xB0, 0x10, 0x00]),
        Frame([0xFF, 0x04, 0x00, 0xF1, 0x02, 0x84]),
        Frame([0xFD, 0x04, 0x00, 0xF1, 0x05, 0x81, 0xA0, 0xB0, 0x10]),
    ];

    internal static byte[][] CreateEditionRequests() =>
    [
        Frame([0xFF, 0x04, 0x00, 0xF1, 0x0C, 0x00, 0x3B, 0xB5, 0x10, 0x7F, 0x00, 0x00, 0xA0, 0xB0, 0x01, 0x00]),
        Frame([0xFF, 0x04, 0x00, 0xF1, 0x02, 0x84]),
        Frame([0xFD, 0x04, 0x00, 0xF1, 0x05, 0x81, 0xA0, 0xB0, 0x01]),
    ];

    internal static byte[] CreateDockFirmwareRequest() =>
        Frame([0xFD, 0x04, 0x07, 0x00, 0x05, 0x02, 0x05, 0x00, 0x00]);

    internal static byte[][] CreateDockSerialRequests() =>
    [
        Frame([0xFF, 0x04, 0x00, 0x00, 0x0C, 0x00, 0x2E, 0xC4, 0x00, 0x7F, 0x00, 0x00, 0x60, 0xC0, 0x10, 0x00]),
        Frame([0xFF, 0x04, 0x00, 0x00, 0x02, 0x84]),
        Frame([0xFD, 0x04, 0x00, 0x00, 0x05, 0x81, 0x60, 0xC0, 0x10]),
    ];

    internal static byte[][] CreateDockEditionRequests() =>
    [
        Frame([0xFF, 0x04, 0x00, 0x00, 0x0C, 0x00, 0x2E, 0xC4, 0x10, 0x7F, 0x00, 0x00, 0x60, 0xC0, 0x01, 0x00]),
        Frame([0xFF, 0x04, 0x00, 0x00, 0x02, 0x84]),
        Frame([0xFD, 0x04, 0x00, 0x00, 0x05, 0x81, 0x60, 0xC0, 0x01]),
    ];

    internal static byte[] CreateLinkStatusRequest() =>
        Frame([0xFD, 0x04, 0x00, 0x00, 0x05, 0x81, 0xAA, 0xC6, 0x01]);

    internal static byte[] CreateBatteryRequest() =>
        Frame([0xFD, 0x04, 0x12, 0xF1, 0x02, 0x05]);

    internal static byte[] CreateSidetoneRequest() =>
        Frame([0xFD, 0x04, 0x00, 0xF1, 0x05, 0x81, 0x60, 0xB4, 0x01]);

    internal static byte[] CreateSetSidetoneRequest(byte percent)
    {
        var encoded = unchecked((byte)((percent * 44 / 100) - 64));
        return Frame([0xFF, 0x04, 0x0E, 0xF1, 0x05, 0x01, 0x04, 0x00], [encoded]);
    }

    internal static byte[] CreateSilenceDetectionRequest() =>
        Frame([0xFD, 0x04, 0x12, 0xF1, 0x02, 0x41]);

    internal static byte[] CreateSetSilenceDetectionRequest(bool enabled, ushort period)
    {
        Span<byte> payload = stackalloc byte[3];
        payload[0] = enabled ? (byte)1 : (byte)0;
        payload[1] = (byte)period;
        payload[2] = (byte)(period >> 8);
        return Frame([0xFF, 0x04, 0x12, 0xF1, 0x05, 0x41], payload);
    }

    internal static byte[] CreateHapticsRequest() =>
        Frame([0xFD, 0x04, 0x02, 0xF1, 0x03, 0x20, 0x00]);

    internal static byte[] CreateSetHapticsRequest(bool enabled, ushort strength)
    {
        Span<byte> payload = stackalloc byte[3];
        payload[0] = enabled ? (byte)1 : (byte)0;
        payload[1] = (byte)strength;
        payload[2] = (byte)(strength >> 8);
        return Frame([0xFF, 0x04, 0x02, 0xF1, 0x06, 0x20], payload);
    }

    internal static byte[] CreateChromaEffectRequest() =>
        Frame([0xFD, 0x04, 0x12, 0xF1, 0x02, 0x70]);

    internal static byte[] CreateSetChromaEffectRequest(RazerChromaEffect effect, byte red, byte green, byte blue)
    {
        Span<byte> payload = stackalloc byte[4];
        payload[0] = (byte)effect;
        if (effect == RazerChromaEffect.Static)
        {
            payload[1] = red;
            payload[2] = green;
            payload[3] = blue;
        }

        return Frame([0xFF, 0x04, 0x12, 0xF1, 0x06, 0x70], payload);
    }

    internal static byte[] CreateChromaBrightnessRequest() =>
        Frame([0xFD, 0x04, 0x12, 0xF1, 0x02, 0x71]);

    internal static byte[] CreateSetChromaBrightnessRequest(byte percent)
    {
        var raw = (byte)Math.Clamp(255 * percent / 100, 0, 255);
        return Frame([0xFF, 0x04, 0x12, 0xF1, 0x03, 0x71], [raw]);
    }

    internal static byte[] CreateSetChromaFrameRequest(byte red, byte green, byte blue) =>
        Frame([0xFF, 0x04, 0x12, 0xF1, 0x05, 0x72], [red, green, blue]);

    internal static bool Matches(ReadOnlySpan<byte> response, ReadOnlySpan<byte> header) =>
        response.Length == ReadLength &&
        response[0] == ReportId &&
        response[1] == Sync &&
        response.Slice(3, header.Length).SequenceEqual(header);

    internal static Version ReadFirmware(ReadOnlySpan<byte> response) =>
        ReadFirmware(response, FirmwareHeader);

    internal static Version ReadDockFirmware(ReadOnlySpan<byte> response) =>
        ReadFirmware(response, DockFirmwareHeader);

    private static Version ReadFirmware(ReadOnlySpan<byte> response, ReadOnlySpan<byte> header)
    {
        var data = Payload(response, header);
        if (data.Length < 8 || data[0] != 2)
        {
            throw new InvalidDataException("AudioAvnera returned an invalid extended firmware response.");
        }

        var text = Encoding.ASCII.GetString(data.Slice(1, 7));
        if (!Version.TryParse(text, out var version))
        {
            throw new InvalidDataException($"AudioAvnera returned an invalid firmware version '{text}'.");
        }

        return version;
    }

    internal static string ReadSerialNumber(ReadOnlySpan<byte> response) =>
        ReadSerialNumber(response, SerialHeader);

    internal static string ReadDockSerialNumber(ReadOnlySpan<byte> response) =>
        ReadSerialNumber(response, DockSerialHeader);

    private static string ReadSerialNumber(ReadOnlySpan<byte> response, ReadOnlySpan<byte> header)
    {
        var data = Payload(response, header);
        var text = new StringBuilder();
        for (var index = 1; index < data.Length && index < 23; index++)
        {
            if (data[index] is >= 0x20 and < 0x7F)
            {
                text.Append((char)data[index]);
            }
        }

        return text.ToString();
    }

    internal static byte ReadEdition(ReadOnlySpan<byte> response) =>
        Payload(response, EditionHeader)[0];

    internal static byte ReadDockEdition(ReadOnlySpan<byte> response) =>
        Payload(response, DockEditionHeader)[0];

    internal static byte ReadLinkStatus(ReadOnlySpan<byte> response) =>
        Payload(response, LinkStatusHeader)[0];

    internal static (RazerChromaEffect Effect, byte Red, byte Green, byte Blue) ReadChromaEffect(
        ReadOnlySpan<byte> response)
    {
        var data = Payload(response, ChromaEffectHeader);
        if (data.Length < 4)
        {
            throw new InvalidDataException("AudioAvnera returned a short Chroma effect response.");
        }

        return ((RazerChromaEffect)data[0], data[1], data[2], data[3]);
    }

    internal static byte ReadChromaBrightness(ReadOnlySpan<byte> response)
    {
        var data = Payload(response, ChromaBrightnessHeader);
        if (data.Length < 1)
        {
            throw new InvalidDataException("AudioAvnera returned a short Chroma brightness response.");
        }

        return (byte)Math.Clamp((data[0] * 100 + 127) / 255, 0, 100);
    }

    internal static (byte Level, bool Charging) ReadBattery(ReadOnlySpan<byte> response)
    {
        var data = Payload(response, BatteryHeader);
        if (data.Length < 6)
        {
            throw new InvalidDataException("AudioAvnera returned a short battery response.");
        }

        // Module 77337: data[0] == 5 means charging; data[5] is the level.
        return (data[5], data[0] == 5);
    }

    internal static byte ReadSidetone(ReadOnlySpan<byte> response)
    {
        var data = Payload(response, SidetoneHeader);
        if (data.Length < 1)
        {
            throw new InvalidDataException("AudioAvnera returned a short sidetone response.");
        }

        // Module 33688 maps the raw value -64..-20 to 0..100 percent.
        return (byte)Math.Clamp(((data[0] - 192) * 100) / 44, 0, 100);
    }

    internal static (bool Enabled, ushort Period) ReadSilenceDetection(ReadOnlySpan<byte> response)
    {
        var data = Payload(response, SilenceDetectionHeader);
        if (data.Length < 3)
        {
            throw new InvalidDataException("AudioAvnera returned a short silence-detection response.");
        }

        return (data[0] != 0, (ushort)(data[1] | (data[2] << 8)));
    }

    internal static RazerHapticsControlState ReadHaptics(ReadOnlySpan<byte> response)
    {
        var data = Payload(response, HapticsHeader);
        if (data.Length < 3)
        {
            throw new InvalidDataException("AudioAvnera returned a short haptics response.");
        }

        return new RazerHapticsControlState(
            data[0] != 0,
            (ushort)(data[1] | (data[2] << 8)));
    }

    private static ReadOnlySpan<byte> Payload(ReadOnlySpan<byte> response, ReadOnlySpan<byte> header)
    {
        if (!Matches(response, header))
        {
            throw new InvalidDataException("AudioAvnera response header did not match the request.");
        }

        var payloadStart = 3 + header.Length;
        var payloadLength = header[4] - (header.Length - 4);
        if (payloadLength < 0 || payloadStart + payloadLength > response.Length)
        {
            throw new InvalidDataException("AudioAvnera response length is invalid.");
        }

        return response.Slice(payloadStart, payloadLength);
    }

    private static byte[] Frame(ReadOnlySpan<byte> command, ReadOnlySpan<byte> payload = default)
    {
        var report = new byte[WriteLength];
        report[0] = ReportId;
        report[1] = Sync;
        command.CopyTo(report.AsSpan(3));
        payload.CopyTo(report.AsSpan(3 + command.Length));
        return report;
    }
}
