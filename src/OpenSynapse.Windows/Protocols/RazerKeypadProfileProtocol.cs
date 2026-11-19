using System.Buffers.Binary;
using OpenSynapse.Windows.Devices;

namespace OpenSynapse.Windows.Protocols;

/// <summary>
/// One 64-byte Synapse 2 P2 profile record used by the legacy one-handed keypads.
/// The field offsets come from rzdevicedll FUN_100eac60/FUN_100ecc30. The three
/// words at +0x14..+0x1F are not named in the available evidence, so they are
/// preserved verbatim rather than interpreted.
/// </summary>
internal readonly record struct RazerKeypadProfileRecord
{
    internal const int Size = 64;
    internal const int PayloadOffset = 0x20;
    internal const int PayloadLength = 0x20;

    public RazerKeypadProfileRecord(
        uint category,
        bool isValid,
        uint action,
        uint inputId,
        uint flags,
        ReadOnlySpan<byte> payload,
        uint reserved0 = 0,
        uint reserved1 = 0,
        uint reserved2 = 0)
    {
        if (payload.Length > PayloadLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(payload),
                "A keypad profile record carries at most 32 payload bytes.");
        }

        Category = category;
        IsValid = isValid;
        Action = action;
        InputId = inputId;
        Flags = flags;
        Payload = new byte[PayloadLength];
        payload.CopyTo(Payload);
        Reserved0 = reserved0;
        Reserved1 = reserved1;
        Reserved2 = reserved2;
    }

    public uint Category { get; }
    public bool IsValid { get; }

    /// <summary>0 empty, 1/4/5 mouse, 2 keyboard, 3 wheel, 6 media, 7 macro,
    /// 8 application, 9 DPI, 0xA lighting, 0xB disabled, 0xF eight-way table.</summary>
    public uint Action { get; }

    /// <summary>Legacy keypad main-key id, 1..20.</summary>
    public uint InputId { get; }

    /// <summary>0x02000000 marks an eight-way table; bits 30 and 31 pass through.</summary>
    public uint Flags { get; }

    public byte[] Payload { get; }
    public uint Reserved0 { get; }
    public uint Reserved1 { get; }
    public uint Reserved2 { get; }

    internal static RazerKeypadProfileRecord Decode(ReadOnlySpan<byte> source)
    {
        if (source.Length < Size)
        {
            throw new ArgumentException("A keypad profile record is 64 bytes.", nameof(source));
        }

        return new RazerKeypadProfileRecord(
            BinaryPrimitives.ReadUInt32LittleEndian(source),
            BinaryPrimitives.ReadUInt32LittleEndian(source[4..]) != 0,
            BinaryPrimitives.ReadUInt32LittleEndian(source[8..]),
            BinaryPrimitives.ReadUInt32LittleEndian(source[12..]),
            BinaryPrimitives.ReadUInt32LittleEndian(source[16..]),
            source.Slice(PayloadOffset, PayloadLength),
            BinaryPrimitives.ReadUInt32LittleEndian(source[0x14..]),
            BinaryPrimitives.ReadUInt32LittleEndian(source[0x18..]),
            BinaryPrimitives.ReadUInt32LittleEndian(source[0x1C..]));
    }

    internal void Encode(Span<byte> destination)
    {
        if (destination.Length < Size)
        {
            throw new ArgumentException("A keypad profile record is 64 bytes.", nameof(destination));
        }

        destination[..Size].Clear();
        BinaryPrimitives.WriteUInt32LittleEndian(destination, Category);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[4..], IsValid ? 1u : 0u);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[8..], Action);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[12..], InputId);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[16..], Flags);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[0x14..], Reserved0);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[0x18..], Reserved1);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[0x1C..], Reserved2);
        Payload.CopyTo(destination[PayloadOffset..]);
    }
}

/// <summary>
/// Synapse 2 P2 profile transfer for the legacy one-handed keypads.
/// The write IOCTL always receives a 0x8018-byte buffer (24-byte header plus up
/// to 512 records) and returns 16 bytes. The read request has a 24-byte header;
/// its response starts with a 32-bit record count and then the records.
/// </summary>
internal static class RazerKeypadProfileProtocol
{
    internal const int HeaderLength = 24;
    internal const int RecordLength = RazerKeypadProfileRecord.Size;
    internal const int MaxRecordsPerChunk = 512;
    internal const int WriteInputLength = HeaderLength + MaxRecordsPerChunk * RecordLength;
    internal const int WriteOutputLength = 16;

    internal static byte[] CreateReadPacket(uint count)
    {
        var packet = new byte[HeaderLength];
        WriteHeader(packet, 3, 0x22, count, 0, 0x18, 0);
        return packet;
    }

    internal static IReadOnlyList<RazerKeypadProfileRecord> ParseReadResponse(
        ReadOnlySpan<byte> output)
    {
        if (output.Length < sizeof(uint))
        {
            throw new InvalidDataException("The keypad profile response has no record count.");
        }

        var count = BinaryPrimitives.ReadUInt32LittleEndian(output);
        var available = (output.Length - sizeof(uint)) / RecordLength;
        if (count > MaxRecordsPerChunk || count > available)
        {
            throw new InvalidDataException(
                $"The keypad profile response declares {count} records but only {available} fit.");
        }

        var records = new RazerKeypadProfileRecord[(int)count];
        for (var index = 0; index < records.Length; index++)
        {
            records[index] = RazerKeypadProfileRecord.Decode(
                output.Slice(sizeof(uint) + index * RecordLength, RecordLength));
        }

        return records;
    }

    internal static IReadOnlyList<(byte[] Input, int OutputLength)> CreateWriteChunks(
        IReadOnlyList<RazerKeypadProfileRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        if (records.Count == 0)
        {
            return [];
        }

        var chunks = new List<(byte[] Input, int OutputLength)>(
            (records.Count + MaxRecordsPerChunk - 1) / MaxRecordsPerChunk);
        for (var offset = 0; offset < records.Count; offset += MaxRecordsPerChunk)
        {
            var count = Math.Min(MaxRecordsPerChunk, records.Count - offset);
            var input = new byte[WriteInputLength];
            WriteHeader(input, 3, 1, (uint)count, 0, 0, 0);
            for (var index = 0; index < count; index++)
            {
                records[offset + index].Encode(
                    input.AsSpan(HeaderLength + index * RecordLength, RecordLength));
            }

            chunks.Add((input, WriteOutputLength));
        }

        return chunks;
    }

    private static void WriteHeader(
        Span<byte> destination,
        uint first,
        uint second,
        uint third,
        uint fourth,
        uint fifth,
        uint sixth)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(destination, first);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[4..], second);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[8..], third);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[12..], fourth);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[16..], fifth);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[20..], sixth);
    }
}

/// <summary>
/// Converts the editor's function/data pair to the legacy P2 record and back.
/// Only the actions the legacy mapping page can produce are accepted: Off,
/// mouse button and keyboard key. Everything else stays untouched in the
/// device's profile and is reported as unsupported instead of being guessed.
/// </summary>
internal static class RazerKeypadProfileCodec
{
    internal static RazerKeypadProfileRecord FromAssignment(
        byte inputId,
        RazerHidButtonAssignment assignment)
    {
        switch (assignment.FunctionId)
        {
            case RazerHidMappingFunctions.Off:
                return new RazerKeypadProfileRecord(0, true, 0, inputId, 0, []);
            case RazerHidMappingFunctions.ButtonCode when assignment.Data.Count >= 1:
                return new RazerKeypadProfileRecord(
                    0, true, 1, inputId, 0, [assignment.Data[0]]);
            case RazerHidMappingFunctions.KeyCode when assignment.Data.Count >= 2:
            {
                var payload = new byte[RazerKeypadProfileRecord.PayloadLength];
                var code = RazerKeypadKeyCodes.ToInternal(assignment.Data[1]);
                if (code == 0)
                {
                    throw new NotSupportedException(
                        $"HID usage 0x{assignment.Data[1]:X2} has no legacy keypad key code.");
                }

                payload[0] = (byte)code;
                payload[1] = (byte)(code >> 8);
                payload[4] = assignment.Data[0];
                return new RazerKeypadProfileRecord(0, true, 2, inputId, 0, payload);
            }
            default:
                throw new NotSupportedException(
                    $"Legacy keypad input {inputId} does not support function " +
                    $"0x{assignment.FunctionId:X2} in the P2 profile.");
        }
    }

    internal static RazerHidButtonAssignment ToAssignment(
        byte inputId,
        byte mode,
        RazerKeypadProfileRecord record) =>
        record.Action switch
        {
            0 or 0xB => new RazerHidButtonAssignment(
                inputId, mode, RazerHidMappingFunctions.Off, []),
            1 => new RazerHidButtonAssignment(
                inputId, mode, RazerHidMappingFunctions.ButtonCode,
                [record.Payload[0]]),
            2 => new RazerHidButtonAssignment(
                inputId, mode, RazerHidMappingFunctions.KeyCode,
                [
                    record.Payload[4],
                    RazerKeypadKeyCodes.ToHid(
                        (ushort)(record.Payload[0] | record.Payload[1] << 8)),
                ]),
            _ => new RazerHidButtonAssignment(
                inputId, mode, RazerHidMappingFunctions.Off, []),
        };
}
