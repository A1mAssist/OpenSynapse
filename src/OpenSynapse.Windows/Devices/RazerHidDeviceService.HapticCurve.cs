using OpenSynapse.Core.Profiles;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed partial class RazerHidDeviceService
{
    public static bool SupportsHapticCurve(ushort productId) => productId is 0x00A7 or 0x00A8;

    public Task<HapticScrollProfile> ReadHapticScrollProfileAsync(RazerHidDeviceConnection connection,
        byte profileId = 1, bool includeCustom = true, CancellationToken cancellationToken = default) =>
        RunExclusiveAsync(connection, token => ReadHapticScrollProfileCoreAsync(connection, profileId, includeCustom, token), cancellationToken);

    public Task<HapticScrollProfile> ApplyHapticScrollProfileAsync(RazerHidDeviceConnection connection,
        HapticScrollProfile desired, byte profileId = 1,
        Func<HapticScrollProfile, CancellationToken, Task>? persist = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(desired);
        desired.Validate();
        var draft = desired.Clone();
        return RunExclusiveAsync(connection, async token =>
        {
            RequireHapticCurve(connection);
            var previous = await ReadHapticScrollProfileCoreAsync(connection, profileId, draft.Custom is not null, token);
            var writeCustom = false;
            if (draft.Custom is { } custom)
            {
                custom.Metadata[0] = profileId;
                writeCustom = !custom.ContentEquals(previous.Custom!);
                if (writeCustom) await CheckHapticTableCapacityAsync(connection, custom, token);
            }
            try
            {
                await WriteHapticScrollProfileCoreAsync(connection, profileId, draft, writeCustom, token);
                var applied = await ReadHapticScrollProfileCoreAsync(connection, profileId, draft.Custom is not null, token);
                if (!draft.ContentEquals(applied)) throw new IOException("Hapticore readback did not match the requested settings.");
                if (persist is not null) await persist(applied, token);
                return applied;
            }
            catch (Exception error)
            {
                try
                {
                    await WriteHapticScrollProfileCoreAsync(connection, profileId, previous, previous.Custom is not null, CancellationToken.None);
                    var restored = await ReadHapticScrollProfileCoreAsync(connection, profileId, previous.Custom is not null, CancellationToken.None);
                    if (!previous.ContentEquals(restored)) throw new IOException("Hapticore rollback readback did not match the snapshot.");
                }
                catch (Exception rollback)
                {
                    throw new IOException($"Hapticore apply failed: {error.Message} Restore also failed: {rollback.Message}",
                        new AggregateException(error, rollback));
                }
                throw;
            }
        }, cancellationToken);
    }

    private static void RequireHapticCurve(RazerHidDeviceConnection connection)
    {
        RequireReady(connection);
        if (!SupportsHapticCurve(connection.Definition.ProductId))
            throw new NotSupportedException("Hapticore custom curves are supported only on Naga V2 Pro USB 00A7 and receiver 00A8.");
    }

    private async Task<HapticScrollProfile> ReadHapticScrollProfileCoreAsync(RazerHidDeviceConnection connection,
        byte profileId, bool includeCustom, CancellationToken token)
    {
        RequireHapticCurve(connection);
        var stages = await GetHapticScrollStagesAsync(connection, profileId, token);
        if (stages.ProfileId != profileId || stages.MaxStage is < 1 or > 6)
            throw new IOException("Hapticore returned a different profile or an invalid stage count.");
        var result = new HapticScrollProfile
        {
            ActiveStage = stages.ActiveStage,
            StageTypes = stages.StageTypes.Take(stages.MaxStage).Select(type => (byte)type).ToList(),
        };
        if (includeCustom)
        {
            var metadataRequest = RazerHapticoreScrollProtocol.CreateGetGeneratorCustomMetaDataRequest(MiddlewareTransactionId, profileId);
            var metadata = RazerHapticoreScrollProtocol.ParseGeneratorCustomMetaDataRaw(
                await HapticExchangeAsync(connection, metadataRequest, token), metadataRequest);
            var request = RazerHapticoreScrollProtocol.CreateGetGeneratorSettingRequest(MiddlewareTransactionId, profileId, 6);
            var generator = RazerHapticoreScrollProtocol.ParseGeneratorSetting(await HapticExchangeAsync(connection, request, token), request);
            if (metadata[0] != profileId || generator.ProfileId != profileId || generator.Stage != 6)
                throw new IOException("Hapticore returned a different custom profile or stage.");
            result.Custom = new HapticScrollCustomProfile
            {
                Metadata = metadata, Enabled = generator.Enabled, AlternateCurrent = generator.AlternateCurrent,
                ReductionAngleCw = generator.ReductionAngleCw, IndexCountAngleCw = generator.IndexCountAngleCw,
                ReductionAngleCcw = generator.ReductionAngleCcw, IndexCountAngleCcw = generator.IndexCountAngleCcw,
                Cw = await ReadHapticTableAsync(connection, profileId, RazerHapticoreScrollTableClass.CwTable, generator.CurrentTableSizeCw, token),
                Ccw = await ReadHapticTableAsync(connection, profileId, RazerHapticoreScrollTableClass.CcwTable, generator.CurrentTableSizeCcw, token),
                Velocity = await ReadHapticTableAsync(connection, profileId, RazerHapticoreScrollTableClass.VelocityFactorTable, generator.VelocityFactorTableSize, token),
            };
        }
        result.Validate();
        return result;
    }

    private async Task<List<HapticScrollPoint>> ReadHapticTableAsync(RazerHidDeviceConnection connection,
        byte profileId, RazerHapticoreScrollTableClass tableClass, ushort size, CancellationToken token)
    {
        var points = new List<HapticScrollPoint>(size);
        for (var offset = 0; offset < size; offset += RazerHapticoreScrollProtocol.NativeGeneratorTableChunkPoints)
        {
            var count = (ushort)Math.Min(RazerHapticoreScrollProtocol.NativeGeneratorTableChunkPoints, size - offset);
            var request = RazerHapticoreScrollProtocol.CreateGetGeneratorTableRequest(MiddlewareTransactionId, profileId, 6, tableClass, (ushort)offset, count);
            var table = RazerHapticoreScrollProtocol.ParseGeneratorTable(await HapticExchangeAsync(connection, request, token), request);
            if (table.ProfileId != profileId || table.Stage != 6 || table.TableClass != tableClass ||
                table.PointOffset != offset || table.PointSize != count)
                throw new IOException("Hapticore returned an out-of-order or incomplete curve table.");
            points.AddRange(table.Points.Select(p => new HapticScrollPoint(p.X, p.Y)));
        }
        return points;
    }

    private async Task CheckHapticTableCapacityAsync(RazerHidDeviceConnection connection, HapticScrollCustomProfile custom, CancellationToken token)
    {
        var request = RazerHapticoreScrollProtocol.CreateGetGeneratorTableMaxPointCountRequest(MiddlewareTransactionId);
        var limits = RazerHapticoreScrollProtocol.ParseGeneratorTableMaxPointCount(await HapticExchangeAsync(connection, request, token), request);
        if (custom.Cw.Count > limits.CwTableMaxPoint || custom.Ccw.Count > limits.CcwTableMaxPoint || custom.Velocity.Count > limits.VelocityFactorTableMaxPoint)
            throw new InvalidDataException("The custom curve exceeds this device's reported table capacity.");
    }

    private async Task WriteHapticScrollProfileCoreAsync(RazerHidDeviceConnection connection, byte profileId,
        HapticScrollProfile profile, bool writeCustom, CancellationToken token)
    {
        if (writeCustom && profile.Custom is { } custom)
        {
            await HapticWriteAsync(connection, RazerHapticoreScrollProtocol.CreateSetGeneratorCustomMetaDataRawRequest(
                MiddlewareTransactionId, profileId, custom.Metadata), token);
            await HapticWriteAsync(connection, RazerHapticoreScrollProtocol.CreateSetGeneratorSettingRequest(
                MiddlewareTransactionId, profileId, 6, custom.Enabled, custom.AlternateCurrent,
                custom.ReductionAngleCw, custom.IndexCountAngleCw, custom.ReductionAngleCcw, custom.IndexCountAngleCcw,
                (ushort)custom.Cw.Count, (ushort)custom.Ccw.Count, (ushort)custom.Velocity.Count), token);
            foreach (var (tableClass, points) in new[] { (RazerHapticoreScrollTableClass.CwTable, custom.Cw),
                (RazerHapticoreScrollTableClass.CcwTable, custom.Ccw), (RazerHapticoreScrollTableClass.VelocityFactorTable, custom.Velocity) })
            {
                for (var offset = 0; offset < points.Count; offset += RazerHapticoreScrollProtocol.NativeGeneratorTableChunkPoints)
                {
                    var chunk = points.Skip(offset).Take(RazerHapticoreScrollProtocol.NativeGeneratorTableChunkPoints)
                        .Select(p => new RazerHapticoreScrollGeneratorPoint(p.X, p.Y)).ToArray();
                    await HapticWriteAsync(connection, RazerHapticoreScrollProtocol.CreateSetGeneratorTableRequest(
                        MiddlewareTransactionId, profileId, 6, tableClass, (ushort)offset, chunk), token);
                }
            }
        }
        await HapticWriteAsync(connection, RazerHapticoreScrollProtocol.CreateSetActiveStageRequest(
            MiddlewareTransactionId, profileId, profile.ActiveStage), token);
        await HapticWriteAsync(connection, RazerHapticoreScrollProtocol.CreateSetStageInformationRequest(
            MiddlewareTransactionId, profileId, profile.ActiveStage, (byte)profile.StageTypes.Count,
            profile.StageTypes.Select(type => (RazerHapticoreScrollStageType)type).ToArray()), token);
    }

    private Task<byte[]> HapticExchangeAsync(RazerHidDeviceConnection connection, byte[] request, CancellationToken token) =>
        ExecuteAsync(connection, new RazerHidRequest("razer_hapticore_curve", request), token);

    private async Task HapticWriteAsync(RazerHidDeviceConnection connection, byte[] request, CancellationToken token)
    {
        var response = await HapticExchangeAsync(connection, request, token);
        if (!RazerFeatureReport.IsSuccessfulResponse(request, response, 0))
            throw new IOException("Hapticore rejected a curve or stage write.");
    }
}
