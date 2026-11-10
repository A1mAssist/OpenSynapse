using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed partial class RazerHeadsetAudioService
{
    public Task SetBassAsync(byte level, CancellationToken cancellationToken = default)
    {
        if (level > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(level), "Bass level must be between 0 and 100.");
        }

        if (_protocol25Transport is not null)
        {
            RequireProtocol25Support(RazerProtocol25FeatureProtocol.SupportsHidBass(_productId), "bass");
            return SetProtocol25BassAsync(level, cancellationToken);
        }

        return WriteAsync(
            txn => RazerHeadsetAudioProtocol.GetBass(txn),
            RazerHeadsetAudioProtocol.ReadBass,
            level,
            (value, txn) => RazerHeadsetAudioProtocol.SetBass(txn, value),
            value => value == level,
            "bass",
            cancellationToken);
    }

    public Task SetMicMuteAsync(bool muted, CancellationToken cancellationToken = default)
    {
        if (_protocol25Transport is not null)
        {
            RequireProtocol25Support(
                RazerProtocol25FeatureProtocol.SupportsHidMicrophone(_productId), "microphone mute");
            return SetProtocol25MicMuteAsync(muted, cancellationToken);
        }

        return WriteAsync(
            txn => RazerHeadsetAudioProtocol.GetMicMute(txn),
            RazerHeadsetAudioProtocol.ReadMicMute,
            muted,
            (value, txn) => RazerHeadsetAudioProtocol.SetMicMute(txn, value),
            value => value == muted,
            "microphone mute",
            cancellationToken);
    }

    public Task SetMicVolumeAsync(byte volume, CancellationToken cancellationToken = default)
    {
        if (volume > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(volume), "Microphone volume must be between 0 and 100.");
        }

        if (_protocol25Transport is not null)
        {
            RequireProtocol25Support(
                RazerProtocol25FeatureProtocol.SupportsHidMicVolume(_productId), "microphone volume");
            return SetProtocol25MicVolumeAsync(volume, cancellationToken);
        }

        return WriteAsync(
            txn => RazerHeadsetAudioProtocol.GetMicVolume(txn),
            RazerHeadsetAudioProtocol.ReadMicVolume,
            volume,
            (value, txn) => RazerHeadsetAudioProtocol.SetMicVolume(txn, value),
            value => value == volume,
            "microphone volume",
            cancellationToken);
    }

    private async Task SetNoiseSuppressionCoreAsync(bool enabled, CancellationToken cancellationToken)
    {
        if (_protocol25Transport is not null)
        {
            RequireProtocol25Support(
                RazerProtocol25FeatureProtocol.SupportsHidAudioInput(_productId), "noise suppression");
            // The 65-byte bundle only carries the 08:16 switch; 08:1C has no short-report
            // evidence, so the amount is left untouched.
            await SetProtocol25NoiseSuppressionAsync(enabled, cancellationToken).ConfigureAwait(false);
            return;
        }

        // The switch is the audio-input control state (08:16, source 3); 08:1C only carries
        // the amount, where 1 would mean 1% suppression. Enable first, then raise the level.
        await WriteAsync(
            txn => RazerHeadsetAudioProtocol.GetAudioInputControlState(txn),
            RazerHeadsetAudioProtocol.ReadAudioInputControlState,
            enabled,
            (value, txn) => RazerHeadsetAudioProtocol.SetAudioInputControlState(txn, value),
            value => value == enabled,
            "noise suppression",
            cancellationToken).ConfigureAwait(false);

        if (!enabled)
        {
            return;
        }

        await WriteAsync(
            txn => RazerHeadsetAudioProtocol.GetNoiseSuppression(txn),
            RazerHeadsetAudioProtocol.ReadNoiseSuppression,
            true,
            (value, txn) => RazerHeadsetAudioProtocol.SetNoiseSuppression(txn, value),
            value => value,
            "noise suppression level",
            cancellationToken).ConfigureAwait(false);
    }
}
