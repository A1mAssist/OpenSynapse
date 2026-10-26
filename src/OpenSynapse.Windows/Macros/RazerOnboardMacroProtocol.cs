using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Macros;

/// <summary>
/// One onboard-memory command. Class, id and payload size are the vectors declared by
/// Synapse's protocol 25 keyboard class (rzDevice25): 06:03 delete, 06:08 allocate,
/// 06:09 write, 06:80 count, 06:88 data size, 06:89 memory read, 06:8E storage info.
/// </summary>
public sealed record RazerOnboardMacroCommand(byte CommandClass, byte CommandId, byte DataSize)
{
    public byte[] CreateRequest(byte transactionId, ReadOnlySpan<byte> arguments)
    {
        if (arguments.Length > DataSize)
        {
            throw new ArgumentException(
                $"Command 0x{CommandClass:X2}:0x{CommandId:X2} accepts {DataSize} argument bytes, not {arguments.Length}.",
                nameof(arguments));
        }

        return RazerFeatureReport.CreateRequest(transactionId, DataSize, CommandClass, CommandId, arguments);
    }
}

/// <summary>
/// Four 16/32-bit counters from Get Macro Storage Info. MaxId counts macro slots;
/// the three memory fields are bytes.
/// </summary>
public sealed record RazerOnboardMacroStorageInfo(
    int MaxId,
    int MaxMemory,
    int AvailableMemory,
    int RecyclableMemory);

public sealed record RazerOnboardMacroMemoryChunk(int Offset, byte[] Data);

/// <summary>
/// Request and response shapes for protocol 25 keyboards, transcribed from the
/// rzDevice25 getMacroStorageInfo/getMacroMemoryData/setMacroMemoryData builders.
/// A macro id is a big-endian 16-bit value; offsets and sizes are big-endian 32-bit
/// values split into two 16-bit halves.
/// </summary>
public static class RazerOnboardMacroProtocol
{
    public static readonly RazerOnboardMacroCommand DeleteMacro = new(0x06, 0x03, 2);
    public static readonly RazerOnboardMacroCommand AllocateMacroData = new(0x06, 0x08, 6);
    public static readonly RazerOnboardMacroCommand SetMacroMemory = new(0x06, 0x09, 80);
    public static readonly RazerOnboardMacroCommand GetMacroDataSize = new(0x06, 0x88, 6);
    public static readonly RazerOnboardMacroCommand GetMacroMemory = new(0x06, 0x89, 80);
    public static readonly RazerOnboardMacroCommand GetMacroStorageInfo = new(0x06, 0x8E, 14);
    public static readonly RazerOnboardMacroCommand GetMacroIdList = new(0x06, 0x8B, 80);

    public const int ChunkSize = 72;
    public const int AllocationGranularity = 256;
    private const int ArgumentOffset = RazerFeatureReport.ArgumentsOffset;

    /// <summary>
    /// Synapse keeps 256 bytes of name/header space per macro and rounds the whole
    /// allocation up to the next 256-byte boundary.
    /// </summary>
    public static int RequiredAllocation(int macroDataSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(macroDataSize);
        return AllocationGranularity *
            (int)Math.Ceiling((macroDataSize + (double)AllocationGranularity) / AllocationGranularity);
    }

    public static byte[] CreateReadMemoryArguments(byte macroId, int offset, int length)
    {
        if (offset is < 0 or > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        if (length is <= 0 or > ChunkSize)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        return
        [
            0,
            macroId,
            (byte)(offset >> 24),
            (byte)(offset >> 16),
            (byte)(offset >> 8),
            (byte)offset,
            (byte)length,
        ];
    }

    public static byte[] CreateWriteMemoryArguments(
        byte macroId,
        int offset,
        ReadOnlySpan<byte> data)
    {
        if (data.Length is <= 0 or > ChunkSize)
        {
            throw new ArgumentOutOfRangeException(nameof(data));
        }

        var arguments = new byte[7 + data.Length];
        CreateReadMemoryArguments(macroId, offset, data.Length).CopyTo(arguments, 0);
        data.CopyTo(arguments.AsSpan(7));
        return arguments;
    }

    public static byte[] CreateAllocateArguments(byte macroId, int allocatedSize)
    {
        if (allocatedSize < 0 || allocatedSize % AllocationGranularity != 0)
        {
            throw new ArgumentException(
                "Allocated macro memory must be a whole number of 256-byte blocks.",
                nameof(allocatedSize));
        }

        return
        [
            0,
            macroId,
            (byte)(allocatedSize >> 24),
            (byte)(allocatedSize >> 16),
            (byte)(allocatedSize >> 8),
            (byte)allocatedSize,
        ];
    }

    public static RazerOnboardMacroMemoryChunk ParseMemoryChunk(ReadOnlySpan<byte> response)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(response.Length, ArgumentOffset + 7);
        var arguments = response[ArgumentOffset..];
        var offset = (arguments[2] << 24) | (arguments[3] << 16) | (arguments[4] << 8) | arguments[5];
        var size = arguments[6];
        if (arguments.Length < 7 + size)
        {
            throw new InvalidOperationException(
                $"Onboard macro chunk declares {size} bytes but the response carries {arguments.Length - 7}.");
        }

        return new RazerOnboardMacroMemoryChunk(offset, arguments.Slice(7, size).ToArray());
    }

    public static RazerOnboardMacroStorageInfo ParseStorageInfo(ReadOnlySpan<byte> response)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(response.Length, ArgumentOffset + 14);
        var data = response.Slice(ArgumentOffset, 14);
        return new RazerOnboardMacroStorageInfo(
            (data[0] << 8) | data[1],
            (data[2] << 24) | (data[3] << 16) | (data[4] << 8) | data[5],
            (data[6] << 24) | (data[7] << 16) | (data[8] << 8) | data[9],
            (data[10] << 24) | (data[11] << 16) | (data[12] << 8) | data[13]);
    }


    public static int ParseMacroDataSize(ReadOnlySpan<byte> response)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(response.Length, ArgumentOffset + 6);
        var data = response[ArgumentOffset..];
        return (data[2] << 24) | (data[3] << 16) | (data[4] << 8) | data[5];
    }

    public static byte[] CreateMacroIdArguments(byte macroId) => [0, macroId];

    public static byte[] CreateMacroIdListArguments(int offset)
    {
        if (offset is < 0 or > 0xFFFF)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        return [(byte)(offset >> 8), (byte)offset];
    }

    /// <summary>
    /// Get Macro ID List answers with a two-byte offset echo, a two-byte total count
    /// and the ids as big-endian 16-bit values.
    /// </summary>
    public static (int Total, byte[] Ids) ParseMacroIdList(ReadOnlySpan<byte> response)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(response.Length, ArgumentOffset + 4);
        var data = response[ArgumentOffset..];
        var total = (data[2] << 8) | data[3];
        var returned = Math.Min((data.Length - 4) / 2, total);
        var ids = new byte[returned];
        for (var index = 0; index < returned; index++)
        {
            var id = (data[4 + (index * 2)] << 8) | data[5 + (index * 2)];
            if (id is <= 0 or > byte.MaxValue)
            {
                throw new InvalidOperationException($"Onboard macro id {id} does not fit a byte slot.");
            }

            ids[index] = (byte)id;
        }

        return (total, ids);
    }
}

