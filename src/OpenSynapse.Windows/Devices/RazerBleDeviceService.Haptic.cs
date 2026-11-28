using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed partial class RazerBleDeviceService
{
    private readonly SemaphoreSlim _hapticGate = new(1, 1);
    public bool SupportsHapticStages => _productId == 0x00A9;

    public async Task<(byte Active, byte Max)> ReadHapticStageAsync(byte profileId = 1, CancellationToken cancellationToken = default)
    {
        await _hapticGate.WaitAsync(cancellationToken);
        try { return await ReadHapticStageCoreAsync(profileId, cancellationToken).ConfigureAwait(false); }
        finally { _hapticGate.Release(); }
    }

    private async Task<(byte Active, byte Max)> ReadHapticStageCoreAsync(byte profileId, CancellationToken token)
    {
        if (!SupportsHapticStages) throw new NotSupportedException("BLE Hapticore stages require Naga V2 Pro 00A9.");
        var active = await _session.SendAsync(t => RazerBleProtocol30.CreateGetHapticActiveStage(t, profileId), "get haptic active stage", token).ConfigureAwait(false);
        EnsureSuccess(active, "get haptic active stage");
        var max = await _session.SendAsync(t => RazerBleProtocol30.CreateGetHapticMaxStage(t, profileId), "get haptic max stage", token).ConfigureAwait(false);
        EnsureSuccess(max, "get haptic max stage");
        if (active.Payload.Count != 1 || max.Payload.Count != 1)
            throw new InvalidDataException("BLE Hapticore stage replies must carry one byte.");
        var value = RazerBleProtocol30.ReadSingleByte(active);
        var count = RazerBleProtocol30.ReadSingleByte(max);
        if (count is < 1 or > 6 || value < 1 || value > count)
            throw new InvalidDataException("BLE Hapticore returned an invalid active stage or stage count.");
        return (value, count);
    }

    public Task<byte> SetHapticStageAsync(byte stage, byte profileId = 1, CancellationToken cancellationToken = default) =>
        ApplyHapticStageAsync(stage, profileId, null, cancellationToken);

    public async Task<byte> ApplyHapticStageAsync(byte stage, byte profileId,
        Func<byte, CancellationToken, Task>? persist, CancellationToken cancellationToken = default)
    {
        _ = RazerBleProtocol30.CreateSetHapticActiveStage(0, profileId, stage);
        await _hapticGate.WaitAsync(cancellationToken);
        try
        {
            var previous = await ReadHapticStageCoreAsync(profileId, cancellationToken);
            if (stage > previous.Max) throw new ArgumentOutOfRangeException(nameof(stage), "The stage is not enabled on this profile.");
            try
            {
                await WriteHapticStageCoreAsync(stage, profileId, cancellationToken);
                var applied = await ReadHapticStageCoreAsync(profileId, cancellationToken);
                if (applied.Active != stage || applied.Max != previous.Max)
                    throw new InvalidOperationException("The device kept different Hapticore settings.");
                if (persist is not null) await persist(applied.Active, cancellationToken);
                return applied.Active;
            }
            catch (Exception error)
            {
                try
                {
                    await WriteHapticStageCoreAsync(previous.Active, profileId, CancellationToken.None).ConfigureAwait(false);
                    if (await ReadHapticStageCoreAsync(profileId, CancellationToken.None).ConfigureAwait(false) != previous)
                        throw new IOException("Hapticore restore readback did not match the previous stage.");
                }
                catch (Exception rollback)
                {
                    throw new IOException($"Hapticore apply failed: {error.Message} Restore also failed: {rollback.Message}", new AggregateException(error, rollback));
                }
                if (error is OperationCanceledException) throw;
                throw new InvalidOperationException($"{error.Message} The previous Hapticore stage was restored.", error);
            }
        }
        finally { _hapticGate.Release(); }
    }

    private async Task WriteHapticStageCoreAsync(byte stage, byte profileId, CancellationToken token)
    {
        var response = await _session.SendAsync(t => RazerBleProtocol30.CreateSetHapticActiveStage(t, profileId, stage), "set haptic active stage", token).ConfigureAwait(false);
        EnsureSuccess(response, "set haptic active stage");
    }
}
