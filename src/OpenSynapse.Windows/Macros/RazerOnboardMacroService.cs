using System.Collections.Concurrent;
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
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> TransactionGates =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan RestoreTimeout = TimeSpan.FromSeconds(10);
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

    /// <summary>
    /// Reads only slot ids and name blocks. The mapping page uses this to show macro
    /// names without pulling every macro data stream off the device.
    /// </summary>
    public async Task<IReadOnlyList<RazerOnboardMacroSlotInfo>> ReadSlotInfosAsync(
        CancellationToken cancellationToken = default)
    {
        var ids = await ReadIdsAsync(cancellationToken).ConfigureAwait(false);
        var slots = new List<RazerOnboardMacroSlotInfo>(ids.Count);
        foreach (var id in ids)
        {
            slots.Add(new RazerOnboardMacroSlotInfo(
                id,
                await ReadNameAsync(id, cancellationToken).ConfigureAwait(false)));
        }

        return slots;
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
                    cancellationToken).ConfigureAwait(false), id);
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
        var name = await ReadNameAsync(id, cancellationToken).ConfigureAwait(false);
        return new RazerOnboardMacro(id, dataSize, raw, RazerOnboardMacroCodec.Decode(raw), name);
    }

    /// <summary>
    /// Reads the 250-byte name block in 64-byte pages. Firmware that does not answer
    /// the command returns null so the slot itself remains usable by id.
    /// </summary>
    public async Task<RazerOnboardMacroName?> ReadNameAsync(
        byte id,
        CancellationToken cancellationToken = default)
    {
        if (id == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(id));
        }

        try
        {
            return RazerOnboardMacroName.Decode(
                await ReadNameRawAsync(id, cancellationToken).ConfigureAwait(false));
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            _diagnostic?.Invoke($"onboard macro {id} name unavailable: {exception.Message}");
            return null;
        }
    }

    /// <summary>
    /// Rewrites one macro and confirms it by reading the bytes back. Callers keep the
    /// original RazerOnboardMacro.Raw so a failed round trip can be restored.
    /// </summary>
    public Task WriteAsync(
        byte id,
        IReadOnlyList<OnboardMacroItem> items,
        CancellationToken cancellationToken = default) =>
        WriteAsync(id, items, null, cancellationToken);

    public async Task WriteAsync(
        byte id,
        IReadOnlyList<OnboardMacroItem> items,
        RazerOnboardMacroName? name,
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

        var gate = TransactionGates.GetOrAdd(_devicePath, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var allocation = RazerOnboardMacroProtocol.RequiredAllocation(payload.Length);
            if (await ProbeStorageAsync(cancellationToken).ConfigureAwait(false) is { } storage &&
                allocation > storage.MaxMemory)
            {
                throw new InvalidOperationException(
                    $"Onboard macro needs {allocation} bytes of device storage, but this device holds at most {storage.MaxMemory}.");
            }

            var exists = (await ReadIdsAsync(cancellationToken).ConfigureAwait(false)).Contains(id);
            var backup = exists
                ? await ReadRawAsync(id, cancellationToken).ConfigureAwait(false)
                : null;
            var nameBackup = exists && name is not null
                ? await ReadNameRawAsync(id, cancellationToken).ConfigureAwait(false)
                : null;
            try
            {
                await WriteRawAsync(id, payload, cancellationToken).ConfigureAwait(false);
                await VerifyRawAsync(id, payload, cancellationToken).ConfigureAwait(false);
                if (name is not null)
                {
                    var encodedName = name.Encode();
                    await WriteNameRawAsync(id, encodedName, cancellationToken).ConfigureAwait(false);
                    await VerifyNameRawAsync(id, encodedName, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception writeFailure)
            {
                if (backup is { Length: > 0 })
                {
                    try
                    {
                        using var restore = new CancellationTokenSource(RestoreTimeout);
                        await WriteRawAsync(id, backup, restore.Token).ConfigureAwait(false);
                        await VerifyRawAsync(id, backup, restore.Token).ConfigureAwait(false);
                        if (nameBackup is { Length: > 0 })
                        {
                            await WriteNameRawAsync(id, nameBackup, restore.Token).ConfigureAwait(false);
                            await VerifyNameRawAsync(id, nameBackup, restore.Token).ConfigureAwait(false);
                        }
                    }
                    catch (Exception restoreFailure) when (
                        IsExpected(restoreFailure) || restoreFailure is OperationCanceledException)
                    {
                        _diagnostic?.Invoke(
                            $"onboard macro {id} could not be restored: {restoreFailure.Message}");
                        throw new InvalidOperationException(
                            $"Onboard macro {id} write failed and the original slot could not be restored. Check the slot before using it.",
                            new AggregateException(writeFailure, restoreFailure));
                    }
                }

                if (backup is null)
                {
                    try
                    {
                        using var restore = new CancellationTokenSource(RestoreTimeout);
                        await DeleteAsync(id, restore.Token).ConfigureAwait(false);
                    }
                    catch (Exception deleteFailure) when (
                        IsExpected(deleteFailure) || deleteFailure is OperationCanceledException)
                    {
                        _diagnostic?.Invoke(
                            $"onboard macro {id} could not be deleted after a failed write: {deleteFailure.Message}");
                    }
                }

                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(writeFailure).Throw();
                throw;
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task DeleteAsync(byte id, CancellationToken cancellationToken = default)
    {
        if (id == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(id));
        }

        await QueryAsync(
            transactionId => RazerOnboardMacroProtocol.DeleteMacro.CreateRequest(
                transactionId, RazerOnboardMacroProtocol.CreateMacroIdArguments(id)),
            $"macro {id} delete",
            cancellationToken).ConfigureAwait(false);
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
                transactionId => RazerOnboardMacroProtocol.SetMacroMemory.CreateRequest(transactionId, arguments, 7 + length),
                $"macro {id} write {offset}",
                cancellationToken).ConfigureAwait(false);
            offset += length;
        }
    }

    private async Task<byte[]> ReadNameRawAsync(byte id, CancellationToken cancellationToken)
    {
        var data = new byte[RazerOnboardMacroProtocol.NameStructureSize];
        for (var offset = 0; offset < data.Length;)
        {
            var length = Math.Min(RazerOnboardMacroProtocol.NameChunkSize, data.Length - offset);
            var arguments = RazerOnboardMacroProtocol.CreateReadNameArguments(id, offset);
            var response = await QueryAsync(
                transactionId => RazerOnboardMacroProtocol.GetMacroName.CreateRequest(
                    transactionId, arguments),
                $"macro {id} name {offset}",
                cancellationToken).ConfigureAwait(false);
            RazerOnboardMacroProtocol.ParseNameChunk(response, id, offset, length)
                .CopyTo(data, offset);
            offset += length;
        }

        return data;
    }

    private async Task WriteNameRawAsync(
        byte id,
        byte[] data,
        CancellationToken cancellationToken)
    {
        for (var offset = 0; offset < data.Length;)
        {
            var length = Math.Min(RazerOnboardMacroProtocol.NameChunkSize, data.Length - offset);
            var arguments = RazerOnboardMacroProtocol.CreateWriteNameArguments(
                id,
                offset,
                data.AsSpan(offset, length));
            await QueryAsync(
                transactionId => RazerOnboardMacroProtocol.SetMacroName.CreateRequest(
                    transactionId, arguments),
                $"macro {id} name write {offset}",
                cancellationToken).ConfigureAwait(false);
            offset += length;
        }
    }

    private async Task VerifyNameRawAsync(
        byte id,
        byte[] expected,
        CancellationToken cancellationToken)
    {
        var written = await ReadNameRawAsync(id, cancellationToken).ConfigureAwait(false);
        if (!written.AsSpan().SequenceEqual(expected))
        {
            throw new InvalidOperationException(
                $"Onboard macro {id} did not read back the name block that was written.");
        }
    }

    private async Task<byte[]> ReadRawAsync(byte id, CancellationToken cancellationToken)
    {
        var dataSize = RazerOnboardMacroProtocol.ParseMacroDataSize(
            await QueryAsync(
                transactionId => RazerOnboardMacroProtocol.GetMacroDataSize.CreateRequest(
                    transactionId,
                    RazerOnboardMacroProtocol.CreateMacroIdArguments(id)),
                $"macro {id} backup size",
                cancellationToken).ConfigureAwait(false), id);
        if (dataSize is <= 0 or > 64 * 1024)
        {
            throw new InvalidDataException(
                $"Onboard macro {id} declares an implausible backup size {dataSize}.");
        }

        return await ReadMemoryAsync(id, dataSize, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<byte>> ReadIdsAsync(CancellationToken cancellationToken = default)
    {
        var total = RazerOnboardMacroProtocol.ParseMacroCount(
            await QueryAsync(
                transactionId => RazerOnboardMacroProtocol.GetMacroCount.CreateRequest(transactionId, []),
                "macro count",
                cancellationToken).ConfigureAwait(false));
        if (total > byte.MaxValue)
        {
            throw new InvalidDataException($"Onboard macro count {total} exceeds the 255-slot protocol limit.");
        }

        var ids = new List<byte>(total);
        while (ids.Count < total)
        {
            var count = Math.Min(32, total - ids.Count);
            var window = RazerOnboardMacroProtocol.ParseMacroIdList(
                await QueryAsync(
                    transactionId => RazerOnboardMacroProtocol.GetMacroIdList.CreateRequest(
                        transactionId,
                        RazerOnboardMacroProtocol.CreateMacroIdListArguments(ids.Count),
                        4 + (count * 2)),
                    "macro id list",
                    cancellationToken).ConfigureAwait(false),
                ids.Count,
                count);

            if (window.Any(ids.Contains))
            {
                throw new InvalidDataException("Onboard macro id paging returned a duplicate slot.");
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
                        RazerOnboardMacroProtocol.CreateReadMemoryArguments(id, offset, length),
                        7 + length),
                    $"macro {id} read {offset}",
                    cancellationToken).ConfigureAwait(false), id);

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

    private async Task VerifyRawAsync(byte id, byte[] expected, CancellationToken cancellationToken)
    {
        var written = await ReadMemoryAsync(id, expected.Length, cancellationToken).ConfigureAwait(false);
        if (!written.AsSpan().SequenceEqual(expected))
        {
            throw new InvalidOperationException(
                $"Onboard macro {id} did not read back the bytes that were written.");
        }
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
