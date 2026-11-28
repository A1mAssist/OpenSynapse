using System.Collections.Concurrent;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed partial class RazerHidSpecialLightingService
{
    private readonly ConcurrentDictionary<string, byte> _avneraBrightness =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _protocol40Brightness =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _protocol40Armed =
        new(StringComparer.OrdinalIgnoreCase);

    public async Task SetKrakenLightingAsync(
        RazerHidSpecialLightingConnection connection,
        RazerHidLightingSettings settings,
        byte? intensity = null,
        byte? brightness = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        switch (connection.Kind)
        {
            case RazerHidSpecialLightingKind.KrystalT2:
                await SetKrystalLightingAsync(connection, settings, cancellationToken)
                    .ConfigureAwait(false);
                return;
            case RazerHidSpecialLightingKind.KrakenV4:
                await SetKrakenV4LightingAsync(connection, settings, intensity, brightness, cancellationToken)
                    .ConfigureAwait(false);
                return;
            case RazerHidSpecialLightingKind.AvneraChroma:
                await SetAvneraChromaAsync(connection, settings, brightness, cancellationToken)
                    .ConfigureAwait(false);
                return;
            case RazerHidSpecialLightingKind.Protocol40Chroma:
                await SetProtocol40ChromaAsync(connection, settings, brightness, cancellationToken)
                    .ConfigureAwait(false);
                return;
        }

        RequireReady(connection, RazerHidSpecialLightingKind.Kraken37);
        if (!connection.SupportedEffects.Contains(settings.Effect))
        {
            throw new NotSupportedException(
                $"RazerHid Kraken 1532:{connection.ProductId:X4} does not support {settings.Effect}.");
        }

        if (RazerKraken40LightingProtocol.IsSupported(connection.ProductId))
        {
            await SetKraken40LightingAsync(connection, settings, brightness, cancellationToken).ConfigureAwait(false);
            return;
        }

        var device = KrakenDevices[connection.ProductId];
        var reports = settings.Effect switch
        {
            RazerHidLightingEffect.Off => RazerHidKrakenProtocol.CreateOff(device.Layout),
            RazerHidLightingEffect.Static => RazerHidKrakenProtocol.CreateStatic(
                device.Layout, settings.Primary, intensity, device.WritesColor),
            RazerHidLightingEffect.Spectrum => RazerHidKrakenProtocol.CreateSpectrum(device.Layout),
            RazerHidLightingEffect.BreathingSingle => RazerHidKrakenProtocol.CreateBreathing(
                device.Layout, [settings.Primary]),
            RazerHidLightingEffect.BreathingDual => RazerHidKrakenProtocol.CreateBreathing(
                device.Layout, [settings.Primary, settings.Secondary]),
            RazerHidLightingEffect.BreathingTriple => RazerHidKrakenProtocol.CreateBreathing(
                device.Layout, [settings.Primary, settings.Secondary, settings.Tertiary]),
            RazerHidLightingEffect.Custom => RazerHidKrakenProtocol.CreateCustom(
                device.Layout, settings.Primary, intensity),
            _ => throw new NotSupportedException(),
        };

        var gate = _gates.GetOrAdd(connection.InstanceId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (var report in reports)
            {
                await _sendOutput(connection.DevicePath!, report, cancellationToken).ConfigureAwait(false);
                if (settings.Effect != RazerHidLightingEffect.Custom)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(report[2] * 15), cancellationToken).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task SetKraken40LightingAsync(
        RazerHidSpecialLightingConnection connection,
        RazerHidLightingSettings settings,
        byte? brightness,
        CancellationToken cancellationToken)
    {
        var reports = new List<byte[]>();
        if (brightness is { } percent) reports.Add(RazerKraken40LightingProtocol.CreateBrightness(connection.ProductId, percent));
        // Ultimate's effect command carries no colour. A static colour uses its frame mode.
        if (connection.ProductId == 0x0527 && settings.Effect == RazerHidLightingEffect.Static)
            settings = settings with { Effect = RazerHidLightingEffect.Custom };
        reports.Add(RazerKraken40LightingProtocol.CreateEffect(connection.ProductId, settings));
        if (settings.Effect == RazerHidLightingEffect.Custom)
            reports.Add(RazerKraken40LightingProtocol.CreateFrame(connection.ProductId, settings.Primary));
        var gate = _gates.GetOrAdd(connection.InstanceId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (var report in reports)
                await _sendOutput(connection.DevicePath!,
                    RazerKrakenBaseProtocol.PadOutputReport(report, connection.OutputReportLength), cancellationToken)
                    .ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }

    private async Task SetAvneraChromaAsync(
        RazerHidSpecialLightingConnection connection,
        RazerHidLightingSettings settings,
        byte? brightness,
        CancellationToken cancellationToken)
    {
        RequireReady(connection, RazerHidSpecialLightingKind.AvneraChroma);
        if (!connection.SupportedEffects.Contains(settings.Effect))
        {
            throw new NotSupportedException(
                $"RazerHid Nari 1532:{connection.ProductId:X4} does not support {settings.Effect}.");
        }

        var path = connection.DevicePath!;
        var color = settings.Primary;
        var reports = new List<byte[]>(3);
        if (brightness is { } percent &&
            (!_avneraBrightness.TryGetValue(path, out var sent) || sent != percent))
        {
            reports.Add(RazerAvnera65Protocol.CreateSetChromaBrightnessRequest(percent));
        }

        switch (settings.Effect)
        {
            case RazerHidLightingEffect.Off:
                reports.Add(RazerAvnera65Protocol.CreateSetChromaEffectRequest(
                    RazerChromaEffect.Off, 0, 0, 0));
                break;
            case RazerHidLightingEffect.Static:
                reports.Add(RazerAvnera65Protocol.CreateSetChromaEffectRequest(
                    RazerChromaEffect.Static, color.Red, color.Green, color.Blue));
                break;
            case RazerHidLightingEffect.Custom:
                // The bundle maps every unsupported effect id to CustomFrame, so the
                // effect switch and the one-colour frame are two separate writes.
                reports.Add(RazerAvnera65Protocol.CreateSetChromaEffectRequest(
                    RazerChromaEffect.CustomFrame, 0, 0, 0));
                reports.Add(RazerAvnera65Protocol.CreateSetChromaFrameRequest(
                    color.Red, color.Green, color.Blue));
                break;
            default:
                throw new NotSupportedException();
        }

        var gate = _gates.GetOrAdd(connection.InstanceId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (var report in reports)
            {
                _diagnostic?.Invoke(
                    $"nari lighting 1532:{connection.ProductId:X4} effect={settings.Effect} " +
                    $"report=[{Convert.ToHexString(report)}]");
                await _sendFeature(path, report, cancellationToken).ConfigureAwait(false);
            }

            if (brightness is { } value)
            {
                _avneraBrightness[path] = value;
            }
        }
        catch
        {
            _avneraBrightness.TryRemove(path, out _);
            throw;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task SetProtocol40ChromaAsync(
        RazerHidSpecialLightingConnection connection,
        RazerHidLightingSettings settings,
        byte? brightness,
        CancellationToken cancellationToken)
    {
        RequireReady(connection, RazerHidSpecialLightingKind.Protocol40Chroma);
        if (!connection.SupportedEffects.Contains(settings.Effect))
        {
            throw new NotSupportedException(
                $"RazerHid Hammerhead 1532:{connection.ProductId:X4} does not support {settings.Effect}.");
        }

        var path = connection.DevicePath!;
        var color = settings.Primary;
        var reports = new List<byte[]>(3);
        if (!_protocol40Armed.ContainsKey(path))
        {
            reports.Add(RazerProtocol40HeadsetProtocol.CreateChromaConnect());
        }
        if (brightness is { } percent &&
            (!_protocol40Brightness.TryGetValue(path, out var sent) || sent != percent))
        {
            reports.Add(RazerProtocol40HeadsetProtocol.CreateSetChromaBrightness(percent));
        }

        reports.Add(settings.Effect switch
        {
            RazerHidLightingEffect.Off =>
                RazerProtocol40HeadsetProtocol.CreateSetChromaEffect(0x00, 0x00, []),
            RazerHidLightingEffect.Static =>
                RazerProtocol40HeadsetProtocol.CreateSetChromaEffect(
                    0x01, 0x00, [color.Red, color.Green, color.Blue]),
            RazerHidLightingEffect.BreathingSingle =>
                RazerProtocol40HeadsetProtocol.CreateSetChromaEffect(
                    0x02, 0x00, [color.Red, color.Green, color.Blue]),
            RazerHidLightingEffect.Spectrum =>
                RazerProtocol40HeadsetProtocol.CreateSetChromaEffect(0x03, 0x00, []),
            _ => throw new NotSupportedException(),
        });

        var gate = _gates.GetOrAdd(connection.InstanceId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (var report in reports)
            {
                _diagnostic?.Invoke(
                    $"hammerhead lighting 1532:{connection.ProductId:X4} effect={settings.Effect} " +
                    $"report=[{Convert.ToHexString(report)}]");
                await _sendFeature(path, report, cancellationToken).ConfigureAwait(false);
            }

            _protocol40Armed[path] = 0;
            if (brightness is { } value)
            {
                _protocol40Brightness[path] = value;
            }
        }
        catch
        {
            _protocol40Armed.TryRemove(path, out _);
            _protocol40Brightness.TryRemove(path, out _);
            throw;
        }
        finally
        {
            gate.Release();
        }
    }
}
