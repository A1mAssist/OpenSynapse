using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed partial class RazerAwBleHeadsetService
{
    public async Task<RazerAwChromaState> ReadChromaEffectAsync(
        CancellationToken cancellationToken = default)
    {
        var response = await ReadFrameAsync(
            RazerAwBleProtocol.ChromaEffectGetCommand,
            "Chroma effect",
            cancellationToken,
            ProbeTimeout).ConfigureAwait(false);
        if (!RazerAwBleProtocol.TryParseChromaEffect(response, out var effect, out _))
        {
            throw new InvalidDataException(
                "The headset returned an invalid Chroma effect reply.");
        }

        var primary = Array.Empty<byte>();
        var secondary = Array.Empty<byte>();
        switch (effect)
        {
            case RazerAwChromaEffect.Static:
                primary = await ReadChromaColorAsync(
                    RazerAwBleProtocol.ChromaStaticColorGetCommand,
                    3,
                    "Chroma static color",
                    cancellationToken).ConfigureAwait(false);
                break;
            case RazerAwChromaEffect.Breathing:
                var breathing = await ReadChromaColorAsync(
                    RazerAwBleProtocol.ChromaBreathingColorGetCommand,
                    6,
                    "Chroma breathing color",
                    cancellationToken).ConfigureAwait(false);
                primary = breathing[..3];
                secondary = breathing[3..];
                break;
            case RazerAwChromaEffect.AudioMeter:
                primary = await ReadChromaColorAsync(
                    RazerAwBleProtocol.ChromaAudioMeterColorGetCommand,
                    3,
                    "Chroma audio-meter color",
                    cancellationToken).ConfigureAwait(false);
                break;
        }

        return new RazerAwChromaState(effect, primary, secondary);
    }

    public async Task<RazerAwChromaState> SetChromaEffectAsync(
        RazerAwChromaEffect effect,
        byte[] primaryColor,
        byte[] secondaryColor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(primaryColor);
        ArgumentNullException.ThrowIfNull(secondaryColor);

        byte[] request;
        byte setCommand;
        switch (effect)
        {
            case RazerAwChromaEffect.Static:
                request = RazerAwBleProtocol.CreateChromaStaticColorRequest(primaryColor);
                setCommand = RazerAwBleProtocol.ChromaStaticColorSetCommand;
                break;
            case RazerAwChromaEffect.Breathing:
                request = RazerAwBleProtocol.CreateChromaBreathingColorRequest(
                    primaryColor, secondaryColor);
                setCommand = RazerAwBleProtocol.ChromaBreathingColorSetCommand;
                break;
            case RazerAwChromaEffect.AudioMeter:
                request = RazerAwBleProtocol.CreateChromaAudioMeterColorRequest(primaryColor);
                setCommand = RazerAwBleProtocol.ChromaAudioMeterColorSetCommand;
                break;
            case RazerAwChromaEffect.Off:
            case RazerAwChromaEffect.Spectrum:
                request = RazerAwBleProtocol.CreateChromaEffectRequest(effect);
                setCommand = RazerAwBleProtocol.ChromaEffectSetCommand;
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(effect), effect, "Unsupported Chroma effect.");
        }

        var acknowledgement = await _transport.ExchangeAsync(
            request,
            _timeout,
            cancellationToken).ConfigureAwait(false);
        if (acknowledgement is null)
        {
            throw new InvalidOperationException(
                "The headset did not acknowledge the Chroma effect write.");
        }

        if (!RazerAwBleProtocol.IsReplyForCommand(acknowledgement, setCommand))
        {
            throw new InvalidDataException(
                "The headset returned an invalid Chroma effect acknowledgement.");
        }

        var actual = await ReadChromaEffectAsync(cancellationToken).ConfigureAwait(false);
        if (actual.Effect != effect)
        {
            throw new InvalidOperationException(
                "Chroma effect readback did not match the requested effect.");
        }

        if (effect is RazerAwChromaEffect.Static or RazerAwChromaEffect.AudioMeter &&
            !actual.PrimaryColor.SequenceEqual(primaryColor))
        {
            throw new InvalidOperationException(
                "Chroma color readback did not match the requested color.");
        }

        if (effect == RazerAwChromaEffect.Breathing &&
            (!actual.PrimaryColor.SequenceEqual(primaryColor) ||
             !actual.SecondaryColor.SequenceEqual(secondaryColor)))
        {
            throw new InvalidOperationException(
                "Chroma breathing color readback did not match the requested colors.");
        }

        return actual;
    }

    private async Task<byte[]> ReadChromaColorAsync(
        byte command,
        int expectedLength,
        string label,
        CancellationToken cancellationToken)
    {
        var response = await ReadFrameAsync(
            command,
            label,
            cancellationToken,
            ProbeTimeout).ConfigureAwait(false);
        if (!RazerAwBleProtocol.TryParseChromaColor(
                response, command, expectedLength, out var color))
        {
            throw new InvalidDataException(
                $"The headset returned an invalid {label} reply.");
        }

        return color;
    }
}
