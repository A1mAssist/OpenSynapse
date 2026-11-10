using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed partial class RazerHeadsetAudioService
{
    private readonly SemaphoreSlim _eqGate = new(1, 1);

    public Task SetEqPresetAsync(byte preset, CancellationToken cancellationToken = default) =>
        ExecuteEqAsync(token => SetEqPresetCoreAsync(preset, token), cancellationToken);

    public Task SetEqBandsAsync(IReadOnlyList<byte> bands, CancellationToken cancellationToken = default) =>
        ExecuteEqAsync(token => SetEqBandsCoreAsync(bands, token), cancellationToken);

    public Task SetCustomEqAsync(byte preset, IReadOnlyList<byte> bands, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bands);
        if (preset == 0xFF) throw new ArgumentOutOfRangeException(nameof(preset));
        var requested = bands.ToArray();
        if (requested.Length is not (8 or 10)) throw new ArgumentException("Invalid EQ band count.", nameof(bands));
        return ExecuteEqAsync(async token =>
        {
            if (_protocol25Transport is not null)
            {
                RequireProtocol25Support(
                    RazerProtocol25FeatureProtocol.SupportsHidEqualizer(_productId), "custom EQ");
                await SetProtocol25CustomEqAsync(preset, requested, token).ConfigureAwait(false);
                return;
            }

            var previousPreset = RazerHeadsetAudioProtocol.ReadEqPreset(await QueryAsync(
                txn => RazerHeadsetAudioProtocol.GetEqPreset(txn), "snapshot EQ preset", token).ConfigureAwait(false));
            var previousBands = await ReadEqBandsRequiredAsync(requested.Length == 10, token).ConfigureAwait(false);
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

    private Task SetEqPresetCoreAsync(byte preset, CancellationToken cancellationToken = default)
    {
        if (preset == 0xFF)
        {
            throw new ArgumentOutOfRangeException(nameof(preset));
        }

        if (_protocol25Transport is not null)
        {
            RequireProtocol25Support(
                RazerProtocol25FeatureProtocol.SupportsHidEqualizer(_productId), "EQ preset");
            return SetProtocol25EqPresetAsync(preset, cancellationToken);
        }

        return WriteAsync(
            txn => RazerHeadsetAudioProtocol.GetEqPreset(txn),
            RazerHeadsetAudioProtocol.ReadEqPreset,
            preset,
            (value, txn) => RazerHeadsetAudioProtocol.SetEqPreset(txn, value),
            value => value == preset,
            "EQ preset",
            cancellationToken);
    }

    private async Task SetEqBandsCoreAsync(
        IReadOnlyList<byte> bands,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bands);
        var tenBand = bands.Count == RazerHeadsetAudioProtocol.TenBandCount;
        if (!tenBand && bands.Count != RazerHeadsetAudioProtocol.EightBandCount)
        {
            throw new ArgumentException("Equalizer data must contain 10 or 8 bands.", nameof(bands));
        }

        if (_protocol25Transport is not null)
        {
            RequireProtocol25Support(
                RazerProtocol25FeatureProtocol.SupportsHidEqualizer(_productId), "EQ bands");
            await SetProtocol25EqBandsAsync(bands, cancellationToken).ConfigureAwait(false);
            return;
        }

        var previous = await ReadEqBandsRequiredAsync(tenBand, cancellationToken).ConfigureAwait(false);
        if (previous.SequenceEqual(bands))
        {
            return;
        }

        try
        {
            await QueryAsync(
                txn => RazerHeadsetAudioProtocol.SetEqBands(txn, bands.ToArray()),
                "set EQ bands",
                cancellationToken).ConfigureAwait(false);
            var actual = await ReadEqBandsRequiredAsync(tenBand, cancellationToken).ConfigureAwait(false);
            if (!actual.SequenceEqual(bands))
            {
                throw new InvalidOperationException("EQ band readback did not match the requested values.");
            }
        }
        catch
        {
            await TryRestoreAsync(
                txn => RazerHeadsetAudioProtocol.SetEqBands(txn, previous.ToArray()),
                "restore EQ bands").ConfigureAwait(false);
            throw;
        }
    }


    private async Task ReplayEqPresetAsync(byte preset)
    {
        await QueryAsync(t => RazerHeadsetAudioProtocol.SetEqPreset(t, preset), "restore EQ preset", CancellationToken.None).ConfigureAwait(false);
        var actual = RazerHeadsetAudioProtocol.ReadEqPreset(await QueryAsync(t => RazerHeadsetAudioProtocol.GetEqPreset(t), "verify restored EQ preset", CancellationToken.None).ConfigureAwait(false));
        if (actual != preset) throw new InvalidOperationException("Restored EQ preset did not match.");
    }
    private async Task ReplayEqBandsAsync(byte[] bands)
    {
        await QueryAsync(t => RazerHeadsetAudioProtocol.SetEqBands(t, bands), "restore EQ bands", CancellationToken.None).ConfigureAwait(false);
        var actual = await ReadEqBandsRequiredAsync(bands.Length == 10, CancellationToken.None).ConfigureAwait(false);
        if (!actual.SequenceEqual(bands)) throw new InvalidOperationException("Restored EQ bands did not match.");
    }
}
