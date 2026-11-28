using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

/// <summary>
/// The lighting a device had before a custom-frame session. Only a zone that can
/// write back what it read is captured; a read-only zone has no restore path.
/// </summary>
internal sealed class RazerHidLightingBaseline
{
    private readonly IReadOnlyList<ZoneBaseline> _zones;

    private RazerHidLightingBaseline(IReadOnlyList<ZoneBaseline> zones) => _zones = zones;

    internal bool CanRestoreEffectFor(RazerHidLedZone zone) => _zones.Any(saved =>
        saved.Zone == zone && saved.Effect is { } effect && CanRestoreEffect(effect, saved.Color));

    internal static async Task<RazerHidLightingBaseline?> CaptureAsync(
        RazerHidDeviceService service,
        RazerHidDeviceConnection connection,
        CancellationToken cancellationToken)
    {
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

            RazerHidLightingEffect? effect = null;
            if (canRestoreEffect)
            {
                try
                {
                    effect = await service.GetLedEffectAsync(connection, storage, zone, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception exception) when (IsDeviceFailure(exception))
                {
                    // Baseline capture is best effort; an unreadable effect must not stop frames.
                }
            }

            RazerHidColor? color = null;
            if (canRestoreColor)
            {
                try
                {
                    color = await service.GetLedColorAsync(connection, storage, zone, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception exception) when (IsDeviceFailure(exception))
                {
                }
            }

            if (effect is not null || color is not null)
            {
                baseline.Add(new ZoneBaseline(zone, effect, color));
            }
        }

        return baseline.Count == 0 ? null : new RazerHidLightingBaseline(baseline);
    }

    internal async Task<IReadOnlyList<Exception>> RestoreAsync(
        RazerHidDeviceService service,
        RazerHidDeviceConnection connection,
        CancellationToken cancellationToken)
    {
        var failures = new List<Exception>();
        var storage = connection.Definition.DefaultStorage;
        foreach (var saved in _zones)
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
                    await service.SetLightingAsync(connection, new RazerHidLightingSettings(
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
                    await service.SetLedColorAsync(connection, storage, saved.Zone, color, cancellationToken)
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

    /// <summary>
    /// No readable baseline: the extracted manifests document Spectrum as the
    /// Studio exit effect, so use that instead of leaving the device on Custom.
    /// </summary>
    internal static async Task<bool> ApplyExitEffectAsync(
        RazerHidDeviceService service,
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
                await service.SetLightingAsync(connection, new RazerHidLightingSettings(
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
            await service.SetLightingAsync(connection, new RazerHidLightingSettings(
                RazerHidLightingEffect.Spectrum,
                Storage: storage), cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    internal static bool IsDeviceFailure(Exception exception) =>
        exception switch
        {
            ObjectDisposedException or OperationCanceledException => false,
            IOException or UnauthorizedAccessException or TimeoutException or
                InvalidOperationException or System.ComponentModel.Win32Exception or
                System.Runtime.InteropServices.COMException or
                InvalidDataException or NotSupportedException => true,
            _ => false,
        };

    private static bool CanRestoreEffect(
        RazerHidLightingEffect effect,
        RazerHidColor? color) =>
        effect is RazerHidLightingEffect.Spectrum or RazerHidLightingEffect.Wave or
            RazerHidLightingEffect.Reactive or RazerHidLightingEffect.Off ||
        color is not null;

    private readonly record struct ZoneBaseline(
        RazerHidLedZone Zone,
        RazerHidLightingEffect? Effect,
        RazerHidColor? Color);
}
