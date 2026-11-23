using System.Buffers.Binary;
using System.Text;

namespace OpenSynapse.Windows.Protocols;

/// <summary>052C/052D AudioNXPDevice: H4 serial commands, not HID feature reports.</summary>
public static class RazerNxpHeadsetProtocol
{
    public static bool IsSupported(ushort productId) => productId is 0x052C or 0x052D;

    public static byte[] GetLinkStatus() => [0x01, 0x9A, 0xFC, 0x00];
    public static byte[] GetFirmware(bool dock = false) => Create(0x20, target: dock ? (byte)4 : (byte)2);
    public static byte[] GetSerial(bool dock = false) => Create(0x21, target: dock ? (byte)4 : (byte)2);
    public static byte[] GetEdition(bool dock = false) => Create(0x22, target: dock ? (byte)4 : (byte)2);
    public static byte[] GetBattery() => Create(0x24);
    public static byte[] GetSidetone() => Create(0x25);
    public static byte[] GetSilenceDetection() => Create(0x26);
    public static byte[] GetHaptics() => Create(0x40, payload: [0x8B]);

    public static byte QuantizeSidetone(int percent)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(percent);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(percent, 100);
        return (byte)(Math.Max(1, (percent + 19) / 20) * 20);
    }

    public static byte[] SetSidetone(bool enabled, int percent) =>
        Create(0x25, true, payload: [(byte)(enabled ? 1 : 0), (byte)(QuantizeSidetone(percent) / 20 - 1)]);

    public static byte[] SetSilenceDetection(bool enabled, ushort seconds) =>
        Create(0x26, true, payload: [(byte)(enabled ? 1 : 0), (byte)seconds, (byte)(seconds >> 8)]);

    public static byte[] SetHaptics(bool enabled, byte intensity) =>
        Create(0x40, true, payload: [0x8A, 0, (byte)(enabled ? 1 : 0), intensity]);

    private static byte[] Create(byte command, bool write = false, byte target = 2, byte[]? payload = null)
    {
        // AudioNXPDevice H()/q(): checksum covers bytes 7..27, excluding the header.
        var frame = new byte[28];
        frame[0] = 1;
        frame[1] = 0xF3;
        frame[2] = 0xFF;
        frame[3] = 24;
        frame[4] = target;
        frame[5] = write ? (byte)0x21 : (byte)0x20;
        frame[6] = command;
        if (payload is not null) payload.CopyTo(frame, 7);
        var sum = 0;
        for (var index = 7; index < 27; index++) sum += frame[index];
        frame[27] = unchecked((byte)-sum);
        return frame;
    }

    internal static bool MatchesResponse(ReadOnlySpan<byte> request, ReadOnlySpan<byte> response)
    {
        if (request.Length < 4 || response.Length < 8 || response[0] != 4) return false;
        // H4 Command Status is not a reply payload. Command Complete carries the echoed opcode.
        if (response[1] == 0x0F || response[1] == 0x0E &&
            (response[4] != request[1] || response[5] != request[2])) return false;
        if (request[1] == 0x9A) return response[4] == 0x9A;
        // _parseNotify routes by target / read-or-write / command / MCU subcommand.
        return request.Length == 28 && response.Length >= 10 && response[4] != 0x9A &&
            response[7] == request[4] && response[8] == request[5] && response[9] == request[6] &&
            (request[6] != 0x40 || response.Length >= 12 && response[10] == request[7]);
    }

    internal static void ValidateResponse(ReadOnlySpan<byte> request, ReadOnlySpan<byte> response)
    {
        if (!MatchesResponse(request, response) || response.Length != response[2] + 3)
            throw new InvalidDataException("NXP returned an incomplete or unrelated serial response.");
        if (response[1] == 0x0E && response[6] != 0)
            throw new InvalidDataException($"NXP H4 command failed with status 0x{response[6]:X2}.");
        if (request.Length == 28 && request[6] == 0x40 && response[11] == 0)
            throw new InvalidDataException("NXP external MCU rejected the command.");
    }

    public static byte ReadLinkStatus(ReadOnlySpan<byte> response)
    {
        Require(GetLinkStatus(), response, 8);
        if (response[7] > 2) throw new InvalidDataException("Unknown NXP link state.");
        return response[7];
    }

    public static Version ReadFirmware(ReadOnlySpan<byte> response, bool dock = false)
    {
        Require(GetFirmware(dock), response, 13);
        return new Version(response[10], response[11], response[12]);
    }

    public static string ReadSerial(ReadOnlySpan<byte> response, bool dock = false)
    {
        Require(GetSerial(dock), response, 11);
        var text = response[10..];
        var end = text.IndexOf((byte)0);
        if (end >= 0) text = text[..end];
        if (text.IsEmpty) throw new InvalidDataException("NXP returned an empty serial number.");
        return Encoding.ASCII.GetString(text);
    }

    public static byte ReadEdition(ReadOnlySpan<byte> response, bool dock = false)
    {
        Require(GetEdition(dock), response, 11);
        return response[10];
    }

    public static (byte Level, bool Charging) ReadBattery(ReadOnlySpan<byte> response)
    {
        Require(GetBattery(), response, 14);
        if (response[10] is 0 or 7 or > 7 || response[13] > 100)
            throw new InvalidDataException("NXP returned an invalid battery state or percentage.");
        return (response[13], response[10] == 5);
    }

    public static (bool Enabled, byte Percent) ReadSidetone(ReadOnlySpan<byte> response)
    {
        Require(GetSidetone(), response, 12);
        if (response[10] > 1 || response[11] > 4)
            throw new InvalidDataException("NXP returned an invalid sidetone setting.");
        return (response[10] == 1, (byte)(20 * response[11] + 20));
    }

    public static (bool Enabled, ushort Seconds) ReadSilenceDetection(ReadOnlySpan<byte> response)
    {
        Require(GetSilenceDetection(), response, 13);
        if (response[10] > 1) throw new InvalidDataException("NXP returned an invalid silence detection setting.");
        return (response[10] == 1, BinaryPrimitives.ReadUInt16LittleEndian(response[11..13]));
    }

    public static RazerHapticsControlState ReadHaptics(ReadOnlySpan<byte> response)
    {
        Require(GetHaptics(), response, 15);
        if (response[13] > 1) throw new InvalidDataException("NXP returned an invalid haptics setting.");
        return new RazerHapticsControlState(response[13] == 1, response[14]);
    }

    private static void Require(byte[] request, ReadOnlySpan<byte> response, int minimumLength)
    {
        ValidateResponse(request, response);
        if (response.Length < minimumLength) throw new InvalidDataException("NXP response has no complete value.");
    }
}
