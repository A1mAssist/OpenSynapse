using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.ComponentModel;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed partial class RazerHidDeviceService
{
    private readonly ConcurrentDictionary<string, string?> _legacyControlPaths =
        new(StringComparer.OrdinalIgnoreCase);

    // The five Synapse 2 keypads predate the per-key OBM tables: their 20 main
    // keys live in one P2 profile blob, not behind 0x8D/0x9E.
    private static bool UsesLegacyKeypadProfile(RazerHidDeviceConnection connection) =>
        UsesLegacyKeypadProfile(connection.Definition.ProductId);

    private static bool UsesLegacyKeypadProfile(ushort productId) =>
        productId is 0x0111 or 0x0113 or 0x0201 or 0x0207 or 0x0208;

    private async Task<RazerHidButtonAssignment> GetLegacyAssignmentAsync(
        RazerHidDeviceConnection connection,
        byte buttonId,
        byte mode,
        CancellationToken cancellationToken)
    {
        if (IsLegacyDpadDirection(buttonId))
        {
            return await GetLegacyDpadAsync(
                connection, buttonId, mode, cancellationToken).ConfigureAwait(false);
        }

        var records = await ReadLegacyProfileAsync(connection, cancellationToken)
            .ConfigureAwait(false);
        var record = records.FirstOrDefault(item => item.IsValid && item.InputId == buttonId);
        return record.InputId == buttonId
            ? RazerKeypadProfileCodec.ToAssignment(buttonId, mode, record)
            : new RazerHidButtonAssignment(buttonId, mode, RazerHidMappingFunctions.Off, []);
    }

    private async Task SetLegacyAssignmentAsync(
        RazerHidDeviceConnection connection,
        byte buttonId,
        byte mode,
        byte functionId,
        IReadOnlyList<byte> data,
        CancellationToken cancellationToken)
    {
        if (IsLegacyDpadDirection(buttonId))
        {
            await SetLegacyDpadAsync(
                connection, buttonId, mode, functionId, data, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        var records = (await ReadLegacyProfileAsync(connection, cancellationToken)
            .ConfigureAwait(false)).ToList();
        var next = RazerKeypadProfileCodec.FromAssignment(
            buttonId,
            new RazerHidButtonAssignment(buttonId, mode, functionId, data));
        var index = records.FindIndex(item => item.IsValid && item.InputId == buttonId);
        if (index >= 0)
        {
            records[index] = next;
        }
        else
        {
            records.Add(next);
        }

        await WriteLegacyProfileAsync(connection, records, cancellationToken).ConfigureAwait(false);
        var applied = await GetLegacyAssignmentAsync(
            connection, buttonId, mode, cancellationToken).ConfigureAwait(false);
        if (applied.FunctionId != functionId || !applied.Data.SequenceEqual(data))
        {
            throw new InvalidOperationException(
                $"Keypad input {buttonId} kept a different assignment after the P2 write.");
        }
    }

    private static bool IsLegacyDpadDirection(byte buttonId) =>
        buttonId is >= 0xF0 and <= 0xF8;

    private async Task<RazerHidButtonAssignment> GetLegacyDpadAsync(
        RazerHidDeviceConnection connection,
        byte buttonId,
        byte mode,
        CancellationToken cancellationToken)
    {
        var index = buttonId - 0xF0;
        var response = await ExecuteAsync(
                connection,
                RazerKeypadDpadProtocol.GetDirection(index),
                cancellationToken)
            .ConfigureAwait(false);
        return RazerKeypadDpadProtocol.ParseDirection(response, index, buttonId, mode);
    }

    private async Task SetLegacyDpadAsync(
        RazerHidDeviceConnection connection,
        byte buttonId,
        byte mode,
        byte functionId,
        IReadOnlyList<byte> data,
        CancellationToken cancellationToken)
    {
        var index = buttonId - 0xF0;
        await ExecuteWithoutResultAsync(
                connection,
                RazerKeypadDpadProtocol.SetDirection(index, functionId, data),
                cancellationToken)
            .ConfigureAwait(false);
        var applied = await GetLegacyDpadAsync(
            connection, buttonId, mode, cancellationToken).ConfigureAwait(false);
        if (applied.FunctionId != functionId || !applied.Data.SequenceEqual(data))
        {
            throw new InvalidOperationException(
                $"Keypad D-pad input {buttonId} kept a different assignment.");
        }
    }

    private async Task<IReadOnlyList<RazerKeypadProfileRecord>> ReadLegacyProfileAsync(
        RazerHidDeviceConnection connection,
        CancellationToken cancellationToken)
    {
        var path = await ResolveLegacyControlPathAsync(connection, cancellationToken)
            .ConfigureAwait(false);
        var countReply = await RazerFilterControlTransport.TransferAsync(
                path,
                RazerKeypadProfileProtocol.CreateReadPacket(0),
                sizeof(uint),
                cancellationToken)
            .ConfigureAwait(false);
        if (countReply.Length < sizeof(uint))
        {
            throw new InvalidDataException(
                $"The keypad profile count reply was {countReply.Length} bytes.");
        }

        var count = BinaryPrimitives.ReadUInt32LittleEndian(countReply);
        if (count == 0)
        {
            return [];
        }

        count = Math.Min(count, RazerKeypadProfileProtocol.MaxRecordsPerChunk);
        var outputLength = sizeof(uint) + (int)count * RazerKeypadProfileProtocol.RecordLength;
        var output = await RazerFilterControlTransport.TransferAsync(
                path,
                RazerKeypadProfileProtocol.CreateReadPacket(count),
                outputLength,
                cancellationToken)
            .ConfigureAwait(false);
        return RazerKeypadProfileProtocol.ParseReadResponse(output);
    }

    private async Task WriteLegacyProfileAsync(
        RazerHidDeviceConnection connection,
        IReadOnlyList<RazerKeypadProfileRecord> records,
        CancellationToken cancellationToken)
    {
        var path = await ResolveLegacyControlPathAsync(connection, cancellationToken)
            .ConfigureAwait(false);
        foreach (var (input, outputLength) in RazerKeypadProfileProtocol.CreateWriteChunks(records))
        {
            await RazerFilterControlTransport.TransferAsync(
                    path, input, outputLength, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task<string> ResolveLegacyControlPathAsync(
        RazerHidDeviceConnection connection,
        CancellationToken cancellationToken)
    {
        if (_legacyControlPaths.TryGetValue(connection.InstanceId, out var cached) &&
            cached is not null)
        {
            return cached;
        }

        var candidates = new List<string?>
        {
            await RazerFilterEndpointDiscovery
                .DiscoverAsync(connection.Definition.ProductId, cancellationToken)
                .ConfigureAwait(false),
            @"\\.\RzUdd",
            @"\\.\RzControl",
        };
        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            try
            {
                _ = await RazerFilterControlTransport.TransferAsync(
                        candidate,
                        RazerKeypadProfileProtocol.CreateReadPacket(0),
                        sizeof(uint),
                        cancellationToken)
                    .ConfigureAwait(false);
                _legacyControlPaths[connection.InstanceId] = candidate;
                return candidate;
            }
            catch (Exception exception) when (
                exception is Win32Exception or InvalidDataException or IOException)
            {
                WriteDiagnostic(connection.Definition.ProductId,
                    $"keypad profile transport {candidate} unavailable: {exception.Message}");
            }
        }

        _legacyControlPaths[connection.InstanceId] = null;
        throw new NotSupportedException(
            $"Device 1532:{connection.Definition.ProductId:X4} has no usable RZCONTROL " +
            "endpoint for its P2 keypad profile.");
    }
}
