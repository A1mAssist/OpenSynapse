using OpenSynapse.App.ViewModels;
using OpenSynapse.Windows.Lifecycle;
using OpenSynapse.Core.Storage;
using System.Runtime.InteropServices;

namespace OpenSynapse.App;

public partial class App
{
    private int _processExitRequested;
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
            _elevatedMacroClient?.Dispose();
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
                _diagnosticLog.TryWrite("application", "OpenSynapse process exit requested.");
                Environment.Exit(0);
            }
        }
    }
}
