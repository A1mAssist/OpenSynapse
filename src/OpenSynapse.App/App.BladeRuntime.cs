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
    private static readonly TimeSpan BladeAudioMuteSwitchDebounce = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan BladeAudioMuteWatchdogInterval = TimeSpan.FromSeconds(2);
    private readonly CancellationTokenSource _bladeAudioMuteWatchdogStop = new();
    private Task? _bladeAudioMuteWatchdog;
    private long _bladeAudioMuteNextRetryTicks;

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
            try
            {
                await DisposeBladeAudioStackAsync();
            }
            catch (Exception exception)
            {
                _diagnosticLog.TryWrite("audio-mute-sync", $"stop failed: {exception}");
            }

            try
            {
                await _bladeModeCoordinator.RestoreAllAsync().WaitAsync(TimeSpan.FromSeconds(2));
            }
            catch (Exception exception)
            {
                _diagnosticLog.TryWrite("blade-mode", $"exit restore failed: {exception.Message}");
            }
        }
        finally
        {
            _audioMuteRuntimeGate.Release();
        }
    }

    private void OnBladeControlDeviceChanged(string? devicePath, ushort productId)
    {
        if (string.IsNullOrWhiteSpace(devicePath))
        {
            // A discovery pass can briefly miss the control interface during startup or a
            // PnP storm. Tearing the Fn runtime down here left the touchpad and game-mode
            // keys dead until something else triggered another enumeration.
            _diagnosticLog.TryWrite(
                "blade-fn",
                $"control device temporarily unavailable; keeping path={_activeBladeControlDevicePath ?? "<none>"} pid=0x{_activeBladeProductId:X4}");
            return;
        }

        _activeBladeControlDevicePath = devicePath;
        _activeBladeProductId = productId;
        var transport = _razerTransport;
        if (!string.IsNullOrWhiteSpace(devicePath) && transport is not null)
        {
            var path = devicePath;
            _bladeModeCoordinator.SetRestoreHandler(
                path,
                () => BladeRecoveryDeviceRestorer.RestoreNormalModeAsync(transport, path));
        }

        var generation = Interlocked.Increment(ref _audioMuteGeneration);
        _diagnosticLog.TryWrite(
            "blade-fn",
            $"control device changed: path={devicePath ?? "<none>"} pid=0x{productId:X4} generation={generation}");
        _ = SwitchBladeAudioMuteRuntimeAsync(devicePath, productId, generation);
    }

    private void OnBladeInputProfileChanged()
    {
        var generation = Interlocked.Increment(ref _audioMuteGeneration);
        _diagnosticLog.TryWrite(
            "blade-fn",
            $"input profile changed: generation={generation}");
        _ = SwitchBladeAudioMuteRuntimeAsync(
            _activeBladeControlDevicePath,
            _activeBladeProductId,
            generation);
    }

    private async Task SwitchBladeAudioMuteRuntimeAsync(
        string? devicePath,
        ushort productId,
        int generation,
        bool onlyIfMissing = false)
    {
        await _audioMuteRuntimeGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _closing) != 0 ||
                generation != Volatile.Read(ref _audioMuteGeneration))
            {
                return;
            }

            // Device discovery and the first profile apply both request a switch
            // within a few hundred milliseconds. Let that burst settle so the
            // second request cannot cancel the runtime halfway through startup.
            await Task.Delay(BladeAudioMuteSwitchDebounce).ConfigureAwait(false);
            if (Volatile.Read(ref _closing) != 0 ||
                generation != Volatile.Read(ref _audioMuteGeneration))
            {
                return;
            }

            // A watchdog tick can queue while the previous switch is still
            // starting the runtime. Re-check under the gate so that queued tick
            // cannot tear down the runtime it was waiting for.
            if (onlyIfMissing && _bladeFnRuntime is not null)
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
            var current = Volatile.Read(ref _audioMuteGeneration);
            if (Volatile.Read(ref _closing) != 0 || generation != current)
            {
                // A newer device/profile request owns the next attempt. This is
                // not a user-facing failure and must not schedule a duplicate.
                _diagnosticLog.TryWrite(
                    "audio-mute-sync",
                    $"switch superseded: generation={generation} current={current}: {exception.GetType().Name}");
                return;
            }

            _diagnosticLog.TryWrite("audio-mute-sync", $"switch failed: {exception}");
            if (exception is BladeFilterEndpointMissingException or
                BladeFilterDriverIncompleteException)
            {
                SetBladeFilterDriverMissing(true);
                return;
            }

            ReportBladeFnFailure(exception);
            if (exception is not FileNotFoundException &&
                !string.IsNullOrWhiteSpace(devicePath) &&
                Volatile.Read(ref _closing) == 0)
            {
                Volatile.Write(
                    ref _bladeAudioMuteNextRetryTicks,
                    Environment.TickCount64 + (long)AudioMuteRetryDelay.TotalMilliseconds);
                _ = RetryBladeAudioMuteRuntimeAsync(devicePath, productId, generation);
            }
        }
        finally
        {
            _audioMuteRuntimeGate.Release();
        }
    }

    private void StartBladeAudioMuteWatchdog()
    {
        _bladeAudioMuteWatchdog ??= WatchBladeAudioMuteRuntimeAsync(
            _bladeAudioMuteWatchdogStop.Token);
    }

    private async Task StopBladeAudioMuteWatchdogAsync()
    {
        _bladeAudioMuteWatchdogStop.Cancel();
        var watchdog = _bladeAudioMuteWatchdog;
        _bladeAudioMuteWatchdog = null;
        if (watchdog is null)
        {
            return;
        }

        try
        {
            await watchdog.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task WatchBladeAudioMuteRuntimeAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(BladeAudioMuteWatchdogInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                if (Volatile.Read(ref _closing) != 0 ||
                    _bladeFnRuntime is not null ||
                    _audioMuteRuntimeGate.CurrentCount == 0 ||
                    Environment.TickCount64 < Volatile.Read(ref _bladeAudioMuteNextRetryTicks))
                {
                    continue;
                }

                var devicePath = _activeBladeControlDevicePath;
                var productId = _activeBladeProductId;
                if (string.IsNullOrWhiteSpace(devicePath))
                {
                    RequestBladeControlDeviceDiscovery();
                    continue;
                }

                if (_razerTransport is null ||
                    !BladeFnRuntime.HasMapping(productId))
                {
                    continue;
                }

                // The runtime is allowed to die on a transient device event, but
                // the Fn keys must come back without waiting for a foreground or
                // restart. Reuse the normal switch so mode ownership stays intact.
                var generation = Volatile.Read(ref _audioMuteGeneration);
                _ = SwitchBladeAudioMuteRuntimeAsync(
                    devicePath,
                    productId,
                    generation,
                    onlyIfMissing: true);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private void RequestBladeControlDeviceDiscovery()
    {
        var viewModel = _audioMuteViewModel;
        if (Volatile.Read(ref _closing) != 0 ||
            viewModel is null ||
            viewModel.Blade.BladeProductId == 0 ||
            Environment.TickCount64 < Volatile.Read(ref _bladeAudioMuteNextRetryTicks))
        {
            return;
        }

        Volatile.Write(
            ref _bladeAudioMuteNextRetryTicks,
            Environment.TickCount64 + (long)AudioMuteRetryDelay.TotalMilliseconds);
        _diagnosticLog.TryWrite("blade-fn", "no control device yet; requesting another discovery.");
        viewModel.Refresh.RequestDeviceRefresh();
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

        try
        {
            _bladeModeCoordinator.RestoreAllAsync().Wait(TimeSpan.FromSeconds(2));
        }
        catch (Exception exception)
        {
            _diagnosticLog.TryWrite("blade-mode", $"emergency restore failed: {exception.Message}");
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
