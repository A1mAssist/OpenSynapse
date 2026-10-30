using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed partial class RazerCoolingPadService
{
    /// <summary>Reads 0D:90; mode 0/1/2 are the fixed presets, 3/4/5 are the smart curves.</summary>
    public async Task<RazerCoolingPadCoolingMode> ReadCoolingModeAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await QueryAsync(
                RazerCoolingPadProtocol.CreateGetCoolingModeRequest(),
                "read cooling mode",
                cancellationToken).ConfigureAwait(false);
            return RazerCoolingPadProtocol.ParseCoolingMode(response);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            throw new InvalidOperationException(
                $"Cooling pad cooling-mode read failed: {exception.Message}", exception);
        }
    }

    public async Task<RazerCoolingPadCoolingMode> SetFixedModeAsync(
        RazerCoolingPadFixedMode mode,
        CancellationToken cancellationToken = default)
    {
        // Validate the preset before touching the device.
        _ = RazerCoolingPadProtocol.CreateSetCoolingModeRequest(mode);
        RazerCoolingPadCoolingMode? original = null;
        try
        {
            try
            {
                original = await ReadCoolingModeAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (IsExpected(exception))
            {
                _diagnostic?.Invoke(
                    $"cooling pad fixed mode: original state unavailable ({exception.Message})");
            }

            await QueryAsync(
                RazerCoolingPadProtocol.CreateSetCoolingModeRequest(mode),
                "set fixed mode",
                cancellationToken).ConfigureAwait(false);
            var actual = await ReadCoolingModeAsync(cancellationToken).ConfigureAwait(false);
            if (actual.ModeId != (byte)mode ||
                actual.SpeedUnits != RazerCoolingPadProtocol.UnitsFor(mode))
            {
                throw new InvalidOperationException(
                    $"Cooling pad fixed-mode readback mismatch: wrote {mode}, " +
                    $"read mode {actual.ModeId}/{actual.SpeedUnits} units.");
            }

            _diagnostic?.Invoke($"cooling pad fixed mode set to {mode} ({actual.Rpm} RPM)");
            return actual;
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            // Only a fixed preset can be restored; the smart curves and off have no
            // 0D:10 payload this app has evidence for.
            if (original is { ModeId: <= 2 })
            {
                try
                {
                    await QueryAsync(
                        RazerCoolingPadProtocol.CreateSetCoolingModeRequest(
                            (RazerCoolingPadFixedMode)original.Value.ModeId),
                        "restore fixed mode",
                        CancellationToken.None).ConfigureAwait(false);
                    _diagnostic?.Invoke(
                        $"cooling pad fixed mode restored to {(RazerCoolingPadFixedMode)original.Value.ModeId} after a failed update");
                }
                catch (Exception restoreException) when (IsExpected(restoreException))
                {
                    _diagnostic?.Invoke(
                        $"cooling pad fixed mode restore failed: {restoreException.Message}");
                }
            }

            throw new InvalidOperationException(
                $"Cooling pad fixed-mode update failed: {exception.Message}", exception);
        }
    }
}
