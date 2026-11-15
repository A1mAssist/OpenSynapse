using System.Diagnostics;
using Microsoft.UI.Dispatching;
using OpenSynapse.Core.Lighting;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Lighting;
using OpenSynapse.Windows.Protocols;
using Windows.UI;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHeadsetAudioViewModel
{
    /// <summary>Matches Chroma Studio's native playback rate for Mako X frames.</summary>
    private const double SoftwareEffectFramesPerSecond = 25;

    private static readonly TimeSpan SoftwareEffectFrameInterval =
        TimeSpan.FromSeconds(1d / SoftwareEffectFramesPerSecond);

    /// <summary>Synapse blends the previous effect into the new one over ~1.5 s.</summary>
    private const double CrossfadeSeconds = 1.5;

    /// <summary>Longest wait for the first Graphics Capture frame before Ambient fails.</summary>
    private static readonly TimeSpan AmbientCaptureTimeout = TimeSpan.FromSeconds(3);

    private readonly DispatcherQueue? _dispatcher = DispatcherQueue.GetForCurrentThread();
    private StudioInputSampler? _softwareSampler;
    private RazerMakoXChromaService? _softwareService;
    private CancellationTokenSource? _softwareCancellation;
    private Task? _softwarePump;
    private byte[]? _softwareLastFrame;

    /// <summary>Below this audio level the meter goes dark instead of holding one LED.</summary>
    private const double AudioMeterSilenceLevel = 0.02;

    /// <summary>
    /// Effects Synapse 4 renders in its lighting engine and streams as per-LED
    /// frames on the Mako X (the page's setChromaEffect call always carries the
    /// device's CustomFrame id 8). Off, Static and Wave stay on the firmware:
    /// Static is visually identical and Wave has no Synapse counterpart.
    /// </summary>
    internal static bool IsSoftwareRenderedEffect(RazerChromaEffect effect) =>
        effect is RazerChromaEffect.Static or RazerChromaEffect.Breathing or
            RazerChromaEffect.Spectrum or
            RazerChromaEffect.Wheel or RazerChromaEffect.Starlight or
            RazerChromaEffect.Fire or RazerChromaEffect.AudioMeter or
            RazerChromaEffect.CustomFrame;

    /// <summary>
    /// One 25-frame rotation cycle (1 s at 25 fps, speed 360) captured from the
    /// Synapse lighting engine on a Mako X. The engine paints a single-row strip
    /// with a travelling 10-stop palette, which the radial wheel renderer cannot
    /// reproduce, so the measured cycle is replayed instead.
    /// </summary>
    private static readonly byte[][] WheelCycleFrames =
    [
        [0x14, 0x6D, 0xFF, 0x06, 0xD4, 0xFF, 0xCF, 0xFF, 0x00, 0xFF, 0x44, 0x08, 0xFF, 0x12, 0x5A, 0xFF, 0x15, 0xBC, 0xFE, 0x18, 0xFF, 0xB4, 0x2C, 0xFF, 0x69, 0x41, 0xFF, 0x18, 0x58, 0xFF],
        [0x0C, 0xA6, 0xFF, 0x00, 0xFF, 0xB2, 0xE6, 0xFF, 0x00, 0xFF, 0x16, 0x0F, 0xFF, 0x14, 0x8F, 0xFF, 0x17, 0xF1, 0xD5, 0x23, 0xFF, 0x8B, 0x37, 0xFF, 0x41, 0x4C, 0xFF, 0x18, 0x58, 0xFF],
        [0x04, 0xDE, 0xFF, 0x3C, 0xFF, 0x00, 0xEF, 0xF7, 0x00, 0xFF, 0x11, 0x3C, 0xFF, 0x16, 0xC5, 0xE1, 0x20, 0xFF, 0xAD, 0x2E, 0xFF, 0x63, 0x43, 0xFF, 0x18, 0x57, 0xFF, 0x18, 0x58, 0xFF],
        [0x00, 0xFF, 0x77, 0xBF, 0xFF, 0x00, 0xF9, 0xAE, 0x00, 0xFF, 0x13, 0x71, 0xFF, 0x17, 0xFB, 0xB9, 0x2B, 0xFF, 0x85, 0x39, 0xFF, 0x3B, 0x4E, 0xFF, 0x18, 0x58, 0xFF, 0x11, 0x86, 0xFF],
        [0x52, 0xFF, 0x00, 0xE5, 0xFF, 0x00, 0xFF, 0x70, 0x02, 0xFF, 0x15, 0xA7, 0xDA, 0x22, 0xFF, 0x90, 0x36, 0xFF, 0x5C, 0x44, 0xFF, 0x18, 0x58, 0xFF, 0x18, 0x58, 0xFF, 0x09, 0xBE, 0xFF],
        [0xD5, 0xFF, 0x00, 0xEF, 0xFF, 0x00, 0xFF, 0x43, 0x08, 0xFF, 0x16, 0xDD, 0xB2, 0x2D, 0xFF, 0x68, 0x41, 0xFF, 0x34, 0x50, 0xFF, 0x18, 0x58, 0xFF, 0x18, 0x58, 0xFF, 0x01, 0xF6, 0xFF],
        [0xE7, 0xFF, 0x00, 0xF8, 0xB7, 0x00, 0xFF, 0x15, 0x0F, 0xF1, 0x1B, 0xFF, 0x8A, 0x38, 0xFF, 0x40, 0x4C, 0xFF, 0x18, 0x58, 0xFF, 0x18, 0x58, 0xFF, 0x12, 0x7F, 0xFF, 0x09, 0xFF, 0x00],
        [0xF0, 0xF4, 0x00, 0xFF, 0x76, 0x01, 0xFF, 0x11, 0x3D, 0xC8, 0x26, 0xFF, 0x61, 0x43, 0xFF, 0x18, 0x58, 0xFF, 0x18, 0x58, 0xFF, 0x18, 0x58, 0xFF, 0x0A, 0xB7, 0xFF, 0x8C, 0xFF, 0x00],
        [0xF9, 0xAA, 0x00, 0xFF, 0x48, 0x07, 0xFF, 0x13, 0x73, 0xA0, 0x32, 0xFF, 0x39, 0x4E, 0xFF, 0x18, 0x58, 0xFF, 0x18, 0x58, 0xFF, 0x11, 0x87, 0xFF, 0x02, 0xEF, 0xFF, 0xE2, 0xFF, 0x00],
        [0xFF, 0x6E, 0x02, 0xFF, 0x1B, 0x0E, 0xFF, 0x15, 0xA8, 0x78, 0x3D, 0xFF, 0x18, 0x58, 0xFF, 0x18, 0x58, 0xFF, 0x17, 0x59, 0xFF, 0x09, 0xC0, 0xFF, 0x00, 0xFF, 0x08, 0xEB, 0xFF, 0x00],
        [0xFF, 0x40, 0x09, 0xFF, 0x11, 0x37, 0xFF, 0x16, 0xDE, 0x50, 0x48, 0xFF, 0x18, 0x58, 0xFF, 0x18, 0x58, 0xFF, 0x0F, 0x91, 0xFF, 0x00, 0xF8, 0xFF, 0x7C, 0xFF, 0x00, 0xF4, 0xD4, 0x00],
        [0xFF, 0x13, 0x0F, 0xFF, 0x13, 0x6C, 0xF0, 0x1C, 0xFF, 0x27, 0x53, 0xFF, 0x18, 0x58, 0xFF, 0x12, 0x81, 0xFF, 0x07, 0xC9, 0xFF, 0x0C, 0xFF, 0x00, 0xE1, 0xFF, 0x00, 0xFD, 0x8A, 0x00],
        [0xFF, 0x11, 0x40, 0xFF, 0x14, 0xA2, 0xC7, 0x27, 0xFF, 0x18, 0x58, 0xFF, 0x18, 0x58, 0xFF, 0x0A, 0xB9, 0xFF, 0x00, 0xFF, 0xF6, 0x8F, 0xFF, 0x00, 0xEA, 0xFF, 0x00, 0xFF, 0x5A, 0x05],
        [0xFF, 0x13, 0x75, 0xFF, 0x16, 0xD7, 0x9F, 0x32, 0xFF, 0x18, 0x58, 0xFF, 0x10, 0x8A, 0xFF, 0x01, 0xF1, 0xFF, 0x23, 0xFF, 0x00, 0xE2, 0xFF, 0x00, 0xF3, 0xDD, 0x00, 0xFF, 0x2C, 0x0B],
        [0xFF, 0x15, 0xAB, 0xF5, 0x1A, 0xFF, 0x77, 0x3D, 0xFF, 0x18, 0x58, 0xFF, 0x08, 0xC2, 0xFF, 0x00, 0xFF, 0x00, 0xA5, 0xFF, 0x00, 0xEB, 0xFF, 0x00, 0xFC, 0x93, 0x00, 0xFF, 0x10, 0x22],
        [0xFF, 0x16, 0xE0, 0xCC, 0x25, 0xFF, 0x4F, 0x48, 0xFF, 0x15, 0x6B, 0xFF, 0x00, 0xFA, 0xFF, 0x7F, 0xFF, 0x00, 0xE3, 0xFF, 0x00, 0xF4, 0xD2, 0x00, 0xFF, 0x5F, 0x04, 0xFF, 0x12, 0x57],
        [0xEE, 0x1C, 0xFF, 0xA4, 0x31, 0xFF, 0x26, 0x53, 0xFF, 0x0D, 0xA3, 0xFF, 0x13, 0xFF, 0x00, 0xE1, 0xFF, 0x00, 0xED, 0xFF, 0x00, 0xFD, 0x88, 0x00, 0xFF, 0x32, 0x0B, 0xFF, 0x14, 0x8D],
        [0xC5, 0x27, 0xFF, 0x7C, 0x3C, 0xFF, 0x18, 0x58, 0xFF, 0x05, 0xDB, 0xFF, 0x95, 0xFF, 0x00, 0xEA, 0xFF, 0x00, 0xF6, 0xC5, 0x00, 0xFF, 0x59, 0x05, 0xFF, 0x10, 0x1B, 0xFF, 0x15, 0xC2],
        [0x9D, 0x32, 0xFF, 0x53, 0x47, 0xFF, 0x18, 0x58, 0xFF, 0x00, 0xFF, 0x88, 0xE2, 0xFF, 0x00, 0xF3, 0xDB, 0x00, 0xFF, 0x7E, 0x00, 0xFF, 0x2B, 0x0C, 0xFF, 0x12, 0x51, 0xFF, 0x17, 0xF8],
        [0x75, 0x3E, 0xFF, 0x2B, 0x52, 0xFF, 0x18, 0x58, 0xFF, 0x4C, 0xFF, 0x00, 0xEC, 0xFF, 0x00, 0xFC, 0x91, 0x00, 0xFF, 0x51, 0x06, 0xFF, 0x10, 0x23, 0xFF, 0x13, 0x86, 0xDC, 0x21, 0xFF],
        [0x4E, 0x49, 0xFF, 0x18, 0x58, 0xFF, 0x15, 0x6B, 0xFF, 0xCB, 0xFF, 0x00, 0xF4, 0xD0, 0x00, 0xFF, 0x5F, 0x04, 0xFF, 0x25, 0x0C, 0xFF, 0x12, 0x57, 0xFF, 0x15, 0xBB, 0xB5, 0x2C, 0xFF],
        [0x25, 0x54, 0xFF, 0x18, 0x58, 0xFF, 0x0D, 0xA3, 0xFF, 0xE6, 0xFF, 0x00, 0xFE, 0x87, 0x00, 0xFF, 0x32, 0x0B, 0xFF, 0x10, 0x2B, 0xFF, 0x14, 0x8D, 0xFF, 0x17, 0xF0, 0x8C, 0x37, 0xFF],
        [0x18, 0x58, 0xFF, 0x18, 0x58, 0xFF, 0x05, 0xDB, 0xFF, 0xEF, 0xF9, 0x00, 0xFF, 0x58, 0x05, 0xFF, 0x10, 0x1B, 0xFF, 0x12, 0x60, 0xFF, 0x15, 0xC2, 0xE2, 0x1F, 0xFF, 0x64, 0x42, 0xFF],
        [0x18, 0x58, 0xFF, 0x16, 0x64, 0xFF, 0x00, 0xFF, 0x88, 0xF8, 0xB0, 0x00, 0xFF, 0x2A, 0x0C, 0xFF, 0x12, 0x51, 0xFF, 0x14, 0x96, 0xFF, 0x17, 0xF8, 0xBA, 0x2B, 0xFF, 0x3C, 0x4D, 0xFF],
        [0x18, 0x58, 0xFF, 0x0E, 0x9C, 0xFF, 0x4C, 0xFF, 0x00, 0xFF, 0x71, 0x02, 0xFF, 0x10, 0x24, 0xFF, 0x13, 0x86, 0xFF, 0x16, 0xCC, 0xDC, 0x21, 0xFF, 0x91, 0x36, 0xFF, 0x18, 0x58, 0xFF],
    ];

    private static readonly StudioColorStop[] SynapseSpectrumStops =
    [
        new(0, new StudioRgb(0xFF, 0x18, 0xFF)),
        new(18, new StudioRgb(0xFF, 0x10, 0x10)),
        new(28, new StudioRgb(0xFF, 0x80, 0x00)),
        new(35, new StudioRgb(0xEF, 0xFF, 0x00)),
        new(42, new StudioRgb(0xDF, 0xFF, 0x00)),
        new(49, new StudioRgb(0x00, 0xFF, 0x00)),
        new(52, new StudioRgb(0x00, 0xFF, 0xFF)),
        new(64, new StudioRgb(0x18, 0x58, 0xFF)),
        new(77, new StudioRgb(0x18, 0x58, 0xFF)),
        new(100, new StudioRgb(0xFF, 0x18, 0xFF)),
    ];

    /// <summary>
    /// Mirror of Synapse 4's effect payloads for Mako X, taken from the lighting
    /// engine log (<c>RzLightingEngine.addEffect</c>): same palettes, durations and
    /// rates, so the strip animates like the Razer software instead of approximating it.
    /// </summary>
    internal static StudioEffectParams CreateSoftwareEffectParameters(
        RazerChromaEffect effect,
        Color primary,
        Color secondary,
        int directionIndex)
    {
        var primaryRgb = new StudioRgb(primary.R, primary.G, primary.B);
        var secondaryRgb = new StudioRgb(secondary.R, secondary.G, secondary.B);
        var parameters = new StudioEffectParams
        {
            Primary = primaryRgb,
            Secondary = secondaryRgb,
        };

        switch (effect)
        {
            case RazerChromaEffect.Static:
                parameters.Effect = StudioEffectKind.Solid;
                break;
            case RazerChromaEffect.Breathing:
                // 7000 ms: 0/25 black, 50 primary, 75 second colour, 100 black.
                parameters.Effect = StudioEffectKind.Breathing;
                parameters.DurationMs = 7_000;
                parameters.ColorStops.Add(new StudioColorStop(0, StudioRgb.Black));
                parameters.ColorStops.Add(new StudioColorStop(25, StudioRgb.Black));
                parameters.ColorStops.Add(new StudioColorStop(50, primaryRgb));
                parameters.ColorStops.Add(new StudioColorStop(
                    75, secondaryRgb == primaryRgb ? primaryRgb : secondaryRgb));
                parameters.ColorStops.Add(new StudioColorStop(100, StudioRgb.Black));
                break;
            case RazerChromaEffect.Spectrum:
                // 37.74 s rainbow palette from the engine's spectrum effect.
                parameters.Effect = StudioEffectKind.Spectrum;
                parameters.DurationMs = 37_740;
                parameters.ColorStops.AddRange(SynapseSpectrumStops);
                break;
            case RazerChromaEffect.Starlight:
                // 1700 ms life, 20 % star density, palette peaks on the page colour.
                parameters.Effect = StudioEffectKind.Starlight;
                parameters.DurationMs = 1_700;
                parameters.Density = 2;
                parameters.ColorStops.Add(new StudioColorStop(0, StudioRgb.Black));
                parameters.ColorStops.Add(new StudioColorStop(33, primaryRgb));
                parameters.ColorStops.Add(new StudioColorStop(67, StudioRgb.Black));
                parameters.ColorStops.Add(new StudioColorStop(100, StudioRgb.Black));
                break;
            case RazerChromaEffect.Fire:
                // Native warm palette: orange, then red, fading to black.
                parameters.Effect = StudioEffectKind.Fire;
                parameters.ColorStops.Add(new StudioColorStop(0, new StudioRgb(0xFF, 0x7F, 0x00)));
                parameters.ColorStops.Add(new StudioColorStop(50, new StudioRgb(0xFF, 0x00, 0x00)));
                parameters.ColorStops.Add(new StudioColorStop(100, StudioRgb.Black));
                parameters.FireRate = 160;
                parameters.FireSeed = 100;
                break;
            case RazerChromaEffect.AudioMeter:
                // Fixed green → yellow → red metre, decay 1, boost 1.
                parameters.Effect = StudioEffectKind.Audio;
                parameters.Decay = 1;
                parameters.Boost = 1;
                parameters.ColorStops.Add(new StudioColorStop(0, new StudioRgb(0x00, 0xFF, 0x00)));
                parameters.ColorStops.Add(new StudioColorStop(50, new StudioRgb(0xFF, 0xFF, 0x00)));
                parameters.ColorStops.Add(new StudioColorStop(100, new StudioRgb(0xFF, 0x00, 0x00)));
                break;
            case RazerChromaEffect.Wheel:
                // Rotation: 360 deg/s, rainbow wheel, centre (3,3) like the engine.
                parameters.Effect = StudioEffectKind.Wheel;
                parameters.Speed = 360;
                parameters.CenterRow = 3;
                parameters.CenterCol = 3;
                parameters.Counterclockwise =
                    MakoXWheelDirections[Math.Clamp(directionIndex, 0, MakoXWheelDirections.Length - 1)]
                        .Counterclockwise;
                parameters.ColorStops.AddRange(SynapseSpectrumStops);
                break;
            case RazerChromaEffect.CustomFrame:
                // Ambient Awareness: full-screen sample, blur 5.
                parameters = StudioEffectCatalog.CreateDefault(StudioEffectKind.Ambient);
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(effect), effect, "This effect is not rendered as a frame.");
        }

        return parameters;
    }

    /// <summary>Renders one frame for the Mako X 1 x 10 strip.</summary>
    internal static byte[] RenderSoftwareEffectFrame(
        StudioEffectParams parameters,
        StudioEffectInput input,
        double seconds)
    {
        var colors = StudioEffectRenderer.Render(
            parameters, 1, RazerMakoXChromaService.LedCount, seconds, input);
        var frame = new byte[colors.Length * 3];
        for (var index = 0; index < colors.Length; index++)
        {
            frame[index * 3] = colors[index].R;
            frame[(index * 3) + 1] = colors[index].G;
            frame[(index * 3) + 2] = colors[index].B;
        }

        return frame;
    }

    private async Task<bool> RunSoftwareEffectAsync(
        RazerChromaEffect effect,
        RazerChromaEffect fallbackEffect,
        Color fallbackPrimary,
        Color fallbackSecondary,
        int fallbackDirection,
        CancellationToken cancellationToken)
    {
        if (!BeginOperation()) return false;
        try
        {
            await StartSoftwareEffectCoreAsync(
                effect, PrimaryColor, SecondaryColor, SelectedWaveDirectionIndex, cancellationToken)
                .ConfigureAwait(true);
            ErrorText = string.Empty;
            OperationStatusText = AppStrings.Text("BlackSharkSaved");
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            ErrorText = exception.Message;
            OperationStatusText = string.Empty;
            if (fallbackEffect != RazerChromaEffect.CustomFrame)
            {
                try
                {
                    await RestoreEffectAsync(
                        fallbackEffect, fallbackPrimary, fallbackSecondary, fallbackDirection, CancellationToken.None)
                        .ConfigureAwait(true);
                }
                catch (Exception restore) when (IsExpectedException(restore))
                {
                    _diagnostic?.Invoke($"mako-x effect restore failed: {restore.Message}");
                }
            }

            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task StartSoftwareEffectCoreAsync(
        RazerChromaEffect effect,
        Color primary,
        Color secondary,
        int directionIndex,
        CancellationToken cancellationToken)
    {
        // Keep the frame that is on the strip so the new effect can blend from it.
        var crossfadeFrom = _softwareLastFrame;
        await StopSoftwareEffectCoreAsync().ConfigureAwait(true);

        var parameters = CreateSoftwareEffectParameters(effect, primary, secondary, directionIndex);
        // Fire and Starlight are pure functions of time; only Audio Meter and Ambient
        // need live input, so those are the only effects that open capture.
        var sampler = effect is RazerChromaEffect.AudioMeter or RazerChromaEffect.CustomFrame
            ? new StudioInputSampler()
            : null;
        var service = new RazerMakoXChromaService(
            _connection.DevicePath, _diagnostic, keepOpen: true);
        try
        {
            await service.SetCustomFrameEffectAsync(cancellationToken).ConfigureAwait(true);
            if (sampler is not null)
            {
                await sampler.StartAsync(cancellationToken).ConfigureAwait(true);
                if (effect == RazerChromaEffect.CustomFrame)
                {
                    // Graphics Capture delivers the first frame asynchronously, so
                    // wait for it instead of assuming one fixed delay is enough.
                    var deadline = Stopwatch.GetTimestamp() +
                        (long)(Stopwatch.Frequency * AmbientCaptureTimeout.TotalSeconds);
                    while (!sampler.HasScreenFrame && Stopwatch.GetTimestamp() < deadline)
                    {
                        await Task.Delay(50, cancellationToken).ConfigureAwait(true);
                    }

                    if (!sampler.HasScreenFrame)
                    {
                        throw new InvalidOperationException("Screen capture is unavailable.");
                    }
                }
            }

            // Write the first frame here so a dead channel fails the click, not the pump.
            // When a fade is running the strip already shows the old frame, so the
            // pump blends the first two ticks and this write only validates the channel.
            var firstInput = ReadSoftwareEffectInput(sampler);
            var first = parameters.Effect switch
            {
                StudioEffectKind.Wheel => RenderWheelCycleFrame(parameters, 0),
                StudioEffectKind.Audio => RenderAudioMeterFrame(parameters, firstInput.AudioLevel),
                _ => RenderSoftwareEffectFrame(parameters, firstInput, 0),
            };
            await service.SetFrameAsync(first, cancellationToken).ConfigureAwait(true);
            _softwareLastFrame = first;
        }
        catch
        {
            if (sampler is not null)
            {
                try
                {
                    await sampler.DisposeAsync().ConfigureAwait(true);
                }
                catch (Exception exception) when (IsExpectedException(exception))
                {
                    _diagnostic?.Invoke($"mako-x effect capture dispose failed: {exception.Message}");
                }
            }

            try
            {
                await service.DisposeAsync().ConfigureAwait(true);
            }
            catch (Exception exception) when (IsExpectedException(exception))
            {
                _diagnostic?.Invoke($"mako-x effect device dispose failed: {exception.Message}");
            }

            throw;
        }

        var stop = new CancellationTokenSource();
        _softwareSampler = sampler;
        _softwareService = service;
        _softwareCancellation = stop;
        _diagnostic?.Invoke(
            $"mako-x effect pump: {effect} at {SoftwareEffectFramesPerSecond:0} fps");
        _softwarePump = Task.Run(
            () => PumpSoftwareEffectAsync(
                parameters, sampler, service, crossfadeFrom, Stopwatch.GetTimestamp(), stop.Token),
            CancellationToken.None);
    }

    private static StudioEffectInput ReadSoftwareEffectInput(StudioInputSampler? sampler) =>
        sampler?.Sample(1, RazerMakoXChromaService.LedCount) ?? default;

    private static byte[] RenderWheelCycleFrame(StudioEffectParams parameters, double seconds)
    {
        var tick = (int)Math.Floor(Math.Max(0, seconds) * SoftwareEffectFramesPerSecond);
        var index = (tick % WheelCycleFrames.Length + WheelCycleFrames.Length) % WheelCycleFrames.Length;
        if (parameters.Counterclockwise)
        {
            index = (WheelCycleFrames.Length - index) % WheelCycleFrames.Length;
        }

        return (byte[])WheelCycleFrames[index].Clone();
    }

    /// <summary>320 ms linear ramp to black, matching Synapse's effect swap.</summary>
    private static async Task FadeOutAsync(
        RazerMakoXChromaService service,
        byte[] frame,
        CancellationToken cancellationToken)
    {
        const int steps = 8;
        var faded = new byte[frame.Length];
        for (var step = 1; step <= steps; step++)
        {
            var scale = 1 - (step / (double)steps);
            for (var index = 0; index < frame.Length; index++)
            {
                faded[index] = (byte)(frame[index] * scale);
            }

            await service.SetFrameAsync(faded, cancellationToken).ConfigureAwait(false);
            await Task.Delay(40, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Mako X's Audio Meter paints the whole strip with one palette colour chosen
    /// by the current level (green at silence through yellow to red at full
    /// scale) and turns the strip off below the noise floor. Captured from
    /// Synapse: every frame is uniform, only the colour changes.
    /// </summary>
    private static byte[] RenderAudioMeterFrame(StudioEffectParams parameters, double level)
    {
        var frame = new byte[RazerMakoXChromaService.LedCount * 3];
        var adjusted = double.IsFinite(level) ? Math.Clamp(level, 0, 1) : 0;
        if (adjusted < AudioMeterSilenceLevel)
        {
            return frame;
        }

        var color = parameters.Resolve(adjusted);
        for (var index = 0; index < RazerMakoXChromaService.LedCount; index++)
        {
            frame[index * 3] = color.R;
            frame[(index * 3) + 1] = color.G;
            frame[(index * 3) + 2] = color.B;
        }

        return frame;
    }

    private async Task StopSoftwareEffectCoreAsync(bool fadeOut = false)
    {
        var stop = _softwareCancellation;
        var pump = _softwarePump;
        var sampler = _softwareSampler;
        var service = _softwareService;
        _softwareCancellation = null;
        _softwarePump = null;
        _softwareSampler = null;
        _softwareService = null;

        stop?.Cancel();
        if (pump is not null)
        {
            try
            {
                await pump.ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
            }
        }

        if (fadeOut && service is not null && _softwareLastFrame is { } lastFrame)
        {
            try
            {
                // Leaving the frame pump for a firmware effect has no source frames
                // to blend, so ramp the last frame down instead.
                await FadeOutAsync(service, lastFrame, CancellationToken.None).ConfigureAwait(true);
            }
            catch (Exception exception) when (IsExpectedException(exception))
            {
                _diagnostic?.Invoke($"mako-x effect fade-out failed: {exception.Message}");
            }
        }

        if (sampler is not null)
        {
            try
            {
                await sampler.DisposeAsync().ConfigureAwait(true);
            }
            catch (Exception exception) when (IsExpectedException(exception))
            {
                _diagnostic?.Invoke($"mako-x effect capture stop failed: {exception.Message}");
            }
        }

        if (service is not null)
        {
            try
            {
                await service.DisposeAsync().ConfigureAwait(true);
            }
            catch (Exception exception) when (IsExpectedException(exception))
            {
                _diagnostic?.Invoke($"mako-x effect device stop failed: {exception.Message}");
            }
        }

        stop?.Dispose();
    }

    private Task RestoreEffectAsync(
        RazerChromaEffect effect,
        Color primary,
        Color secondary,
        int directionIndex,
        CancellationToken cancellationToken) =>
        // Routes to the frame pump for software effects and to class 0x0F otherwise.
        WriteChromaEffectHardwareAsync(effect, primary, secondary, directionIndex, cancellationToken);

    private async Task PumpSoftwareEffectAsync(
        StudioEffectParams parameters,
        StudioInputSampler? sampler,
        RazerMakoXChromaService service,
        byte[]? crossfadeFrom,
        long startedAt,
        CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(SoftwareEffectFrameInterval);
        byte[]? previous = null;
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                var seconds = Stopwatch.GetElapsedTime(startedAt).TotalSeconds;
                var input = ReadSoftwareEffectInput(sampler);
                var frame = parameters.Effect switch
                {
                    StudioEffectKind.Wheel => RenderWheelCycleFrame(parameters, seconds),
                    StudioEffectKind.Audio => RenderAudioMeterFrame(parameters, input.AudioLevel),
                    _ => RenderSoftwareEffectFrame(parameters, input, seconds),
                };
                if (crossfadeFrom is not null && seconds < CrossfadeSeconds)
                {
                    var alpha = seconds / CrossfadeSeconds;
                    for (var index = 0; index < frame.Length; index++)
                    {
                        frame[index] = (byte)((crossfadeFrom[index] * (1 - alpha)) + (frame[index] * alpha));
                    }
                }
                // Breathing and Starlight spend whole phases on the same colour; the
                // device keeps the last frame, so repeat writes only waste HID time.
                if (previous is not null && frame.AsSpan().SequenceEqual(previous))
                {
                    continue;
                }

                await service.SetFrameAsync(frame, cancellationToken).ConfigureAwait(false);
                previous = frame;
                _softwareLastFrame = frame;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            _diagnostic?.Invoke($"mako-x effect stopped: {exception.Message}");
            if (_dispatcher is { } dispatcher)
            {
                dispatcher.TryEnqueue(() =>
                {
                    ErrorText = exception.Message;
                    OperationStatusText = string.Empty;
                });
            }
        }
    }
}
