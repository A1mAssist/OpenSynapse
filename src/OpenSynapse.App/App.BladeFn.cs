using Microsoft.UI.Xaml;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using OpenSynapse.App.Runtime;
using OpenSynapse.App.ViewModels;
using OpenSynapse.Core.Diagnostics;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Core.Storage;
using OpenSynapse.Windows.Audio;
using OpenSynapse.Windows.Displays;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Input;
using OpenSynapse.Windows.Lifecycle;
using OpenSynapse.Windows.Lighting;
using OpenSynapse.Windows.Protocols;
using OpenSynapse.Windows.Sensors;
using Velopack;
using Velopack.Locators;
namespace OpenSynapse.App;

public partial class App
{
    private ValueTask ExecuteBladeFnLeafAsync(
        BladeMappingAction action,
        int generation,
        CancellationToken cancellationToken)
    {
        var dispatcher = _window?.DispatcherQueue
            ?? throw new InvalidOperationException("Blade Fn UI dispatcher is unavailable.");
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!dispatcher.TryEnqueue(async () =>
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await ExecuteBladeFnLeafOnUiThreadAsync(
                        action,
                        generation,
                        cancellationToken).ConfigureAwait(true);
                    completion.TrySetResult();
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    completion.TrySetCanceled(cancellationToken);
                }
                catch (Exception exception)
                {
                    completion.TrySetException(exception);
                }
            }))
        {
            throw new InvalidOperationException("Blade Fn action could not enter the UI dispatcher.");
        }

        return new ValueTask(completion.Task);
    }

    private async Task ExecuteBladeFnLeafOnUiThreadAsync(
        BladeMappingAction action,
        int generation,
        CancellationToken cancellationToken)
    {
        var viewModel = _audioMuteViewModel;
        if (viewModel is null ||
            Volatile.Read(ref _closing) != 0 ||
            generation != Volatile.Read(ref _audioMuteGeneration))
        {
            throw new OperationCanceledException("Blade Fn session is no longer current.");
        }

        switch (action)
        {
            case BladeCommandMappingAction
            {
                CommandKind: BladeMappingOutputKind.GameMode,
                Command: BladeMappingCommand.Toggle,
            }:
                await viewModel.Blade.ToggleGamingModeAsync(cancellationToken).ConfigureAwait(true);
                break;
            case BladeCommandMappingAction
            {
                CommandKind: BladeMappingOutputKind.BladePerformance,
                Command: BladeMappingCommand.NextPerformanceMode,
            }:
                await viewModel.Blade.CyclePerformanceModeAsync(cancellationToken).ConfigureAwait(true);
                break;
            case BladeCommandMappingAction
            {
                CommandKind: BladeMappingOutputKind.BladeTrackpad,
                Command: BladeMappingCommand.Toggle,
            }:
                await viewModel.Blade.ToggleTouchpadAsync(cancellationToken).ConfigureAwait(true);
                break;
            case BladeCommandMappingAction
            {
                CommandKind: BladeMappingOutputKind.BladeBattery,
                Command: BladeMappingCommand.Toggle,
            }:
                await viewModel.Blade.ToggleOneTimeFullChargeAsync(cancellationToken).ConfigureAwait(true);
                break;
            case BladeCommandMappingAction
            {
                CommandKind: BladeMappingOutputKind.ScreenRefresh,
                Command: BladeMappingCommand.NextRefreshRate,
            }:
                await viewModel.Blade.CycleInternalDisplayRefreshRateAsync(cancellationToken).ConfigureAwait(true);
                break;
            case BladeBacklightMappingAction
            {
                IsDown: true,
                Command: BladeMappingCommand.BrightnessDown,
            }:
                try
                {
                    await viewModel.Blade.StepBrightnessAsync(false, cancellationToken).ConfigureAwait(true);
                }
                catch (Exception exception) when (
                    exception is System.ComponentModel.Win32Exception or
                    System.Runtime.InteropServices.COMException or
                    InvalidOperationException)
                {
                    _diagnosticLog.TryWrite("blade-fn", $"Keyboard backlight step was rejected: {exception.Message}");
                }
                break;
            case BladeBacklightMappingAction
            {
                IsDown: true,
                Command: BladeMappingCommand.BrightnessUp,
            }:
                try
                {
                    await viewModel.Blade.StepBrightnessAsync(true, cancellationToken).ConfigureAwait(true);
                }
                catch (Exception exception) when (
                    exception is System.ComponentModel.Win32Exception or
                    System.Runtime.InteropServices.COMException or
                    InvalidOperationException)
                {
                    _diagnosticLog.TryWrite("blade-fn", $"Keyboard backlight step was rejected: {exception.Message}");
                }
                break;
            case BladeBacklightMappingAction { IsDown: false }:
                break;
            case BladeAudioMappingAction
            {
                Command: BladeMappingCommand.Microphone,
                Mute: 2,
            }:
                await Task.Run(
                    WindowsCoreAudioMuteEventSource.ToggleDefaultCaptureMute,
                    cancellationToken).ConfigureAwait(true);
                break;
            default:
                throw new InvalidOperationException(
                    $"Unsupported Blade Fn leaf action: {action}.");
        }
    }

    private async Task ObserveBladeFnRuntimeAsync(
        BladeFnRuntime? runtime,
        string devicePath,
        ushort productId,
        int generation)
    {
        if (runtime is null)
        {
            return;
        }

        try
        {
            await runtime.Completion.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            if (Volatile.Read(ref _closing) != 0 ||
                generation != Volatile.Read(ref _audioMuteGeneration))
            {
                return;
            }

            _diagnosticLog.TryWrite("blade-fn", $"runtime failed closed: {exception}");
            await _audioMuteRuntimeGate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (ReferenceEquals(_bladeFnRuntime, runtime))
                {
                    try
                    {
                        await DisposeBladeAudioStackAsync().ConfigureAwait(false);
                    }
                    catch (Exception cleanupError)
                    {
                        _diagnosticLog.TryWrite("blade-fn", $"fault cleanup failed: {cleanupError}");
                        ReportBladeFnFailure(cleanupError);
                    }
                }
            }
            finally
            {
                _audioMuteRuntimeGate.Release();
            }

            if (Volatile.Read(ref _closing) == 0 &&
                generation == Volatile.Read(ref _audioMuteGeneration))
            {
                _ = RetryBladeAudioMuteRuntimeAsync(devicePath, productId, generation);
            }
        }
    }

    private void ReportBladeFnFailure(Exception exception)
    {
        var dispatcher = _window?.DispatcherQueue;
        var viewModel = _audioMuteViewModel;
        if (dispatcher is null || viewModel is null)
        {
            return;
        }

        dispatcher.TryEnqueue(() => viewModel.ReportApplicationError(
            AppStrings.FormatText("FnKeyError", exception.Message)));
    }

    private void SetBladeFilterDriverMissing(bool missing)
    {
        var dispatcher = _window?.DispatcherQueue;
        var viewModel = _audioMuteViewModel;
        if (dispatcher is null || viewModel is null)
        {
            return;
        }

        dispatcher.TryEnqueue(() => viewModel.Blade.SetFilterDriverMissing(missing));
    }

    private async Task RetryBladeAudioMuteRuntimeAsync(
        string devicePath,
        ushort productId,
        int generation)
    {
        await Task.Delay(AudioMuteRetryDelay).ConfigureAwait(false);
        if (Volatile.Read(ref _closing) == 0 &&
            generation == Volatile.Read(ref _audioMuteGeneration))
        {
            await SwitchBladeAudioMuteRuntimeAsync(devicePath, productId, generation)
                .ConfigureAwait(false);
        }
    }
}
