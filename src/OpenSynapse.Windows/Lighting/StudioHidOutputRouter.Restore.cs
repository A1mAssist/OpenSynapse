using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Lighting;

public sealed partial class StudioHidOutputRouter
{
    private async Task<DeviceBaseline?> CaptureBaselineAsync(
        RazerHidDeviceConnection connection,
        CancellationToken cancellationToken)
    {
        // Read every zone that advertises a classic read. No catalog zone also
        // exposes a writable effect or color today, so real devices keep their last frame.
        var baseline = new List<ZoneBaseline>();
        var storage = connection.Definition.DefaultStorage;
        foreach (var (zone, capabilities) in connection.LightingZones.OrderBy(pair => pair.Key))
        {
            // Capture only a state this same zone can write back. A read capability on
            // one zone and a write capability on another is not a restore path.
            var canRestoreEffect = capabilities.CanReadEffect && capabilities.CanWriteEffect;
            var canRestoreColor = capabilities.CanReadColor && capabilities.CanWriteColor;
            if (!canRestoreEffect && !canRestoreColor)
            {
                continue;
            }

            var effect = canRestoreEffect
                ? await ReadEffectAsync(connection, storage, zone, cancellationToken).ConfigureAwait(false)
                : null;
            var color = canRestoreColor
                ? await ReadColorAsync(connection, storage, zone, cancellationToken).ConfigureAwait(false)
                : null;
            if (effect is not null || color is not null)
            {
                baseline.Add(new ZoneBaseline(zone, effect, color));
            }
        }

        return baseline.Count == 0 ? null : new DeviceBaseline(baseline);
    }

    private async Task<RazerHidLightingEffect?> ReadEffectAsync(
        RazerHidDeviceConnection connection,
        RazerHidStorage storage,
        RazerHidLedZone zone,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _service.GetLedEffectAsync(connection, storage, zone, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (IsDeviceFailure(exception))
        {
            // Baseline capture is best effort; an unreadable effect must not stop frames.
            return null;
        }
    }

    private async Task<RazerHidColor?> ReadColorAsync(
        RazerHidDeviceConnection connection,
        RazerHidStorage storage,
        RazerHidLedZone zone,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _service.GetLedColorAsync(connection, storage, zone, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (IsDeviceFailure(exception))
        {
            return null;
        }
    }

    private async Task<IReadOnlyList<Exception>> RestoreAsync(
        RazerHidDeviceConnection connection,
        DeviceBaseline baseline,
        CancellationToken cancellationToken)
    {
        var failures = new List<Exception>();
        var storage = connection.Definition.DefaultStorage;
        foreach (var saved in baseline.Zones)
        {
            if (!connection.LightingZones.TryGetValue(saved.Zone, out var capabilities))
            {
                failures.Add(new NotSupportedException(
                    $"The saved lighting zone {saved.Zone} is no longer available."));
                continue;
            }

            var restored = false;
            if (saved.Effect is { } effect &&
                capabilities.CanWriteEffect &&
                capabilities.LightingEffects.Contains(effect) &&
                CanRestoreEffect(effect, saved.Color))
            {
                try
                {
                    await _service.SetLightingAsync(connection, new RazerHidLightingSettings(
                        effect,
                        Primary: saved.Color ?? default,
                        Storage: storage,
                        Zone: saved.Zone), cancellationToken).ConfigureAwait(false);
                    restored = true;
                }
                catch (Exception exception) when (IsDeviceFailure(exception))
                {
                    failures.Add(exception);
                }
            }

            if (!restored &&
                saved.Color is { } color &&
                capabilities.CanWriteColor)
            {
                try
                {
                    await _service.SetLedColorAsync(connection, storage, saved.Zone, color, cancellationToken)
                        .ConfigureAwait(false);
                    restored = true;
                }
                catch (Exception exception) when (IsDeviceFailure(exception))
                {
                    failures.Add(exception);
                }
            }

            if (!restored)
            {
                failures.Add(new NotSupportedException(
                    $"The saved lighting state for zone {saved.Zone} cannot be written back."));
            }
        }

        return failures;
    }

    private void ReportRestoreFailure(
        RazerHidDeviceConnection connection,
        IReadOnlyList<Exception> restoreFailures,
        List<string> failures)
    {
        var error = restoreFailures.Count == 1
            ? restoreFailures[0]
            : new AggregateException(restoreFailures);
        RestoreFailed?.Invoke(connection.Definition.DisplayName, error);
        failures.Add($"{connection.Definition.DisplayName}: {error.Message}");
    }

    private async Task<bool> ApplyOfficialExitEffectAsync(
        RazerHidDeviceConnection connection,
        CancellationToken cancellationToken)
    {
        var storage = connection.Definition.DefaultStorage;
        var zones = connection.LightingZones
            .Where(pair => pair.Value.LightingEffects.Contains(RazerHidLightingEffect.Spectrum))
            .Select(pair => pair.Key)
            .OrderBy(zone => zone == connection.Definition.DefaultLedZone ? 0 : 1)
            .ThenBy(zone => zone);
        foreach (var zone in zones)
        {
            try
            {
                await _service.SetLightingAsync(connection, new RazerHidLightingSettings(
                    RazerHidLightingEffect.Spectrum,
                    Storage: storage,
                    Zone: zone), cancellationToken).ConfigureAwait(false);
                return true;
            }
            catch (NotSupportedException)
            {
                // A device-wide builder may still accept the effect; try that next.
            }
        }

        try
        {
            await _service.SetLightingAsync(connection, new RazerHidLightingSettings(
                RazerHidLightingEffect.Spectrum,
                Storage: storage), cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    private static bool CanRestoreEffect(
        RazerHidLightingEffect effect,
        RazerHidColor? color) =>
        effect is RazerHidLightingEffect.Spectrum or RazerHidLightingEffect.Wave or
            RazerHidLightingEffect.Reactive or RazerHidLightingEffect.Off ||
        color is not null;

    private static bool IsDeviceFailure(Exception exception) =>
        exception switch
        {
            ObjectDisposedException or OperationCanceledException => false,
            IOException or UnauthorizedAccessException or TimeoutException or
                InvalidOperationException or System.ComponentModel.Win32Exception or
                System.Runtime.InteropServices.COMException or
                InvalidDataException or NotSupportedException => true,
            _ => false,
        };

    private readonly record struct ZoneBaseline(
        RazerHidLedZone Zone,
        RazerHidLightingEffect? Effect,
        RazerHidColor? Color);

    private readonly record struct DeviceBaseline(IReadOnlyList<ZoneBaseline> Zones);
}
