using OpenSynapse.Windows.Macros;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed partial class RazerKeyboardService
{
    /// <summary>
    /// Keyboards whose middleware model declares OBMSpecs.supportedFeature.macros.
    /// Products outside this set keep the onboard macro card hidden.
    /// </summary>
    public static readonly IReadOnlySet<ushort> OnboardMacroProducts = new HashSet<ushort>
    {
        0x02D0, 0x02D1, 0x02D8, 0x02E3, 0x02E4, 0x02E5, 0x02E6, 0x02EA, 0x02EB, 0x02F4, 0x02CC,
    };
    /// <summary>
    /// Returns the raw 14-byte storage block, or null when the firmware does not answer
    /// the onboard macro command. Null hides the whole feature instead of showing an
    /// empty page.
    /// </summary>
    public async Task<RazerOnboardMacroStorageInfo?> ProbeOnboardMacroStorageAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await QueryAsync(
                transactionId => RazerOnboardMacroProtocol.GetMacroStorageInfo.CreateRequest(transactionId, []),
                "macro storage",
                cancellationToken).ConfigureAwait(false);
            return RazerOnboardMacroProtocol.ParseStorageInfo(response);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            _diagnostic?.Invoke($"keyboard macro storage unavailable: {exception.Message}");
            return null;
        }
    }

    public async Task<IReadOnlyList<RazerOnboardMacro>> ReadOnboardMacrosAsync(
        CancellationToken cancellationToken = default)
    {
        var ids = await ReadOnboardMacroIdsAsync(cancellationToken).ConfigureAwait(false);
        var macros = new List<RazerOnboardMacro>(ids.Count);
        foreach (var id in ids)
        {
            var raw = await ReadOnboardMacroAsync(id, cancellationToken).ConfigureAwait(false);
            if (raw is not null)
            {
                macros.Add(raw);
            }
        }

        return macros;
    }

    /// <summary>
    /// Reads the id list instead of assuming the ids are 1..count, because deleting a
    /// macro leaves a hole that sequential iteration would read as a missing macro.
    /// </summary>
    private async Task<IReadOnlyList<byte>> ReadOnboardMacroIdsAsync(CancellationToken cancellationToken)
    {
        var ids = new List<byte>();
        var total = int.MaxValue;
        while (ids.Count < total && ids.Count < byte.MaxValue)
        {
            var (windowTotal, window) = RazerOnboardMacroProtocol.ParseMacroIdList(
                await QueryAsync(
                    transactionId => RazerOnboardMacroProtocol.GetMacroIdList.CreateRequest(
                        transactionId,
                        RazerOnboardMacroProtocol.CreateMacroIdListArguments(ids.Count)),
                    "macro id list",
                    cancellationToken).ConfigureAwait(false));

            total = windowTotal;
            if (window.Length == 0 || window.Any(ids.Contains))
            {
                break;
            }

            ids.AddRange(window);
        }

        return ids;
    }

    public async Task<RazerOnboardMacro?> ReadOnboardMacroAsync(
        byte id,
        CancellationToken cancellationToken = default)
    {
        if (id == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(id));
        }

        int dataSize;
        try
        {
            dataSize = RazerOnboardMacroProtocol.ParseMacroDataSize(
                await QueryAsync(
                    transactionId => RazerOnboardMacroProtocol.GetMacroDataSize.CreateRequest(
                        transactionId,
                        RazerOnboardMacroProtocol.CreateMacroIdArguments(id)),
                    $"macro {id} size",
                    cancellationToken).ConfigureAwait(false));
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            _diagnostic?.Invoke($"keyboard macro {id} size unavailable: {exception.Message}");
            return null;
        }

        if (dataSize is <= 0 or > 64 * 1024)
        {
            _diagnostic?.Invoke($"keyboard macro {id} declares an implausible size {dataSize}; skipped");
            return null;
        }

        var raw = await ReadMacroMemoryAsync(id, dataSize, cancellationToken).ConfigureAwait(false);
        return new RazerOnboardMacro(id, dataSize, raw, RazerOnboardMacroCodec.Decode(raw));
    }

    /// <summary>
    /// Rewrites one macro and verifies it by reading it back. The caller keeps the
    /// original RazerOnboardMacro.Raw so a failed round trip can be restored.
    /// </summary>
    public async Task WriteOnboardMacroAsync(
        byte id,
        IReadOnlyList<OnboardMacroItem> items,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (id == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(id));
        }

        var payload = RazerOnboardMacroCodec.Encode(items);
        if (payload.Length == 0)
        {
            throw new ArgumentException("An onboard macro needs at least one item.", nameof(items));
        }

        var allocated = RazerOnboardMacroProtocol.RequiredAllocation(payload.Length);
        await QueryAsync(
            transactionId => RazerOnboardMacroProtocol.AllocateMacroData.CreateRequest(
                transactionId,
                RazerOnboardMacroProtocol.CreateAllocateArguments(id, allocated)),
            $"macro {id} allocate",
            cancellationToken).ConfigureAwait(false);

        for (var offset = 0; offset < payload.Length;)
        {
            var length = Math.Min(RazerOnboardMacroProtocol.ChunkSize, payload.Length - offset);
            var arguments = RazerOnboardMacroProtocol.CreateWriteMemoryArguments(
                id,
                offset,
                payload.AsSpan(offset, length));

            await QueryAsync(
                transactionId => RazerOnboardMacroProtocol.SetMacroMemory.CreateRequest(transactionId, arguments),
                $"macro {id} write {offset}",
                cancellationToken).ConfigureAwait(false);
            offset += length;
        }

        var written = await ReadMacroMemoryAsync(id, payload.Length, cancellationToken).ConfigureAwait(false);
        if (!written.AsSpan().SequenceEqual(payload))
        {
            throw new InvalidOperationException(
                $"Onboard macro {id} did not read back the bytes that were written.");
        }
    }

    private async Task<byte[]> ReadMacroMemoryAsync(
        byte id,
        int size,
        CancellationToken cancellationToken)
    {
        var data = new byte[size];
        var offset = 0;
        while (offset < size)
        {
            var length = Math.Min(RazerOnboardMacroProtocol.ChunkSize, size - offset);
            var chunk = RazerOnboardMacroProtocol.ParseMemoryChunk(
                await QueryAsync(
                    transactionId => RazerOnboardMacroProtocol.GetMacroMemory.CreateRequest(
                        transactionId,
                        RazerOnboardMacroProtocol.CreateReadMemoryArguments(id, offset, length)),
                    $"macro {id} read {offset}",
                    cancellationToken).ConfigureAwait(false));

            if (chunk.Data.Length == 0 || chunk.Offset != offset || chunk.Data.Length > length)
            {
                throw new InvalidDataException(
                    $"Onboard macro {id} chunk at {offset} returned offset {chunk.Offset} and size {chunk.Data.Length}.");
            }

            chunk.Data.CopyTo(data, offset);
            offset += chunk.Data.Length;
        }

        return data;
    }
}


