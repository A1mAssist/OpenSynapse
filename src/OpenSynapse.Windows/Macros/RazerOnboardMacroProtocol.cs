using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Macros;

/// <summary>
/// One onboard-memory command. Class, id and payload size are the vectors declared by
/// Synapse's protocol 25 keyboard class (rzDevice25): 06:03 delete, 06:08 allocate,
/// 06:09 write, 06:80 count, 06:88 data size, 06:89 memory read, 06:8E storage info.
/// </summary>
public sealed record RazerOnboardMacroCommand(byte CommandClass, byte CommandId, byte DataSize)
{
    public byte[] CreateRequest(
        byte transactionId,
        ReadOnlySpan<byte> arguments,
        int? declaredSize = null)
    {
        var size = declaredSize ?? DataSize;
        if (size < 0 || size > DataSize || arguments.Length > size)
        {
            throw new ArgumentException(
                $"Command 0x{CommandClass:X2}:0x{CommandId:X2} accepts at most {DataSize} bytes, not {arguments.Length} arguments with declared size {size}.",
                nameof(arguments));
        }

        return RazerFeatureReport.CreateRequestWithDeclaredSize(
            transactionId,
            (byte)size,
            CommandClass,
            CommandId,
            arguments);
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
    public static readonly RazerOnboardMacroCommand GetMacroCount = new(0x06, 0x80, 2);
    public static readonly RazerOnboardMacroCommand GetMacroDataSize = new(0x06, 0x88, 6);
    public static readonly RazerOnboardMacroCommand GetMacroMemory = new(0x06, 0x89, 80);
    public static readonly RazerOnboardMacroCommand GetMacroStorageInfo = new(0x06, 0x8E, 14);
    public static readonly RazerOnboardMacroCommand GetMacroIdList = new(0x06, 0x8B, 80);
    public static readonly RazerOnboardMacroCommand GetMacroName = new(0x06, 0x8C, 80);
    public static readonly RazerOnboardMacroCommand SetMacroName = new(0x06, 0x0C, 80);

    public const int ChunkSize = 72;
    public const int NameStructureSize = 250;
    public const int NameChunkSize = 64;
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

    public static RazerOnboardMacroMemoryChunk ParseMemoryChunk(ReadOnlySpan<byte> response, byte? expectedMacroId = null)
    {
        var arguments = Arguments(response, 7);
        RequireMacroId(arguments, expectedMacroId);
        var offset = (arguments[2] << 24) | (arguments[3] << 16) | (arguments[4] << 8) | arguments[5];
        var size = arguments[6];
        if (size is 0 or > ChunkSize || arguments.Length < 7 + size)
        {
            throw new InvalidOperationException(
                $"Onboard macro chunk declares {size} bytes but the response carries {arguments.Length - 7}.");
        }

        return new RazerOnboardMacroMemoryChunk(offset, arguments.Slice(7, size).ToArray());
    }

    public static RazerOnboardMacroStorageInfo ParseStorageInfo(ReadOnlySpan<byte> response)
    {
        var data = Arguments(response, 14);
        return new RazerOnboardMacroStorageInfo(
            (data[0] << 8) | data[1],
            (data[2] << 24) | (data[3] << 16) | (data[4] << 8) | data[5],
            (data[6] << 24) | (data[7] << 16) | (data[8] << 8) | data[9],
            (data[10] << 24) | (data[11] << 16) | (data[12] << 8) | data[13]);
    }


    public static int ParseMacroDataSize(ReadOnlySpan<byte> response, byte? expectedMacroId = null)
    {
        var data = Arguments(response, 6);
        RequireMacroId(data, expectedMacroId);
        return (data[2] << 24) | (data[3] << 16) | (data[4] << 8) | data[5];
    }

    private static ReadOnlySpan<byte> Arguments(ReadOnlySpan<byte> response, int minimumLength)
    {
        if (response.Length != RazerFeatureReport.Length ||
            response[RazerFeatureReport.DataSizeOffset] < minimumLength ||
            response[RazerFeatureReport.DataSizeOffset] > 80)
            throw new InvalidDataException("Onboard macro response has an invalid declared payload length.");
        return response.Slice(ArgumentOffset, response[RazerFeatureReport.DataSizeOffset]);
    }

    private static void RequireMacroId(ReadOnlySpan<byte> data, byte? expectedMacroId)
    {
        if (expectedMacroId is { } id && (data[0] != 0 || data[1] != id))
            throw new InvalidDataException("Onboard macro response belongs to a different slot.");
    }

    public static byte[] CreateMacroIdArguments(byte macroId) => [0, macroId];

    public static byte[] CreateReadNameArguments(byte macroId, int offset)
    {
        if (offset is < 0 or > 0xFFFF)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        return [0, macroId, (byte)(offset >> 8), (byte)offset];
    }

    public static byte[] CreateWriteNameArguments(
        byte macroId,
        int offset,
        ReadOnlySpan<byte> data)
    {
        if (offset is < 0 or > 0xFFFF)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        if (data.Length is <= 0 or > NameChunkSize)
        {
            throw new ArgumentOutOfRangeException(nameof(data));
        }

        var arguments = new byte[6 + data.Length];
        arguments[0] = 0;
        arguments[1] = macroId;
        arguments[2] = (byte)(offset >> 8);
        arguments[3] = (byte)offset;
        arguments[4] = (byte)(NameStructureSize >> 8);
        arguments[5] = (byte)NameStructureSize;
        data.CopyTo(arguments.AsSpan(6));
        return arguments;
    }

    public static byte[] ParseNameChunk(
        ReadOnlySpan<byte> response,
        byte expectedMacroId,
        int expectedOffset,
        int expectedLength)
    {
        var arguments = Arguments(response, 0);
        if (expectedLength is <= 0 or > NameChunkSize)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedLength));
        }

        // Firmware variants either return the payload alone or repeat the id/offset
        // header in front of it. Both shapes are accepted, but a mismatched header is
        // not silently treated as data.
        if (arguments.Length >= 6 + expectedLength)
        {
            if (arguments[0] != 0 || arguments[1] != expectedMacroId ||
                ((arguments[2] << 8) | arguments[3]) != expectedOffset)
            {
                throw new InvalidDataException("Onboard macro name response belongs to a different slot.");
            }

            return arguments.Slice(6, expectedLength).ToArray();
        }

        if (arguments.Length < expectedLength)
        {
            throw new InvalidDataException(
                $"Onboard macro name chunk returned {arguments.Length} bytes, expected {expectedLength}.");
        }

        return arguments.Slice(0, expectedLength).ToArray();
    }

    public static byte[] CreateMacroIdListArguments(int offset)
    {
        if (offset is < 0 or > 0xFFFF)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        return [(byte)(offset >> 8), (byte)offset];
    }

    /// <summary>
    /// Macro count is read independently. Each ID page echoes its offset, carries a
    /// firmware size field, then the caller-requested IDs as big-endian 16-bit values.
    /// </summary>
    public static int ParseMacroCount(ReadOnlySpan<byte> response)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(response.Length, ArgumentOffset + 2);
        if (response[RazerFeatureReport.DataSizeOffset] != 2)
        {
            throw new InvalidOperationException(
                $"Onboard macro count declares {response[RazerFeatureReport.DataSizeOffset]} bytes, expected 2.");
        }
        return (response[ArgumentOffset] << 8) | response[ArgumentOffset + 1];
    }

    public static byte[] ParseMacroIdList(
        ReadOnlySpan<byte> response,
        int expectedOffset,
        int expectedCount)
    {
        if (expectedOffset is < 0 or > 0xFFFF)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedOffset));
        }
        if (expectedCount is < 0 or > 32)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedCount));
        }

        var declaredLength = 4 + (expectedCount * 2);
        ArgumentOutOfRangeException.ThrowIfLessThan(response.Length, ArgumentOffset + declaredLength);
        if (response[RazerFeatureReport.DataSizeOffset] != declaredLength)
        {
            throw new InvalidOperationException(
                $"Onboard macro id page declares {response[RazerFeatureReport.DataSizeOffset]} bytes, expected {declaredLength}.");
        }

        var data = response.Slice(ArgumentOffset, declaredLength);
        var returnedOffset = (data[0] << 8) | data[1];
        if (returnedOffset != expectedOffset)
        {
            throw new InvalidOperationException(
                $"Onboard macro id page returned offset {returnedOffset}, expected {expectedOffset}.");
        }

        var ids = new byte[expectedCount];
        for (var index = 0; index < expectedCount; index++)
        {
            var id = (data[4 + (index * 2)] << 8) | data[5 + (index * 2)];
            if (id is <= 0 or > byte.MaxValue)
            {
                throw new InvalidOperationException($"Onboard macro id {id} does not fit a byte slot.");
            }

            ids[index] = (byte)id;
        }

        return ids;
    }
}

