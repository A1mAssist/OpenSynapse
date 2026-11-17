using System.Collections.Concurrent;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed partial class RazerHidSpecialLightingService
{
    private readonly ConcurrentDictionary<string, byte> _krystalArmed =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _krystalCustom =
        new(StringComparer.OrdinalIgnoreCase);

    private async Task SetKrystalLightingAsync(
        RazerHidSpecialLightingConnection connection,
        RazerHidLightingSettings settings,
        CancellationToken cancellationToken)
    {
        RequireReady(connection, RazerHidSpecialLightingKind.KrystalT2);
        if (!connection.SupportedEffects.Contains(settings.Effect))
        {
            throw new NotSupportedException(
                $"RazerHid Kraken 1532:{connection.ProductId:X4} does not support {settings.Effect}.");
        }

        var path = connection.DevicePath!;
        var gate = _gates.GetOrAdd(connection.InstanceId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var reports = new List<byte[]>(4);
            if (!_krystalArmed.ContainsKey(path))
            {
                reports.Add(RazerKrystalT2LightingProtocol.CreateConnect());
                reports.Add(RazerKrystalT2LightingProtocol.CreateForceChroma(true));
            }

            var color = settings.Effect is RazerHidLightingEffect.Off or RazerHidLightingEffect.Spectrum
                ? (RazerHidColor?)null
                : settings.Primary;
            reports.Add(RazerKrystalT2LightingProtocol.CreateEffect(settings.Effect, color));
            if (settings.Effect == RazerHidLightingEffect.Custom)
            {
                reports.Add(RazerKrystalT2LightingProtocol.CreateFrame(settings.Primary));
            }

            try
            {
                foreach (var report in reports)
                {
                    await _sendOutput(
                            path,
                            RazerKrystalT2LightingProtocol.Pad(report),
                            cancellationToken)
                        .ConfigureAwait(false);
                }

                _krystalArmed[path] = 0;
                if (settings.Effect == RazerHidLightingEffect.Custom)
                {
                    _krystalCustom[path] = 0;
                }
                else
                {
                    _krystalCustom.TryRemove(path, out _);
                }
            }
            catch
            {
                _krystalArmed.TryRemove(path, out _);
                _krystalCustom.TryRemove(path, out _);
                throw;
            }
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Writes one studio frame. The first frame after another effect re-enters
    /// CustomFrame; unchanged frames are deduplicated by the caller.
    /// </summary>
    internal async Task SetKrystalFrameAsync(
        RazerHidSpecialLightingConnection connection,
        RazerHidColor color,
        CancellationToken cancellationToken = default)
    {
        RequireReady(connection, RazerHidSpecialLightingKind.KrystalT2);
        var path = connection.DevicePath!;
        var gate = _gates.GetOrAdd(connection.InstanceId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var reports = new List<byte[]>(3);
            if (!_krystalArmed.ContainsKey(path))
            {
                reports.Add(RazerKrystalT2LightingProtocol.CreateConnect());
                reports.Add(RazerKrystalT2LightingProtocol.CreateForceChroma(true));
            }
            if (!_krystalCustom.ContainsKey(path))
            {
                reports.Add(RazerKrystalT2LightingProtocol.CreateEffect(
                    RazerHidLightingEffect.Custom));
            }
            reports.Add(RazerKrystalT2LightingProtocol.CreateFrame(color));

            try
            {
                foreach (var report in reports)
                {
                    await _sendOutput(
                            path,
                            RazerKrystalT2LightingProtocol.Pad(report),
                            cancellationToken)
                        .ConfigureAwait(false);
                }

                _krystalArmed[path] = 0;
                _krystalCustom[path] = 0;
            }
            catch
            {
                _krystalArmed.TryRemove(path, out _);
                _krystalCustom.TryRemove(path, out _);
                throw;
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private static KrakenDefinition Krystal(string name) => new(name,
        RazerHidKrakenLayout.Kylie,
        Effects(RazerHidLightingEffect.Off, RazerHidLightingEffect.Static,
            RazerHidLightingEffect.BreathingSingle, RazerHidLightingEffect.Spectrum,
            RazerHidLightingEffect.Custom),
        true,
        RazerHidSpecialLightingKind.KrystalT2,
        LedCount: 1);
}
