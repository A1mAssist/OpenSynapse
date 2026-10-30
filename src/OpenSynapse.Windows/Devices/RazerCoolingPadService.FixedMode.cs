using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed partial class RazerCoolingPadService
{
    /// <summary>Reads 0D:90; mode 0/1/2 are fixed, 3/4/5 are smart curves, 6 is off.</summary>
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

    public Task<RazerCoolingPadCoolingMode> SetFixedModeAsync(
        RazerCoolingPadFixedMode mode,
        CancellationToken cancellationToken = default) =>
        SetCoolingModeCoreAsync(
            (byte)mode,
            mode == RazerCoolingPadFixedMode.Off ? (byte)0 : (byte)1,
            RazerCoolingPadProtocol.UnitsFor(mode),
            $"fixed mode {mode}",
            cancellationToken);

    public Task<RazerCoolingPadCoolingMode> SetSmartModeAsync(
        RazerCoolingPadSmartMode mode,
        CancellationToken cancellationToken = default) =>
        SetCoolingModeCoreAsync((byte)mode, 0, 0, $"smart mode {mode}", cancellationToken);

    private async Task<RazerCoolingPadCoolingMode> SetCoolingModeCoreAsync(
        byte modeId,
        byte source,
        byte speedUnits,
        string label,
        CancellationToken cancellationToken)
    {
        // Validate the frame before reading the device.
        _ = RazerCoolingPadProtocol.CreateSetCoolingModeRequest(modeId, source, speedUnits);
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
                    $"cooling pad {label}: original state unavailable ({exception.Message})");
            }

            await QueryAsync(
                RazerCoolingPadProtocol.CreateSetCoolingModeRequest(modeId, source, speedUnits),
                label,
                cancellationToken).ConfigureAwait(false);
            var actual = await ReadCoolingModeAsync(cancellationToken).ConfigureAwait(false);
            if (actual.ModeId != modeId || actual.SpeedUnits != speedUnits)
            {
                throw new InvalidOperationException(
                    $"Cooling pad {label} readback mismatch: wrote mode {modeId}/{speedUnits} units, " +
                    $"read mode {actual.ModeId}/{actual.SpeedUnits} units.");
            }

            _diagnostic?.Invoke($"cooling pad {label} set (readback {actual.ModeId}/{actual.Rpm} RPM)");
            return actual;
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            if (original is { } restore &&
                (restore.ModeId != modeId || restore.Source != source || restore.SpeedUnits != speedUnits))
            {
                try
                {
                    await QueryAsync(
                        RazerCoolingPadProtocol.CreateSetCoolingModeRequest(
                            restore.ModeId, restore.Source, restore.SpeedUnits),
                        "restore cooling mode",
                        CancellationToken.None).ConfigureAwait(false);
                    _diagnostic?.Invoke(
                        $"cooling pad {label} restored mode {restore.ModeId}/{restore.SpeedUnits} units after a failed update");
                }
                catch (Exception restoreException) when (IsExpected(restoreException))
                {
                    _diagnostic?.Invoke(
                        $"cooling pad {label} restore failed: {restoreException.Message}");
                }
            }

            throw new InvalidOperationException(
                $"Cooling pad {label} update failed: {exception.Message}", exception);
        }
    }
}
