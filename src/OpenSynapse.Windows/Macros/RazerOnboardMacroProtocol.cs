using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Macros;

/// <summary>
/// One onboarding-memory command. The class/id/size tuples come from the per-device
/// handler tables in docs/MIDDLEWARE_DEVICE_PROTOCOLS.md (06:08, 06:09, 06:128,
/// 06:136, 06:137, 06:142 and the 04:xx name pair).
/// </summary>
public sealed record RazerOnboardMacroCommand(byte CommandClass, byte CommandId, byte DataSize)
{
    public byte[] CreateRequest(byte transactionId, ReadOnlySpan<byte> arguments) =>
        RazerFeatureReport.CreateRequest(transactionId, DataSize, CommandClass, CommandId, arguments);
}

public sealed record RazerOnboardMacroStorageInfo(byte[] Raw);

public sealed record RazerOnboardMacroMemoryChunk(int Offset, int Size, byte[] Data);

/// <summary>
/// Request shapes for the onboard macro block. Synapse's own request builders are not
/// present in the shipped chunks, so the argument order below is the documented
/// six-byte pattern: macro id, big-endian offset, length.
/// shortcut: request argument order is inferred, correct it after one hardware read.
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
    public static readonly RazerOnboardMacroCommand GetMacroName = new(0x04, 0x83, 0);
    public static readonly RazerOnboardMacroCommand SetMacroName = new(0x04, 0x03, 255);

    public const int ChunkSize = 72;
    public const int AllocationGranularity = 256;

    /// <summary>
    /// Synapse keeps 256 bytes of name/header space per macro and rounds the whole
    /// allocation up to the next 256-byte boundary.
    /// </summary>
    public static int RequiredAllocation(int macroDataSize)
    {
        if (macroDataSize < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(macroDataSize));
        }

        return AllocationGranularity * (int)Math.Ceiling((macroDataSize + (double)AllocationGranularity) / AllocationGranularity);
    }

    public static byte[] CreateReadMemoryArguments(byte macroId, int offset, int length)
    {
        if (offset is < 0 or > 0xFFFF)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        if (length is <= 0 or > ChunkSize)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        return [macroId, (byte)(offset >> 8), (byte)offset, (byte)length];
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
            macroId,
            (byte)(allocatedSize >> 24),
            (byte)(allocatedSize >> 16),
            (byte)(allocatedSize >> 8),
            (byte)allocatedSize,
        ];
    }

    public static RazerOnboardMacroMemoryChunk ParseMemoryChunk(ReadOnlySpan<byte> response)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(response.Length, RazerFeatureReport.ArgumentsOffset + 6);
        var arguments = response[RazerFeatureReport.ArgumentsOffset..];
        var offset = (arguments[2] << 8) | arguments[3];
        var size = (arguments[4] << 8) | arguments[5];
        if (size < 0 || arguments.Length < 6 + size)
        {
            throw new InvalidOperationException(
                $"Onboard macro chunk declares {size} bytes but the response carries {arguments.Length - 6}.");
        }

        return new RazerOnboardMacroMemoryChunk(offset, size, arguments.Slice(6, size).ToArray());
    }

    public static RazerOnboardMacroStorageInfo ParseStorageInfo(ReadOnlySpan<byte> response)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(response.Length, RazerFeatureReport.ArgumentsOffset + 14);
        return new RazerOnboardMacroStorageInfo(
            response.Slice(RazerFeatureReport.ArgumentsOffset, 14).ToArray());
    }

    public static int ParseMacroCount(ReadOnlySpan<byte> response)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(response.Length, RazerFeatureReport.ArgumentsOffset + 2);
        return (response[RazerFeatureReport.ArgumentsOffset] << 8) |
            response[RazerFeatureReport.ArgumentsOffset + 1];
    }

    public static int ParseMacroDataSize(ReadOnlySpan<byte> response)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(response.Length, RazerFeatureReport.ArgumentsOffset + 6);
        var arguments = response[RazerFeatureReport.ArgumentsOffset..];
        return (arguments[2] << 24) | (arguments[3] << 16) | (arguments[4] << 8) | arguments[5];
    }
}
