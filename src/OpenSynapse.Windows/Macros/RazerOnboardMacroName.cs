using System.Buffers.Binary;
using System.Text;

namespace OpenSynapse.Windows.Macros;

/// <summary>
/// The 250-byte name block that protocol 25 stores next to a macro. The macro data
/// stream is a separate allocation; this block carries the user identity used by the
/// middleware to match a local macro GUID to a device slot.
/// </summary>
public sealed record RazerOnboardMacroName(
    Guid Guid,
    int NumItems,
    string Name,
    byte[] ExtraInfo,
    string Timestamp,
    string Hash)
{
    public const int DeviceSize = 250;
    public const int NameLength = 100;
    public const int ExtraInfoLength = 16;
    public const int TimestampLength = 16;
    public const int HashLength = 32;
    private const int GuidOffset = 0;
    private const int NumItemsOffset = 16;
    private const int NameOffset = 20;
    private const int ExtraInfoOffset = 120;
    private const int TimestampOffset = 136;
    private const int HashOffset = 152;

    private static readonly UTF8Encoding Utf8 = new(false, false);

    public static RazerOnboardMacroName Empty { get; } = new(
        Guid.Empty, 0, string.Empty, [], string.Empty, string.Empty);

    public static RazerOnboardMacroName Decode(ReadOnlySpan<byte> data)
    {
        if (data.Length < DeviceSize)
        {
            throw new InvalidDataException(
                $"Onboard macro name block carries {data.Length} bytes, expected at least {DeviceSize}.");
        }

        var numItems = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(NumItemsOffset, 4));
        if (numItems < 0)
        {
            throw new InvalidDataException("Onboard macro name block declares a negative item count.");
        }

        return new(
            new Guid(data.Slice(GuidOffset, 16)),
            numItems,
            ReadText(data.Slice(NameOffset, NameLength)),
            data.Slice(ExtraInfoOffset, ExtraInfoLength).ToArray(),
            ReadText(data.Slice(TimestampOffset, TimestampLength)),
            ReadText(data.Slice(HashOffset, HashLength)));
    }

    public byte[] Encode()
    {
        var data = new byte[DeviceSize];
        Guid.TryWriteBytes(data.AsSpan(GuidOffset, 16));
        BinaryPrimitives.WriteInt32LittleEndian(
            data.AsSpan(NumItemsOffset, 4),
            Math.Clamp(NumItems, 0, 1024));
        WriteText(data.AsSpan(NameOffset, NameLength), Name);
        var extra = ExtraInfo ?? [];
        extra.AsSpan(0, Math.Min(extra.Length, ExtraInfoLength))
            .CopyTo(data.AsSpan(ExtraInfoOffset, ExtraInfoLength));
        WriteText(data.AsSpan(TimestampOffset, TimestampLength), Timestamp);
        WriteText(data.AsSpan(HashOffset, HashLength), Hash);
        return data;
    }

    private static string ReadText(ReadOnlySpan<byte> data)
    {
        var terminator = data.IndexOf((byte)0);
        return Utf8.GetString(terminator >= 0 ? data[..terminator] : data);
    }

    private static void WriteText(Span<byte> destination, string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        var encoded = Utf8.GetBytes(value);
        var length = Math.Min(encoded.Length, destination.Length);
        while (length > 0 && length < encoded.Length && (encoded[length] & 0xC0) == 0x80)
        {
            length--;
        }

        encoded.AsSpan(0, length).CopyTo(destination);
    }
}
