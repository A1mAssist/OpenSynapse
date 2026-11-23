using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed partial class RazerHeadsetAudioService
{
    internal async Task<bool> ProbeFeatureAudioFirmwareAsync(CancellationToken cancellationToken)
    {
        if (!HasFeature91ProductCapabilities) return false;
        var request = RazerHeadsetAudioProtocol.GetFirmware(NextTransactionId());
        var response = await QueryFeatureAudioAsync(request, cancellationToken).ConfigureAwait(false);
        // The product transport validates the actual wire transaction and product-specific checksum rules.
        // Do not compare its rotated transaction to the service's original counter.
        if (response.Length != RazerFeatureReport.Length || response[0] != request[0] ||
            response[1] != 2 || response[6] is < 2 or > 80 || response[7] != 0 || response[8] != 0x81)
            return false;
        _ = RazerHeadsetAudioProtocol.ReadFirmware(response);
        return true;
    }

    private Task<byte[]> QueryFeatureAudioAsync(byte[] report, CancellationToken cancellationToken)
    {
        RazerAudioFeatureProtocol.PrepareAudioCommand(_productId, report);
        RazerAudioFeatureProtocol.PrepareRequest(_productId, report);
        var wait = _productId == 0x0F66 && _deviceWait < TimeSpan.FromMilliseconds(40)
            ? TimeSpan.FromMilliseconds(40)
            : _deviceWait;
        return _transport.QueryPreparedForProductAsync(
            _devicePath, _productId, report, wait, cancellationToken);
    }

    private async Task<RazerHeadsetAudioState> ReadFeature91ProductStateAsync(CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        var firmware = await ProbeAsync("firmware", RazerHeadsetAudioProtocol.GetFirmware,
            RazerHeadsetAudioProtocol.ReadFirmware, errors, cancellationToken).ConfigureAwait(false);
        var preset = await ProbeAsync("EQ preset", txn => RazerHeadsetAudioProtocol.GetEqPreset(txn),
            RazerHeadsetAudioProtocol.ReadEqPreset, errors, cancellationToken).ConfigureAwait(false);
        var bass = RazerAudioFeatureProtocol.IsNommoV2(_productId)
            ? await ProbeAsync("bass", txn => RazerHeadsetAudioProtocol.GetBass(txn),
                RazerHeadsetAudioProtocol.ReadBass, errors, cancellationToken).ConfigureAwait(false)
            : (Ok: false, Value: default(byte));
        var bands = RazerAudioFeatureProtocol.SupportsHidEqBands(_productId)
            ? await ProbeAsync("EQ bands", txn => RazerHeadsetAudioProtocol.GetEqBands(txn),
                response => RazerHeadsetAudioProtocol.ReadEqBands(response, true), errors, cancellationToken)
                .ConfigureAwait(false)
            : (Ok: false, Value: default(byte[])!);
        return new RazerHeadsetAudioState(
            Firmware: firmware.Ok ? firmware.Value : null,
            BatteryPercent: null, Charging: null,
            EqPreset: preset.Ok ? preset.Value : null,
            EqBands: bands.Ok ? bands.Value : null,
            Bass: bass.Ok ? bass.Value : null,
            MicMuted: null, MicVolume: null, AudioSource: null, NoiseSuppression: null,
            AncState: null, AncLevel: null, Errors: errors);
    }

    internal bool HasFeature91ProductCapabilities =>
        _transportKind == RazerHeadsetAudioTransport.Feature91 &&
        RazerAudioFeatureProtocol.HasProductCapabilities(_productId);

    internal ushort FeatureAudioProductId => _productId;
}
