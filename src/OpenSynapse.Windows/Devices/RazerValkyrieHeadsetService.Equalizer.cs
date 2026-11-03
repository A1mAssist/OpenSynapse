using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed partial class RazerValkyrieHeadsetService
{
    private readonly SemaphoreSlim _eqGate = new(1, 1);

    public Task SetEqPresetAsync(byte preset, CancellationToken cancellationToken = default) =>
        ExecuteEqAsync(token => SetEqPresetCoreAsync(preset, token), cancellationToken);

    public Task SetEqBandsAsync(IReadOnlyList<byte> bands, CancellationToken cancellationToken = default) =>
        ExecuteEqAsync(token => SetEqBandsCoreAsync(bands, token), cancellationToken);

    public Task SetCustomEqAsync(byte preset, IReadOnlyList<byte> bands, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bands);
        var requested = bands.ToArray();
        if (requested.Length != 10) throw new ArgumentException("Invalid EQ band count.", nameof(bands));
        return ExecuteEqAsync(async token =>
        {
            var previousPreset = RazerValkyrieHeadsetProtocol.ReadEqPreset(await QueryAsync(
                RazerValkyrieHeadsetProtocol.CreateGetEqPreset(), RazerValkyrieHeadsetProtocol.EqPresetHeader,
                "snapshot EQ preset", token).ConfigureAwait(false));
            var previousBands = RazerValkyrieHeadsetProtocol.ReadEqBands(await QueryAsync(
                RazerValkyrieHeadsetProtocol.CreateGetEqBands(_eqBandsWithProfile),
                _eqBandsWithProfile ? RazerValkyrieHeadsetProtocol.EqBandsWithProfileHeader : RazerValkyrieHeadsetProtocol.EqBandsBareHeader,
                "snapshot EQ bands", token).ConfigureAwait(false), _eqBandsWithProfile);
            try
            {
                await SetEqPresetCoreAsync(preset, token).ConfigureAwait(false);
                await SetEqBandsCoreAsync(requested, token).ConfigureAwait(false);
            }
            catch (Exception original)
            {
                // Both fields belong to this operation. A failed restoration must not
                // prevent the other field from being restored or hide the original error.
                var bandsError = await RestoreEqFieldAsync(() => ReplayEqBandsAsync(previousBands), "bands").ConfigureAwait(false);
                var presetError = await RestoreEqFieldAsync(() => ReplayEqPresetAsync(previousPreset), "preset").ConfigureAwait(false);
                if (bandsError is not null || presetError is not null)
                    throw new InvalidOperationException($"Custom EQ failed: {original.Message}. Previous EQ could not be fully restored: {bandsError} {presetError}", original);
                throw;
            }
        }, cancellationToken);
    }

    private async Task ExecuteEqAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken)
    {
        await _eqGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { await operation(cancellationToken).ConfigureAwait(false); }
        finally { _eqGate.Release(); }
    }

    private async Task<string?> RestoreEqFieldAsync(Func<Task> restore, string field)
    {
        try { await restore().ConfigureAwait(false); return null; }
        catch (Exception exception) when (IsExpected(exception))
        {
            _diagnostic?.Invoke($"restore custom EQ {field} failed: {exception.Message}");
            return $"{field}: {exception.Message}";
        }
    }

    private async Task ReplayEqPresetAsync(byte preset)
    {
        await QueryAsync(RazerValkyrieHeadsetProtocol.CreateSetEqPreset(preset), RazerValkyrieHeadsetProtocol.SetEqPresetHeader, "restore EQ preset", CancellationToken.None).ConfigureAwait(false);
        var actual = RazerValkyrieHeadsetProtocol.ReadEqPreset(await QueryAsync(RazerValkyrieHeadsetProtocol.CreateGetEqPreset(), RazerValkyrieHeadsetProtocol.EqPresetHeader, "verify restored EQ preset", CancellationToken.None).ConfigureAwait(false));
        if (actual != preset) throw new InvalidOperationException("Restored EQ preset did not match.");
    }
    private async Task ReplayEqBandsAsync(byte[] bands)
    {
        await QueryAsync(RazerValkyrieHeadsetProtocol.CreateSetEqBands(bands, _eqBandsWithProfile), RazerValkyrieHeadsetProtocol.SetEqBandsHeader, "restore EQ bands", CancellationToken.None).ConfigureAwait(false);
        var actual = RazerValkyrieHeadsetProtocol.ReadEqBands(await QueryAsync(RazerValkyrieHeadsetProtocol.CreateGetEqBands(_eqBandsWithProfile), _eqBandsWithProfile ? RazerValkyrieHeadsetProtocol.EqBandsWithProfileHeader : RazerValkyrieHeadsetProtocol.EqBandsBareHeader, "verify restored EQ bands", CancellationToken.None).ConfigureAwait(false), _eqBandsWithProfile);
        if (!actual.SequenceEqual(bands)) throw new InvalidOperationException("Restored EQ bands did not match.");
    }
}
