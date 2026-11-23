using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using Windows.UI;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHeadsetAudioViewModel
{
    private RazerHeadsetAudioService OpenService() =>
        new(new RazerFeatureTransport(), _connection.DevicePath,
            IsKrakenBase ? TimeSpan.FromMilliseconds(1000) : TimeSpan.FromMilliseconds(20),
            _diagnostic, _connection.Transport, _connection.ProductId,
            outputReportByteLength: _connection.OutputReportByteLength);

    private RazerMxicHeadsetService OpenMxicService() =>
        new(_connection.ProductId, _connection.DevicePath, _diagnostic);

    private RazerAvneraHeadsetService OpenAvneraService() =>
        new(_connection.DevicePath, _diagnostic, _connection.FeatureReportByteLength);

    private async Task<RazerHeadsetAudioState> ReadAvneraStateAsync(CancellationToken cancellationToken)
    {
        await using var service = OpenAvneraService();
        return await service.ReadStateAsync(cancellationToken);
    }

    private Task<bool> RunAvneraWriteAsync(
        Func<RazerAvneraHeadsetService, CancellationToken, Task> write,
        CancellationToken cancellationToken) =>
        RunWriteCoreAsync(async token =>
        {
            await using var service = OpenAvneraService();
            await write(service, token);
        }, cancellationToken);

    private async Task<RazerHeadsetAudioState> ReadMxicStateAsync(CancellationToken cancellationToken)
    {
        // Cancel the read itself at the deadline; WaitAsync only abandoned the task and
        // disposed the transport while the native calls were still running.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(MxicReadTimeout);
        var service = OpenMxicService();
        try
        {
            return await service.ReadStateAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The transport cancels the native HID call on this token, so the service
            // is safe to dispose instead of leaving the card in its reading state.
            throw new IOException("Headset read timed out.");
        }
        finally
        {
            await service.DisposeAsync().ConfigureAwait(false);
        }
    }

    private RazerValkyrieHeadsetService OpenValkyrieService() =>
        new(_connection.ProductId, _connection.DevicePath, _diagnostic);

    private async Task<RazerHeadsetAudioState> ReadValkyrieStateAsync(CancellationToken cancellationToken)
    {
        await using var service = OpenValkyrieService();
        return await service.ReadStateAsync(cancellationToken);
    }

    private IRazerHeadsetLightingService OpenChromaService() =>
        IsJolie
            ? new RazerMakoXChromaService(_connection.DevicePath, _diagnostic)
            : new RazerChromaStandardService(
                new RazerFeatureTransport(), _connection.DevicePath, TimeSpan.FromMilliseconds(20), _diagnostic);

    private async Task RunChromaWriteAsync(
        Func<IRazerHeadsetLightingService, CancellationToken, Task> write,
        CancellationToken cancellationToken)
    {
        if (!BeginOperation()) return;
        try
        {
            await write(OpenChromaService(), cancellationToken);
            ErrorText = string.Empty;
            OperationStatusText = AppStrings.Text("BlackSharkSaved");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            ErrorText = exception.Message;
            OperationStatusText = string.Empty;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyMatrixLighting(RazerChromaStandardState state)
    {
        if (state.BrightnessPercent is byte brightness)
        {
            Brightness = brightness;
            _hasLighting = true;
        }

        if (state.Effect is RazerChromaEffect effect && LightingEffects.Contains(effect))
        {
            SelectedLightingEffectIndex = Array.IndexOf(LightingEffects, effect);
            _hasLighting = true;
        }

        if (state.Colors.Count >= 3)
        {
            PrimaryColor = Color.FromArgb(255, state.Colors[0], state.Colors[1], state.Colors[2]);
        }

        if (state.Colors.Count >= 6)
        {
            SecondaryColor = Color.FromArgb(255, state.Colors[3], state.Colors[4], state.Colors[5]);
        }

        OnPropertyChanged(nameof(MatrixLightingVisibility));
    }
    private Task<bool> RunWriteAsync(
        Func<RazerHeadsetAudioService, CancellationToken, Task> write,
        CancellationToken cancellationToken) =>
        RunWriteCoreAsync(token => write(OpenService(), token), cancellationToken);

    private Task<bool> RunMxicWriteAsync(
        Func<RazerMxicHeadsetService, CancellationToken, Task> write,
        CancellationToken cancellationToken) =>
        RunWriteCoreAsync(async token =>
        {
            await using var service = OpenMxicService();
            await write(service, token);
        }, cancellationToken);

    private Task<bool> RunValkyrieWriteAsync(
        Func<RazerValkyrieHeadsetService, CancellationToken, Task> write,
        CancellationToken cancellationToken) =>
        RunWriteCoreAsync(async token =>
        {
            await using var service = OpenValkyrieService();
            await write(service, token);
        }, cancellationToken);

    private async Task<bool> RunWriteCoreAsync(
        Func<CancellationToken, Task> write,
        CancellationToken cancellationToken)
    {
        if (!BeginOperation()) return false;
        try
        {
            await write(cancellationToken);
            ErrorText = string.Empty;
            OperationStatusText = AppStrings.Text("BlackSharkSaved");
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            ErrorText = exception.Message;
            OperationStatusText = string.Empty;
        }
        finally
        {
            IsBusy = false;
        }

        return false;
    }
}
