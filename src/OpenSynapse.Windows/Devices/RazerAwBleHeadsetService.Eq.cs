using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed partial class RazerAwBleHeadsetService
{
    public Task<IReadOnlyList<sbyte>> ReadMicCustomEqBandsAsync(
        CancellationToken cancellationToken = default) =>
        ReadMicCustomEqBandsCoreAsync(ProbeTimeout, cancellationToken);

    public async Task<IReadOnlyList<sbyte>> SetMicCustomEqAsync(
        IReadOnlyList<sbyte> levels,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        var previousPreset = await ReadMicEqPresetAsync(cancellationToken).ConfigureAwait(false);
        if (!enabled)
        {
            await SetMicEqPresetAsync(RazerAwBleProtocol.DefaultEqPreset, cancellationToken)
                .ConfigureAwait(false);
            return levels;
        }

        await SetMicEqPresetAsync(RazerAwBleProtocol.CustomEqPreset, cancellationToken)
            .ConfigureAwait(false);
        try
        {
            return await SetMicCustomEqBandsAsync(levels, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await TryRestoreMicEqPresetAsync(previousPreset).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<IReadOnlyList<sbyte>> SetMicCustomEqBandsAsync(
        IReadOnlyList<sbyte> levels,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(levels);
        if (levels.Count != RazerAwBleProtocol.CustomEqBandCount)
        {
            throw new ArgumentException(
                $"Exactly {RazerAwBleProtocol.CustomEqBandCount} EQ bands are required.",
                nameof(levels));
        }

        var previous = await ReadMicCustomEqBandsCoreAsync(_timeout, cancellationToken)
            .ConfigureAwait(false);
        if (previous.SequenceEqual(levels))
        {
            return previous;
        }

        try
        {
            var acknowledgement = await _transport.ExchangeAsync(
                RazerAwBleProtocol.CreateMicEqBandsRequest(levels),
                _timeout,
                cancellationToken).ConfigureAwait(false);
            if (acknowledgement is null)
            {
                throw new InvalidOperationException(
                    "The headset did not acknowledge the custom mic EQ write.");
            }

            if (!RazerAwBleProtocol.IsReplyForCommand(
                    acknowledgement,
                    RazerAwBleProtocol.MicCustomEqBandsSetCommand))
            {
                throw new InvalidDataException(
                    "The headset returned an invalid custom mic EQ acknowledgement.");
            }

            var actual = await ReadMicCustomEqBandsCoreAsync(_timeout, cancellationToken)
                .ConfigureAwait(false);
            if (!actual.SequenceEqual(levels))
            {
                throw new InvalidOperationException(
                    "Custom mic EQ readback did not match the requested values.");
            }

            return actual;
        }
        catch
        {
            await TryRestoreMicEqBandsAsync(previous).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// The bundle switches the speaker to the custom preset (255) before writing
    /// the ten custom bands; disabling it switches back to the default preset.
    /// </summary>
    public async Task<IReadOnlyList<sbyte>> SetCustomEqAsync(
        IReadOnlyList<sbyte> levels,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        var previousPreset = await ReadEqPresetAsync(cancellationToken).ConfigureAwait(false);
        if (!enabled)
        {
            await SetEqPresetAsync(RazerAwBleProtocol.DefaultEqPreset, cancellationToken)
                .ConfigureAwait(false);
            return levels;
        }

        await SetEqPresetAsync(RazerAwBleProtocol.CustomEqPreset, cancellationToken)
            .ConfigureAwait(false);
        try
        {
            return await SetCustomEqBandsAsync(levels, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await TryRestoreEqPresetAsync(previousPreset).ConfigureAwait(false);
            throw;
        }
    }

    private async Task<IReadOnlyList<sbyte>> ReadMicCustomEqBandsCoreAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var response = await ReadFrameAsync(
            RazerAwBleProtocol.MicCustomEqBandsGetCommand,
            "custom mic EQ bands",
            cancellationToken,
            timeout).ConfigureAwait(false);
        if (!RazerAwBleProtocol.TryParseMicEqBands(response, out var levels))
        {
            throw new InvalidDataException(
                "The headset returned an invalid custom mic EQ reply.");
        }

        return levels;
    }

    private async Task TryRestoreMicEqBandsAsync(IReadOnlyList<sbyte> levels)
    {
        try
        {
            var acknowledgement = await _transport.ExchangeAsync(
                RazerAwBleProtocol.CreateMicEqBandsRequest(levels),
                _timeout,
                CancellationToken.None).ConfigureAwait(false);
            if (acknowledgement is null)
            {
                _diagnostic?.Invoke("aw ble restore custom mic EQ bands: no acknowledgement");
            }
            else if (!RazerAwBleProtocol.IsReplyForCommand(
                         acknowledgement,
                         RazerAwBleProtocol.MicCustomEqBandsSetCommand))
            {
                _diagnostic?.Invoke("aw ble restore custom mic EQ bands: invalid acknowledgement");
            }
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            _diagnostic?.Invoke($"aw ble restore custom mic EQ bands failed: {exception.Message}");
        }
    }

    private async Task TryRestoreEqPresetAsync(byte preset)
    {
        try
        {
            await SetEqPresetAsync(preset, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            _diagnostic?.Invoke($"aw ble restore EQ preset failed: {exception.Message}");
        }
    }

    private async Task TryRestoreMicEqPresetAsync(byte preset)
    {
        try
        {
            await SetMicEqPresetAsync(preset, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            _diagnostic?.Invoke($"aw ble restore mic EQ preset failed: {exception.Message}");
        }
    }
}
