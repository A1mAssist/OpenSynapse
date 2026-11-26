using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed partial class RazerMxicHeadsetService
{
    public async Task<RazerHeadsetEqualizerState> ReadEqualizerAsync(
        int bandCount, CancellationToken cancellationToken = default)
    {
        if (bandCount is not (0 or 10)) throw new ArgumentOutOfRangeException(nameof(bandCount));
        if (_dialect == MxicDialect.Protocol40)
        {
            if (!RazerProtocol40HeadsetProtocol.SupportsEq(_productId))
                throw new NotSupportedException("This protocol40 headset has no EQ reader.");
            await _transport.SendAsync(RazerProtocol40HeadsetProtocol.CreateConnect(), cancellationToken)
                .ConfigureAwait(false);
        }
        var preset = ReadEqPreset(await QueryReportAsync(
            CreateEqPresetRead(), EqPresetHeader, "EQ preset", cancellationToken).ConfigureAwait(false));
        var bands = bandCount == 0 ? null : await QueryReportAsync(
            CreateEqBandsRead(), EqBandsHeader, "EQ bands", cancellationToken).ConfigureAwait(false);
        return new(preset, bands is null ? null : _dialect == MxicDialect.Protocol40
            ? RazerProtocol40HeadsetProtocol.ReadEqBands(bands)
            : RazerMxicHeadsetProtocol.ReadEqBands(bands));
    }

    private readonly SemaphoreSlim _eqGate = new(1, 1);

    public Task SetEqPresetAsync(byte preset, CancellationToken cancellationToken = default) =>
        ExecuteEqAsync(token => SetEqPresetCoreAsync(preset, token), cancellationToken);

    public Task SetEqBandsAsync(IReadOnlyList<byte> bands, CancellationToken cancellationToken = default) =>
        ExecuteEqAsync(token => SetEqBandsCoreAsync(bands, token), cancellationToken);

    public Task SetCustomEqAsync(byte preset, IReadOnlyList<byte> bands, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bands);
        if (_dialect == MxicDialect.ValkyrieT3 && preset > 3) throw new ArgumentOutOfRangeException(nameof(preset));
        var requested = bands.ToArray();
        ValidateEqBands(requested);
        return ExecuteEqAsync(async token =>
        {
            if (_dialect == MxicDialect.Protocol40)
                throw new InvalidOperationException("The protocol40 bundle does not expose EQ writes.");
            var previousPreset = ReadEqPreset(await QueryReportAsync(
                CreateEqPresetRead(), EqPresetHeader, "snapshot EQ preset", token).ConfigureAwait(false));
            var previousBands = RazerMxicHeadsetProtocol.ReadEqBands(await QueryReportAsync(
                CreateEqBandsRead(), EqBandsHeader, "snapshot EQ bands", token).ConfigureAwait(false));
            try
            {
                await SetEqPresetCoreAsync(preset, token).ConfigureAwait(false);
                if (_dialect == MxicDialect.Joliet)
                {
                    // Select Custom before storing its curve; Jolie can echo stale bands after a write.
                    await WriteReportAsync(
                        RazerMxicHeadsetProtocol.CreateSetEqBandsJoliet(preset, requested),
                        SetEqBandsHeader,
                        "custom EQ",
                        token).ConfigureAwait(false);
                }
                else
                {
                    await SetEqBandsCoreAsync(requested, token).ConfigureAwait(false);
                }
            }
            catch (Exception original)
            {
                // Both fields belong to this operation. A failed restoration must not
                // prevent the other field from being restored or hide the original error.
                var bandsError = await RestoreEqFieldAsync(() => ReplayEqBandsAsync(previousPreset, previousBands), "bands").ConfigureAwait(false);
                var presetError = await RestoreEqFieldAsync(() => ReplayEqPresetAsync(previousPreset), "preset").ConfigureAwait(false);
                if (bandsError is not null || presetError is not null)
                    throw new InvalidOperationException($"Custom EQ failed: {original.Message}. Previous EQ could not be fully restored: {bandsError} {presetError}", original);
                throw;
            }
        }, cancellationToken);
    }

    private void ValidateEqBands(IReadOnlyList<byte> bands)
    {
        if (bands.Count != RazerMxicHeadsetProtocol.TenBandCount)
            throw new ArgumentException("Equalizer data must contain 10 bands.", nameof(bands));
        if (_dialect is MxicDialect.ValkyrieT3 or MxicDialect.Joliet &&
            bands.Any(value => value > 6 && value is not (>= 129 and <= 134)))
            throw new ArgumentOutOfRangeException(nameof(bands), $"{_dialect} bands must encode -6..6 dB.");
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
        // Restoration uses CancellationToken.None; a transport cancellation is a field failure.
        catch (Exception exception) when (IsExpected(exception) || exception is TimeoutException or OperationCanceledException)
        {
            _diagnostic?.Invoke($"restore custom EQ {field} failed: {exception.Message}");
            return $"{field}: {exception.Message}";
        }
    }

    private async Task ReplayEqPresetAsync(byte preset)
    {
        await WriteReportAsync(CreateSetEqPreset(preset), SetEqPresetHeader, "restore EQ preset", CancellationToken.None).ConfigureAwait(false);
        var actual = ReadEqPreset(await QueryReportAsync(CreateEqPresetRead(), EqPresetHeader, "verify restored EQ preset", CancellationToken.None).ConfigureAwait(false));
        if (actual != preset) throw new InvalidOperationException("Restored EQ preset did not match.");
    }
    private async Task ReplayEqBandsAsync(byte preset, byte[] bands)
    {
        var request = _dialect == MxicDialect.Joliet
            ? RazerMxicHeadsetProtocol.CreateSetEqBandsJoliet(preset, bands)
            : CreateSetEqBands(bands);
        await WriteReportAsync(request, SetEqBandsHeader, "restore EQ bands", CancellationToken.None).ConfigureAwait(false);
        if (_dialect == MxicDialect.Joliet) return;
        var actual = RazerMxicHeadsetProtocol.ReadEqBands(await QueryReportAsync(CreateEqBandsRead(), EqBandsHeader, "verify restored EQ bands", CancellationToken.None).ConfigureAwait(false));
        if (!actual.SequenceEqual(bands)) throw new InvalidOperationException("Restored EQ bands did not match.");
    }
}
