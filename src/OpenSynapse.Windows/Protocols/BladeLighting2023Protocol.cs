using OpenSynapse.Core.Devices;

namespace OpenSynapse.Windows.Protocols;

/// <summary>
/// Request builders and decoders for the Blade 16 2023 standard lighting reports.
///
/// This generation is a single-zone, whole-keyboard surface: brightness has its own
/// report and effects carry no per-key matrix. The stored effect RGB is not readable,
/// so no color readback is offered and only firmware-rendered effects are admitted.
///
/// Every report is prefixed with the store (0x01) and keyboard region (0x05) selector.
/// </summary>
public static class BladeLighting2023Protocol
{
    private const byte Store = 0x01;
    private const byte KeyboardRegion = 0x05;

    private const byte TransactionId = 0x03;

    /// <summary>Selector payload length shared by the brightness and effect reports.</summary>
    private const byte SelectorDataSize = 0x03;

    private const byte BrightnessCommandClass = 0x03;
    private const byte BrightnessQueryCommandId = 0x83;
    private const byte BrightnessSetCommandId = 0x03;

    private const byte EffectCommandClass = 0x0F;
    private const byte EffectQueryCommandId = 0x82;
    private const byte EffectSetCommandId = 0x02;

    /// <summary>
    /// The Wave report carries a fixed rightward direction byte this model cannot change,
    /// which is why the direction control stays hidden for this generation.
    /// </summary>
    // The middleware maps WAVE_DIRECTION.RIGHT to 2 and leaves 1 for the default
    // (leftward) direction.
    private const byte WaveDirectionRightward = 0x02;

    /// <summary>Raw brightness is a single byte; every value in the byte range is admitted.</summary>
    public const int BrightnessMaximum = 255;

    public const byte EffectOff = 0x00;
    public const byte EffectBreathing = 0x02;
    public const byte EffectSpectrum = 0x03;
    public const byte EffectWave = 0x04;

    public static byte[] CreateGetBrightnessRequest() =>
        RazerFeatureReport.CreateRequest(
            TransactionId,
            SelectorDataSize,
            BrightnessCommandClass,
            BrightnessQueryCommandId,
            [Store, KeyboardRegion, 0x00]);

    public static byte[] CreateSetBrightnessRequest(byte brightness) =>
        RazerFeatureReport.CreateRequest(
            TransactionId,
            SelectorDataSize,
            BrightnessCommandClass,
            BrightnessSetCommandId,
            [Store, KeyboardRegion, brightness]);

    public static byte[] CreateGetEffectRequest() =>
        RazerFeatureReport.CreateRequest(
            TransactionId,
            SelectorDataSize,
            EffectCommandClass,
            EffectQueryCommandId,
            [Store, KeyboardRegion, 0x00]);

    /// <summary>
    /// Every admitted effect is stored under the same selector, so the effect byte and its
    /// effect-specific trailing parameters are supplied together.
    /// </summary>
    public static byte[] CreateSetEffectRequest(byte effect, ReadOnlySpan<byte> parameters)
    {
        // The middleware's 0F:02 effect frame is
        // [profile, region, effect, flags, rate, colorCount, colors...]. Simple
        // effects still carry the three trailing control bytes as zero.
        if (parameters.Length > SelectorDataSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(parameters),
                "Simple Blade effects carry at most one flag byte.");
        }

        var payload = new byte[6];
        payload[0] = Store;
        payload[1] = KeyboardRegion;
        payload[2] = effect;
        parameters.CopyTo(payload.AsSpan(SelectorDataSize));
        return RazerFeatureReport.CreateRequest(
            TransactionId,
            (byte)payload.Length,
            EffectCommandClass,
            EffectSetCommandId,
            payload);
    }

    public static byte[] CreateOffRequest() =>
        CreateSetEffectRequest(EffectOff, ReadOnlySpan<byte>.Empty);

    public static byte[] CreateBreathingRequest() =>
        CreateSetEffectRequest(EffectBreathing, ReadOnlySpan<byte>.Empty);

    public static byte[] CreateSpectrumRequest() =>
        CreateSetEffectRequest(EffectSpectrum, ReadOnlySpan<byte>.Empty);

    public static byte[] CreateWaveRequest() =>
        CreateSetEffectRequest(EffectWave, [WaveDirectionRightward]);

    public static byte ParseBrightness(ReadOnlySpan<byte> response)
    {
        var arguments = ReadSelector(
            response,
            CreateGetBrightnessRequest(),
            "keyboard brightness");
        return arguments[2];
    }

    /// <summary>
    /// Decodes the stored effect. Trailing parameter bytes are effect specific and are
    /// deliberately not interpreted: this model does not admit a color readback.
    /// </summary>
    public static BladeLighting2023Effect ParseEffect(ReadOnlySpan<byte> response)
    {
        var arguments = ReadSelector(response, CreateGetEffectRequest(), "keyboard effect");
        return arguments[2] switch
        {
            EffectOff => BladeLighting2023Effect.Off,
            EffectBreathing => BladeLighting2023Effect.Breathing,
            EffectSpectrum => BladeLighting2023Effect.Spectrum,
            EffectWave => BladeLighting2023Effect.Wave,
            var value => throw new InvalidOperationException(
                $"Blade returned an unsupported keyboard effect: 0x{value:X2}."),
        };
    }

    private static ReadOnlySpan<byte> ReadSelector(
        ReadOnlySpan<byte> response,
        ReadOnlySpan<byte> request,
        string feature)
    {
        if (!RazerFeatureReport.IsSuccessfulResponse(
                request, response, SelectorDataSize, allowRemainingPacketsMismatch: true))
        {
            throw new InvalidOperationException(
                $"Blade {feature} returned an invalid or out-of-order feature report.");
        }

        var arguments = response[RazerFeatureReport.ArgumentsOffset..];
        if (arguments[0] != Store || arguments[1] != KeyboardRegion)
        {
            throw new InvalidOperationException(
                $"Blade returned an unexpected {feature} selector.");
        }

        return arguments;
    }
}
