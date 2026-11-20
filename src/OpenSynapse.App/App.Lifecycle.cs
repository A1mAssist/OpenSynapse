using OpenSynapse.App.ViewModels;
using OpenSynapse.Windows.Lifecycle;
using OpenSynapse.Core.Storage;
using Microsoft.Windows.AppNotifications;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Velopack;
using Velopack.Locators;

namespace OpenSynapse.App;

public partial class App
{
    private int _processExitRequested;
    private string[] _startupArguments = [];
    private int _restartRequested;
    private bool _restartPreviousLiteMode;
    private const uint MessageBoxInformation = 0x00000040;
    private const uint MessageBoxTopmost = 0x00040000;

    private void LoadBehaviorSettings()
    {
        _behaviorSettings = AppBehaviorSettings.Load();
        if (_behaviorSettings.LoadError is { } loadError)
        {
            _diagnosticLog.TryWrite("behavior",
                $"load failed: {loadError}; recovery copy: {_behaviorSettings.RecoveryFilePath ?? "unavailable; saving blocked"}");
        }
    }

    private void EnsureStartupIntent()
    {
        if (Environment.ProcessPath is not { } executablePath)
        {
            return;
        }

        try
        {
            var manager = new WindowsStartupManager();
            if (_behaviorSettings.StartWithWindows is not { } enabled)
            {
                // First run after this setting existed: adopt whatever the Run key
                // says, including a path repaired to the current executable.
                if (!manager.IsEnabled(executablePath) &&
                    !manager.IsSilentEnabled(executablePath) &&
                    !WindowsStartupManager.TryRepairStaleRegistration(executablePath))
                {
                    return;
                }

                _behaviorSettings.StartWithWindows = true;
                _behaviorSettings.StartSilent = manager.IsSilentEnabled(executablePath);
                _behaviorSettings.Save();
                _diagnosticLog.TryWrite("startup", "Adopted the existing startup registration.");
                return;
            }

            WindowsStartupManager.EnsureRegistration(
                enabled,
                _behaviorSettings.StartSilent,
                executablePath);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
                InvalidOperationException or System.Security.SecurityException)
        {
            _diagnosticLog.TryWrite("startup", $"registration failed: {exception}");
        }
    }

    private void SaveStartupIntent(bool enabled, bool silent)
    {
        _behaviorSettings.StartWithWindows = enabled;
        _behaviorSettings.StartSilent = silent;
        // The caller (ProfilesWorkspace) surfaces a failed save as a startup
        // setting error, so a persistence failure must not be swallowed here.
        _behaviorSettings.Save();
    }

    private void LogLiteModeState(bool liteMode)
    {
        if (liteMode)
        {
            _diagnosticLog.TryWrite(
                "application",
                "Lite mode enabled; Blade lighting, performance sampling and Chroma REST are disabled.");
        }
    }

    private static void ShowLegacyInstanceMessage() =>
        MessageBoxW(
            IntPtr.Zero,
            AppStrings.Text("LegacyInstanceRunningMessage"),
            "OpenSynapse",
            MessageBoxInformation | MessageBoxTopmost);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr window, string text, string caption, uint type);

    private void MarkElevatedIfNeeded(bool isElevated)
    {
        if (isElevated)
        {
            _singleInstanceGuard?.MarkElevated();
        }
    }

    private async Task ObserveActivationRequestsAsync(CancellationToken cancellationToken)
    {
        var guard = _singleInstanceGuard;
        if (guard is null)
        {
            return;
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            var signal = await Task.Run(
                () => guard.WaitForSignal(cancellationToken),
                CancellationToken.None);
            switch (signal)
            {
                case SingleInstanceSignal.Activate:
                    _window?.RequestActivation();
                    break;
                case SingleInstanceSignal.Shutdown:
                    _window?.RequestExit();
                    return;
                default:
                    return;
            }
        }
    }

    private async Task CloseApplicationAsync(MainWindow window, MainViewModel viewModel)
    {
        if (!ReferenceEquals(_window, window))
        {
            return;
        }

        try
        {
            if (_elevatedMacroClient is { } macroClient)
            {
                using var stopTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                try
                {
                    _ = await macroClient.StopAsync(stopTimeout.Token).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    _diagnosticLog.TryWrite(
                        "application",
                        $"Macro agent shutdown failed: {exception.Message}");
                }
                finally
                {
                    macroClient.Dispose();
                }
            }

            // Cleanup can hang on a stale HID session or a blocked helper pipe. Bound the
            // wait here so the process still exits instead of leaving another stale
            // instance behind on every relaunch.
            await GetShutdownTask(viewModel)
                .WaitAsync(ExitCleanupTimeout + TimeSpan.FromSeconds(3))
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _diagnosticLog.TryWrite("application", $"Shutdown failed: {exception}");
        }
        finally
        {
            // WinUI keeps the dispatcher alive after the last window closes in some
            // tray/window combinations. Without an explicit exit each close leaves a
            // zombie process holding device and graphics handles until reboot.
            if (Interlocked.Exchange(ref _processExitRequested, 1) == 0)
            {
                RestartIfRequested();
                _diagnosticLog.TryWrite("application", "OpenSynapse process exit requested.");
                Environment.Exit(0);
            }
        }
    }

    internal void RequestRestart(bool previousLiteMode)
    {
        if (_window is null)
        {
            _diagnosticLog.TryWrite("application", "Lite mode restart requested before the window existed.");
            return;
        }

        _restartPreviousLiteMode = previousLiteMode;
        Interlocked.Exchange(ref _restartRequested, 1);
        _window.RequestExit();
    }

    private void RestartIfRequested()
    {
        if (Interlocked.Exchange(ref _restartRequested, 0) == 0)
        {
            return;
        }

        // The shutdown path normally releases this already; disposing again keeps a
        // timed-out cleanup from handing the new process back to the instance in exit.
        Interlocked.Exchange(ref _singleInstanceGuard, null)?.Dispose();
        try
        {
            var executablePath = Environment.ProcessPath ??
                throw new InvalidOperationException("The current executable path is unavailable.");
            var startInfo = new ProcessStartInfo(executablePath)
            {
                UseShellExecute = false,
                WorkingDirectory = AppContext.BaseDirectory,
            };
            foreach (var argument in _startupArguments)
            {
                startInfo.ArgumentList.Add(argument);
            }
            if (Process.Start(startInfo) is null)
            {
                throw new InvalidOperationException("The restarted process did not start.");
            }
            _diagnosticLog.TryWrite("application", "Restarted to apply Lite mode.");
        }
        catch (Exception exception)
        {
            _diagnosticLog.TryWrite(
                "application",
                $"Lite mode restart failed: {exception.Message}; reverted LiteModeEnabled to {_restartPreviousLiteMode}.");
            _behaviorSettings.LiteModeEnabled = _restartPreviousLiteMode;
            try
            {
                _behaviorSettings.Save();
            }
            catch (Exception saveException) when (
                saveException is IOException or UnauthorizedAccessException or
                    InvalidOperationException or System.Security.SecurityException)
            {
                _diagnosticLog.TryWrite(
                    "behavior",
                    $"Lite mode rollback save failed: {saveException.Message}");
            }
        }
    }

    internal async Task ApplyUpdateAndRestartAsync(UpdateManager updateManager, VelopackAsset release)
    {
        if (_audioMuteViewModel is null)
        {
            throw new InvalidOperationException("OpenSynapse is not ready to restart for an update.");
        }

        if (OpenSynapseServiceInstaller.IsMachineInstallPath(Environment.ProcessPath))
        {
            var locator = VelopackLocator.Current;
            var root = locator.RootAppDir
                ?? throw new InvalidOperationException("The OpenSynapse install root is unavailable.");
            var packages = locator.PackagesDir
                ?? throw new InvalidOperationException("The OpenSynapse package directory is unavailable.");
            var packagePath = Path.Combine(packages, release.FileName);
            var attempt = await OpenSynapseServiceClient
                .TryApplyUpdateAsync(
                    release.FileName,
                    packagePath,
                    release.Version.ToString(),
                    Environment.ProcessId)
                .ConfigureAwait(false);
            if (attempt.Started)
            {
                await GetShutdownTask(_audioMuteViewModel);
                Environment.Exit(0);
            }

            if (attempt.ViaService)
            {
                throw new InvalidOperationException(
                    attempt.Error ?? "The OpenSynapse service could not start the update task.");
            }

            _ = MachineWideUpdateLauncher.Start(
                Path.Combine(root, "Update.exe"),
                packagePath,
                root,
                packages,
                Environment.ProcessId);
            await GetShutdownTask(_audioMuteViewModel);
            Environment.Exit(0);
        }

        await GetShutdownTask(_audioMuteViewModel);
        updateManager.ApplyUpdatesAndRestart(release);
    }

    private Task GetShutdownTask(MainViewModel viewModel)
    {
        lock (_shutdownGate)
        {
            return _shutdownTask ??= ShutdownApplicationAsync(viewModel);
        }
    }

    private async Task ShutdownApplicationAsync(MainViewModel viewModel)
    {
        Interlocked.Exchange(ref _closing, 1);
        Interlocked.Increment(ref _audioMuteGeneration);
        await StopBladeAudioMuteWatchdogAsync().ConfigureAwait(false);
        await DisposeChromaHostsAsync().ConfigureAwait(false);
        if (_audioMuteViewModel is not null)
        {
            _audioMuteViewModel.Blade.ControlDeviceChanged -= OnBladeControlDeviceChanged;
            _audioMuteViewModel.Blade.PerformanceModeChangedByUser -= OnBladePerformanceModeChangedByUser;
            _audioMuteViewModel.Blade.GamingModeChangedByUser -= OnBladeGamingModeChangedByUser;
            _audioMuteViewModel.Blade.GamingModeStateChanged -= OnBladeGamingModeStateChanged;
            _audioMuteViewModel.Blade.TouchpadChangedByUser -= OnBladeTouchpadChangedByUser;
            _audioMuteViewModel.Blade.OneTimeFullChargeChangedByUser -= OnBladeOneTimeFullChargeChangedByUser;
            _audioMuteViewModel.InternalDisplayRefreshRateChangedByUser -= OnInternalDisplayRefreshRateChangedByUser;
            _audioMuteViewModel.Blade.InputProfileChanged -= OnBladeInputProfileChanged;
        _audioMuteViewModel.Telemetry.BatteryObserved -= OnPeripheralBatteryObserved;
            _audioMuteViewModel.Macros.MacroContinuousPlaybackStarted -= OnMacroContinuousPlaybackStarted;
            _audioMuteViewModel = null;
        }
        if (_razerHidDeviceService is not null)
        {
            _razerHidDeviceService.BatteryObserved -= OnPeripheralBatteryObserved;
            _razerHidDeviceService = null;
        }
        try
        {
            await DisposeHardwareForExitAsync(viewModel).WaitAsync(ExitCleanupTimeout);
        }
        catch (TimeoutException)
        {
            _diagnosticLog.TryWrite(
                "application",
                "Exit cleanup timed out; stale-session recovery remains armed.");
        }
        _diagnosticLog.TryWrite("application", "OpenSynapse stopped.");
        _taskbarStatusPresenter?.Dispose();
        _taskbarStatusPresenter = null;
        _trayIcon?.Dispose();
        _trayIcon = null;
        _trayMenuWindow?.CloseMenuHost();
        _trayMenuWindow = null;
        _performanceMonitor?.Dispose();
        _performanceMonitor = null;
        _activationCancellation?.Cancel();
        _activationCancellation?.Dispose();
        _activationCancellation = null;
        _singleInstanceGuard?.Dispose();
        _singleInstanceGuard = null;
        _razerTransport = null;
        if (_appNotificationsRegistered)
        {
            try
            {
                AppNotificationManager.Default.Unregister();
            }
            catch (Exception exception)
            {
                _diagnosticLog.TryWrite("notifications", $"unregistration failed: {exception}");
            }
            _appNotificationsRegistered = false;
        }
    }
}
