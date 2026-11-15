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
    private async Task DisposeHardwareForExitAsync(MainViewModel viewModel)
    {
        var lightingController = _bladeLightingController;
        _bladeLightingController = null;
        if (lightingController is not null)
        {
            try
            {
                await lightingController.DisposeAsync();
            }
            catch (Exception exception)
            {
                _diagnosticLog.TryWrite("keyboard-lighting", $"restore failed: {exception}");
            }
        }

        var thxAudioController = _thxAudioController;
        _thxAudioController = null;
        if (thxAudioController is not null)
        {
            try
            {
                await thxAudioController.DisposeAsync();
            }
            catch (Exception exception)
            {
                _diagnosticLog.TryWrite("thx-audio", $"helper shutdown failed: {exception}");
            }
        }

        try
        {
            await viewModel.DisposeAsync();
        }
        catch (Exception exception)
        {
            _diagnosticLog.TryWrite("application", $"Fan recovery before exit failed: {exception}");
        }

        var macroSink = _razerFilterMacroSink;
        _razerFilterMacroSink = null;
        if (macroSink is not null)
        {
            try
            {
                await macroSink.DisposeAsync();
            }
            catch (Exception exception)
            {
                _diagnosticLog.TryWrite("macro", $"Driver macro sink shutdown failed: {exception.Message}");
            }
        }

        await _audioMuteRuntimeGate.WaitAsync();
        try
        {
            await DisposeBladeAudioStackAsync();
        }
        catch (Exception exception)
        {
            _diagnosticLog.TryWrite("audio-mute-sync", $"stop failed: {exception}");
        }
        finally
        {
            _audioMuteRuntimeGate.Release();
        }
    }

    private void OnBladeControlDeviceChanged(string? devicePath, ushort productId)
    {
        _activeBladeControlDevicePath = devicePath;
        _activeBladeProductId = productId;
        var generation = Interlocked.Increment(ref _audioMuteGeneration);
        _ = SwitchBladeAudioMuteRuntimeAsync(devicePath, productId, generation);
    }

    private void OnBladeInputProfileChanged()
    {
        var generation = Interlocked.Increment(ref _audioMuteGeneration);
        _ = SwitchBladeAudioMuteRuntimeAsync(
            _activeBladeControlDevicePath,
            _activeBladeProductId,
            generation);
    }

    private async Task SwitchBladeAudioMuteRuntimeAsync(
        string? devicePath,
        ushort productId,
        int generation)
    {
        await _audioMuteRuntimeGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _closing) != 0 ||
                generation != Volatile.Read(ref _audioMuteGeneration))
            {
                return;
            }

            await DisposeBladeAudioStackAsync().ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(devicePath) || _razerTransport is null)
            {
                return;
            }

            if (!BladeFnRuntime.HasMapping(productId))
            {
                _diagnosticLog.TryWrite(
                    "audio-mute-sync",
                    $"no Fn mapping for Product {productId}; the keyboard stays in firmware mode.");
                return;
            }

            BladeFnRuntime? fnRuntime = null;
            BladeAudioMuteRuntime? audioRuntime = null;
            try
            {
                fnRuntime = new BladeFnRuntime(
                    _razerTransport,
                    devicePath,
                    productId,
                    _bladeModeCoordinator,
                    (action, token) => ExecuteBladeFnLeafAsync(action, generation, token),
                    input => _bladeLightingController?.ObserveMappingInput(input),
                    _audioMuteViewModel?.Blade.ActiveMappingPreset ??
                        OpenSynapse.Core.Profiles.BladeProfileSettings.Product710DefaultMappingPreset,
                    _audioMuteViewModel?.Blade.ActiveSnapTapEnabled == true, enabled => PersistBladeSnapTap(enabled, generation),
                    enabled => _audioMuteViewModel?.Macros.SetHyperShiftHeld(enabled),
                    message => _diagnosticLog.TryWrite("blade-input", message));
                await fnRuntime.StartAsync().ConfigureAwait(false);
                fnRuntime.SetGameModeEnabled(_audioMuteViewModel?.Blade.GameModeEnabled == true);
                if (generation == Volatile.Read(ref _audioMuteGeneration))
                {
                    SetBladeFilterDriverMissing(false);
                }
                _bladeFnRuntime = fnRuntime;
                if (Volatile.Read(ref _closing) != 0 ||
                    generation != Volatile.Read(ref _audioMuteGeneration))
                {
                    throw new OperationCanceledException(AppStrings.Text(
                        "BladeFnRuntimeStoppedBeforeAudioSync"));
                }

                audioRuntime = new BladeAudioMuteRuntime(
                    _razerTransport,
                    devicePath,
                    _bladeModeCoordinator);
                audioRuntime.Synchronized += state => _diagnosticLog.TryWrite(
                    "audio-mute-sync",
                    $"{state.Target} indicator synchronized: muted={state.Muted}.");
                audioRuntime.SynchronizationFailed += exception => _diagnosticLog.TryWrite(
                    "audio-mute-sync",
                    $"audio indicator synchronization failed: {exception}");
                await audioRuntime.StartAsync(
                    Volatile.Read(ref _displayAvailable) != 0).ConfigureAwait(false);

                fnRuntime = null;
                _audioMuteRuntime = audioRuntime;
                audioRuntime = null;
                _diagnosticLog.TryWrite(
                    "audio-mute-sync",
                    "Blade Fn runtime and endpoint synchronization started without MappingEngine/AppEngine.");
                _ = ObserveBladeFnRuntimeAsync(
                    _bladeFnRuntime,
                    devicePath,
                    productId,
                    generation);
            }
            catch
            {
                if (audioRuntime is not null)
                {
                    await audioRuntime.DisposeAsync().ConfigureAwait(false);
                }
                if (fnRuntime is not null)
                {
                    if (ReferenceEquals(_bladeFnRuntime, fnRuntime))
                    {
                        _bladeFnRuntime = null;
                    }

                    await fnRuntime.DisposeAsync().ConfigureAwait(false);
                }
                throw;
            }
        }
        catch (Exception exception)
        {
            _diagnosticLog.TryWrite("audio-mute-sync", $"switch failed: {exception}");
            if (exception is BladeFilterEndpointMissingException or
                BladeFilterDriverIncompleteException)
            {
                if (generation == Volatile.Read(ref _audioMuteGeneration))
                {
                    SetBladeFilterDriverMissing(true);
                }
                return;
            }

            ReportBladeFnFailure(exception);
            if (exception is not FileNotFoundException &&
                !string.IsNullOrWhiteSpace(devicePath) &&
                Volatile.Read(ref _closing) == 0 &&
                generation == Volatile.Read(ref _audioMuteGeneration))
            {
                _ = RetryBladeAudioMuteRuntimeAsync(devicePath, productId, generation);
            }
        }
        finally
        {
            _audioMuteRuntimeGate.Release();
        }
    }

    private void PersistBladeSnapTap(bool enabled, int generation)
    {
        var dispatcher = _window?.DispatcherQueue;
        var viewModel = _audioMuteViewModel;
        if (dispatcher is null || viewModel is null)
        {
            return;
        }

        dispatcher.TryEnqueue(async () =>
        {
            if (generation == Volatile.Read(ref _audioMuteGeneration))
            {
                await viewModel.Blade.SetSnapTapEnabledAsync(enabled);
            }
        });
    }

    private async Task DisposeBladeAudioStackAsync()
    {
        var audioRuntime = _audioMuteRuntime;
        _audioMuteRuntime = null;
        var fnRuntime = _bladeFnRuntime;
        _bladeFnRuntime = null;
        Exception? audioError = null;
        try
        {
            if (audioRuntime is not null)
            {
                await audioRuntime.DisposeAsync().ConfigureAwait(false);
            }
        }
        catch (Exception exception)
        {
            audioError = exception;
        }

        try
        {
            if (fnRuntime is not null)
            {
                await fnRuntime.DisposeAsync().ConfigureAwait(false);
            }
        }
        catch (Exception fnError)
        {
            throw audioError is null
                ? fnError
                : new AggregateException(audioError, fnError);
        }

        if (audioError is not null)
        {
            throw audioError;
        }
    }

    private void EmergencyStopBladeLighting()
    {
        if (Interlocked.Exchange(ref _emergencyLightingCleanupStarted, 1) != 0)
        {
            return;
        }

        var controller = _bladeLightingController;
        if (controller is null)
        {
            return;
        }

        try
        {
            if (!controller.StopAsync().Wait(ExitCleanupTimeout))
            {
                _diagnosticLog.TryWrite(
                    "keyboard-lighting",
                    "Emergency lighting cleanup timed out; hardware may retain its last native effect.");
            }
        }
        catch (Exception exception)
        {
            _diagnosticLog.TryWrite("keyboard-lighting", $"Emergency lighting cleanup failed: {exception}");
        }
    }

    private async Task SetBladeIndicatorDisplayAvailableAsync(bool available)
    {
        Volatile.Write(ref _displayAvailable, available ? 1 : 0);
        await _audioMuteRuntimeGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_audioMuteRuntime is not null)
            {
                await _audioMuteRuntime.SetDisplayAvailableAsync(available).ConfigureAwait(false);
            }
        }
        catch (Exception exception)
        {
            _diagnosticLog.TryWrite(
                "audio-mute-sync",
                $"display-state indicator transition failed: {exception}");
            throw;
        }
        finally
        {
            _audioMuteRuntimeGate.Release();
        }
    }

    private void EmergencyStopBladeMapping()
    {
        if (Interlocked.Exchange(ref _emergencyMappingCleanupStarted, 1) != 0)
        {
            return;
        }

        Interlocked.Exchange(ref _closing, 1);
        Interlocked.Increment(ref _audioMuteGeneration);
        try
        {
            var cleanup = StopBladeMappingForExitAsync();
            if (!cleanup.Wait(ExitCleanupTimeout))
            {
                _diagnosticLog.TryWrite(
                    "blade-fn",
                    "Emergency Blade Fn cleanup timed out; RecoveryHost remains armed.");
            }
        }
        catch (Exception exception)
        {
            _diagnosticLog.TryWrite("blade-fn", $"Emergency Blade Fn cleanup failed: {exception}");
        }
    }

    private async Task StopBladeMappingForExitAsync()
    {
        await _audioMuteRuntimeGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await DisposeBladeAudioStackAsync().ConfigureAwait(false);
        }
        finally
        {
            _audioMuteRuntimeGate.Release();
        }
    }
}
