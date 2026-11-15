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

    /// <summary>Longest wait for the first Graphics Capture frame before Ambient fails.</summary>
    private static readonly TimeSpan AmbientCaptureTimeout = TimeSpan.FromSeconds(3);

    private readonly DispatcherQueue? _dispatcher = DispatcherQueue.GetForCurrentThread();
    private StudioInputSampler? _softwareSampler;
    private RazerMakoXChromaService? _softwareService;
    private CancellationTokenSource? _softwareCancellation;
    private Task? _softwarePump;

    /// <summary>
    /// Effects the Mako X firmware does not implement. 1532:05AD only accepts
    /// Off/Static/Breathing/Spectrum/Wave/CustomFrame and answers status 0x05 with
    /// a dark strip for anything else, so these are rendered as per-LED frames.
    /// </summary>
    internal static bool IsSoftwareRenderedEffect(RazerChromaEffect effect) =>
        effect is RazerChromaEffect.Starlight or RazerChromaEffect.Fire or
            RazerChromaEffect.AudioMeter or RazerChromaEffect.CustomFrame;

    internal static StudioEffectParams CreateSoftwareEffectParameters(
        RazerChromaEffect effect,
        Color primary,
        Color secondary)
    {
        var kind = effect switch
        {
            RazerChromaEffect.Starlight => StudioEffectKind.Starlight,
            RazerChromaEffect.Fire => StudioEffectKind.Fire,
            RazerChromaEffect.AudioMeter => StudioEffectKind.Audio,
            RazerChromaEffect.CustomFrame => StudioEffectKind.Ambient,
            _ => throw new ArgumentOutOfRangeException(
                nameof(effect), effect, "This effect is not rendered as a frame."),
        };

        // Chroma Studio's own defaults supply the native 7x23 fire palette and the
        // fixed audio-meter gradient; only Starlight follows the page's two colours.
        var parameters = StudioEffectCatalog.CreateDefault(kind);
        if (effect == RazerChromaEffect.Starlight)
        {
            parameters.ColorStops.Clear();
            parameters.Primary = new StudioRgb(primary.R, primary.G, primary.B);
            parameters.Secondary = new StudioRgb(secondary.R, secondary.G, secondary.B);
            parameters.RandomColor = false;
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
        CancellationToken cancellationToken)
    {
        if (!BeginOperation()) return false;
        try
        {
            await StartSoftwareEffectCoreAsync(
                effect, PrimaryColor, SecondaryColor, cancellationToken).ConfigureAwait(true);
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
                        fallbackEffect, fallbackPrimary, fallbackSecondary, CancellationToken.None)
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
        CancellationToken cancellationToken)
    {
        await StopSoftwareEffectCoreAsync().ConfigureAwait(true);

        var parameters = CreateSoftwareEffectParameters(effect, primary, secondary);
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
            var first = RenderSoftwareEffectFrame(
                parameters, ReadSoftwareEffectInput(sampler), 0);
            await service.SetFrameAsync(first, cancellationToken).ConfigureAwait(true);
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
                parameters, sampler, service, Stopwatch.GetTimestamp(), stop.Token),
            CancellationToken.None);
    }

    private static StudioEffectInput ReadSoftwareEffectInput(StudioInputSampler? sampler) =>
        sampler?.Sample(1, RazerMakoXChromaService.LedCount) ?? default;

    private async Task StopSoftwareEffectCoreAsync()
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
        CancellationToken cancellationToken) =>
        // Routes to the frame pump for software effects and to class 0x0F otherwise.
        WriteChromaEffectHardwareAsync(effect, primary, secondary, cancellationToken);

    private async Task PumpSoftwareEffectAsync(
        StudioEffectParams parameters,
        StudioInputSampler? sampler,
        RazerMakoXChromaService service,
        long startedAt,
        CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(SoftwareEffectFrameInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                var seconds = Stopwatch.GetElapsedTime(startedAt).TotalSeconds;
                var frame = RenderSoftwareEffectFrame(
                    parameters, ReadSoftwareEffectInput(sampler), seconds);
                await service.SetFrameAsync(frame, cancellationToken).ConfigureAwait(false);
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
