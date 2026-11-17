using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed partial class RazerMxicHeadsetService
{
    private async Task<RazerHeadsetAudioState> ReadProtocol40StateAsync(
        CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        var supportsEq = RazerProtocol40HeadsetProtocol.SupportsEq(_productId);
        await _transport.SendAsync(
            RazerProtocol40HeadsetProtocol.CreateConnect(), cancellationToken).ConfigureAwait(false);
        var battery = await ProbeAsync(
            "battery",
            RazerProtocol40HeadsetProtocol.CreateGetBattery(),
            [],
            RazerProtocol40HeadsetProtocol.ReadBatteryPercent,
            errors,
            cancellationToken).ConfigureAwait(false);
        var charging = await ProbeAsync(
            "charging",
            RazerProtocol40HeadsetProtocol.CreateGetCharging(),
            [],
            RazerProtocol40HeadsetProtocol.ReadChargingStatus,
            errors,
            cancellationToken).ConfigureAwait(false);
        var preset = (Ok: false, Value: (byte)0);
        var bands = (Ok: false, Value: (byte[])null!);
        if (supportsEq)
        {
            preset = await ProbeAsync(
                "EQ preset",
                RazerProtocol40HeadsetProtocol.CreateGetEqPreset(),
                [],
                RazerProtocol40HeadsetProtocol.ReadEqPreset,
                errors,
                cancellationToken).ConfigureAwait(false);
            bands = await ProbeAsync(
                "EQ bands",
                RazerProtocol40HeadsetProtocol.CreateGetEqBands(),
                [],
                RazerProtocol40HeadsetProtocol.ReadEqBands,
                errors,
                cancellationToken).ConfigureAwait(false);
        }
        var micMute = (Ok: false, Value: false);
        if (RazerProtocol40HeadsetProtocol.SupportsMic(_productId))
        {
            micMute = await ProbeAsync(
                "microphone mute",
                RazerProtocol40HeadsetProtocol.CreateGetMicMute(),
                [],
                RazerProtocol40HeadsetProtocol.ReadMicMute,
                errors,
                cancellationToken).ConfigureAwait(false);
        }
        var sidetone = (Ok: false, Value: false);
        if (RazerProtocol40HeadsetProtocol.SupportsSidetone(_productId))
        {
            sidetone = await ProbeAsync(
                "sidetone",
                RazerProtocol40HeadsetProtocol.CreateGetSidetone(),
                [],
                RazerProtocol40HeadsetProtocol.ReadSidetone,
                errors,
                cancellationToken).ConfigureAwait(false);
        }
        var dnd = await ReadAudioDndAsync(errors, cancellationToken).ConfigureAwait(false);

        return new RazerHeadsetAudioState(
            Firmware: null,
            BatteryPercent: battery.Ok ? battery.Value : null,
            Charging: charging.Ok ? charging.Value : null,
            EqPreset: preset.Ok ? preset.Value : null,
            EqBands: bands.Ok ? bands.Value : null,
            Bass: null,
            MicMuted: micMute.Ok ? micMute.Value : null,
            MicVolume: null,
            AudioSource: null,
            NoiseSuppression: null,
            AncState: null,
            AncLevel: null,
            Errors: errors)
        {
            Dnd = dnd,
            SidetonePercent = sidetone.Ok ? (sidetone.Value ? (byte)100 : (byte)0) : null,
            MicReturnSwitch = sidetone.Ok ? sidetone.Value : null,
        };
    }
}
