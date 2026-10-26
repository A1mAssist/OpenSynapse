using System.ComponentModel;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Macros;

/// <summary>
/// Reads and rewrites the onboard macro block of a protocol 25 device. The wire format
/// comes from Synapse's rzDevice25 macro methods, which the keyboards and the
/// macro-capable mice (Naga Trinity, Naga Left Handed 2020, Naga Classic, Turret) share.
/// </summary>
public sealed class RazerOnboardMacroService
{
    private readonly IRazerFeatureTransport _transport;
    private readonly string _devicePath;
    private readonly TimeSpan _deviceWait;
    private readonly Action<string>? _diagnostic;
    private byte _transactionId = 0x1F;

    public RazerOnboardMacroService(
        IRazerFeatureTransport transport,
        string devicePath,
        TimeSpan deviceWait,
        Action<string>? diagnostic = null)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _devicePath = string.IsNullOrWhiteSpace(devicePath)
            ? throw new ArgumentException("A HID device path is required.", nameof(devicePath))
            : devicePath;
        _deviceWait = deviceWait;
        _diagnostic = diagnostic;
    }

    public static bool IsSupportedProduct(ushort productId) =>
        RazerOnboardMacroDevices.Products.Contains(productId);

    /// <summary>
    /// Returns the storage block, or null when the firmware does not answer the command.
    /// Null keeps the whole feature hidden instead of showing an empty page.
    /// </summary>
    public async Task<RazerOnboardMacroStorageInfo?> ProbeStorageAsync(
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
            _diagnostic?.Invoke($"onboard macro storage unavailable: {exception.Message}");
            return null;
        }
    }

    public async Task<IReadOnlyList<RazerOnboardMacro>> ReadAllAsync(
        CancellationToken cancellationToken = default)
    {
        var ids = await ReadIdsAsync(cancellationToken).ConfigureAwait(false);
        var macros = new List<RazerOnboardMacro>(ids.Count);
        foreach (var id in ids)
        {
            var macro = await ReadAsync(id, cancellationToken).ConfigureAwait(false);
            if (macro is not null)
            {
                macros.Add(macro);
            }
        }

        return macros;
    }

    public async Task<RazerOnboardMacro?> ReadAsync(
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
            _diagnostic?.Invoke($"onboard macro {id} size unavailable: {exception.Message}");
            return null;
        }

        if (dataSize is <= 0 or > 64 * 1024)
        {
            _diagnostic?.Invoke($"onboard macro {id} declares an implausible size {dataSize}; skipped");
            return null;
        }

        var raw = await ReadMemoryAsync(id, dataSize, cancellationToken).ConfigureAwait(false);
        return new RazerOnboardMacro(id, dataSize, raw, RazerOnboardMacroCodec.Decode(raw));
    }

    /// <summary>
    /// Rewrites one macro and confirms it by reading the bytes back. Callers keep the
    /// original RazerOnboardMacro.Raw so a failed round trip can be restored.
    /// </summary>
    public async Task WriteAsync(
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

        // Keep whatever the slot held so a failed write cannot silently destroy it.
        var backup = await TryReadRawAsync(id, cancellationToken).ConfigureAwait(false);
        try
        {
            await WriteRawAsync(id, payload, cancellationToken).ConfigureAwait(false);
            var written = await ReadMemoryAsync(id, payload.Length, cancellationToken).ConfigureAwait(false);
            if (!written.AsSpan().SequenceEqual(payload))
            {
                throw new InvalidOperationException(
                    $"Onboard macro {id} did not read back the bytes that were written.");
            }
        }
        catch
        {
            if (backup is { Length: > 0 })
            {
                try
                {
                    await WriteRawAsync(id, backup, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception restoreFailure) when (IsExpected(restoreFailure))
                {
                    _diagnostic?.Invoke(
                        $"onboard macro {id} could not be restored: {restoreFailure.Message}");
                }
            }

            throw;
        }
    }

    private async Task WriteRawAsync(byte id, byte[] payload, CancellationToken cancellationToken)
    {
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
    }

    private async Task<byte[]?> TryReadRawAsync(byte id, CancellationToken cancellationToken)
    {
        try
        {
            var existing = await ReadAsync(id, cancellationToken).ConfigureAwait(false);
            return existing?.Raw;
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            return null;
        }
    }

    private async Task<IReadOnlyList<byte>> ReadIdsAsync(CancellationToken cancellationToken)
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

    private async Task<byte[]> ReadMemoryAsync(byte id, int size, CancellationToken cancellationToken)
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

    private async Task<byte[]> QueryAsync(
        Func<byte, byte[]> request,
        string label,
        CancellationToken cancellationToken)
    {
        var report = request(NextTransactionId());
        _diagnostic?.Invoke(
            $"onboard macro {label}: size={report[6]}, class=0x{report[7]:X2}, command=0x{report[8]:X2}");
        return await _transport.QueryPreparedAsync(
            _devicePath, report, _deviceWait, cancellationToken).ConfigureAwait(false);
    }

    private byte NextTransactionId()
    {
        if (_transactionId >= 0x1F)
        {
            _transactionId = 0;
        }
        else
        {
            _transactionId++;
        }

        return _transactionId;
    }

    private static bool IsExpected(Exception exception) =>
        exception is IOException or InvalidDataException or InvalidOperationException or
            NotSupportedException or Win32Exception or UnauthorizedAccessException;
}
