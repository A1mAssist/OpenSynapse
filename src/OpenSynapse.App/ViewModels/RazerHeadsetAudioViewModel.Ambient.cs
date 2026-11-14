using Microsoft.UI.Dispatching;
using OpenSynapse.Core.Lighting;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Lighting;
using OpenSynapse.Windows.Protocols;
using Windows.UI;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHeadsetAudioViewModel
{
    private static readonly StudioEffectParams AmbientParameters = new()
    {
        Effect = StudioEffectKind.Ambient,
        Blur = 5,
    };

    private readonly DispatcherQueue? _dispatcher = DispatcherQueue.GetForCurrentThread();
    private StudioInputSampler? _ambientSampler;
    private RazerMakoXChromaService? _ambientService;
    private CancellationTokenSource? _ambientCancellation;
    private Task? _ambientPump;

    private async Task<bool> RunAmbientAsync(
        RazerChromaEffect fallbackEffect,
        Color fallbackPrimary,
        Color fallbackSecondary,
        CancellationToken cancellationToken)
    {
        if (!BeginOperation()) return false;
        try
        {
            await StartAmbientCoreAsync(cancellationToken).ConfigureAwait(true);
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
                    _diagnostic?.Invoke($"mako-x ambient restore failed: {restore.Message}");
                }
            }

            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task StartAmbientCoreAsync(CancellationToken cancellationToken)
    {
        await StopAmbientCoreAsync().ConfigureAwait(true);

        var sampler = new StudioInputSampler();
        var service = new RazerMakoXChromaService(
            _connection.DevicePath, _diagnostic, keepOpen: true);
        try
        {
            await service.SetCustomFrameEffectAsync(cancellationToken).ConfigureAwait(true);
            await sampler.StartAsync(cancellationToken).ConfigureAwait(true);
            await Task.Delay(300, cancellationToken).ConfigureAwait(true);
            if (!sampler.HasScreenFrame)
            {
                throw new InvalidOperationException("Screen capture is unavailable.");
            }
        }
        catch
        {
            try
            {
                await sampler.DisposeAsync().ConfigureAwait(true);
            }
            catch (Exception exception) when (IsExpectedException(exception))
            {
                _diagnostic?.Invoke($"mako-x ambient capture dispose failed: {exception.Message}");
            }

            try
            {
                await service.DisposeAsync().ConfigureAwait(true);
            }
            catch (Exception exception) when (IsExpectedException(exception))
            {
                _diagnostic?.Invoke($"mako-x ambient device dispose failed: {exception.Message}");
            }

            throw;
        }

        var stop = new CancellationTokenSource();
        _ambientSampler = sampler;
        _ambientService = service;
        _ambientCancellation = stop;
        _ambientPump = Task.Run(
            () => PumpAmbientAsync(sampler, service, stop.Token),
            CancellationToken.None);
    }

    private async Task StopAmbientCoreAsync()
    {
        var stop = _ambientCancellation;
        var pump = _ambientPump;
        var sampler = _ambientSampler;
        var service = _ambientService;
        _ambientCancellation = null;
        _ambientPump = null;
        _ambientSampler = null;
        _ambientService = null;

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
                _diagnostic?.Invoke($"mako-x ambient capture stop failed: {exception.Message}");
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
                _diagnostic?.Invoke($"mako-x ambient device stop failed: {exception.Message}");
            }
        }

        stop?.Dispose();
    }

    private async Task RestoreEffectAsync(
        RazerChromaEffect effect,
        Color primary,
        Color secondary,
        CancellationToken cancellationToken)
    {
        if (effect == RazerChromaEffect.CustomFrame)
        {
            await StartAmbientCoreAsync(cancellationToken).ConfigureAwait(true);
            return;
        }

        await StopAmbientCoreAsync().ConfigureAwait(true);
        await WriteChromaEffectHardwareAsync(effect, primary, secondary, cancellationToken)
            .ConfigureAwait(true);
    }

    private async Task PumpAmbientAsync(
        StudioInputSampler sampler,
        RazerMakoXChromaService service,
        CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(
            TimeSpan.FromSeconds(1d / StudioEffectRenderer.NativeFramesPerSecond));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                var input = sampler.Sample(1, RazerMakoXChromaService.LedCount);
                var colors = StudioEffectRenderer.Render(
                    AmbientParameters, 1, RazerMakoXChromaService.LedCount, 0, input);
                var frame = new byte[colors.Length * 3];
                for (var index = 0; index < colors.Length; index++)
                {
                    frame[index * 3] = colors[index].R;
                    frame[(index * 3) + 1] = colors[index].G;
                    frame[(index * 3) + 2] = colors[index].B;
                }

                await service.SetFrameAsync(frame, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            _diagnostic?.Invoke($"mako-x ambient stopped: {exception.Message}");
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
