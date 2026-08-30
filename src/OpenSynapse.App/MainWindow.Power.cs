using Microsoft.Win32;
using System.Runtime.InteropServices;

namespace OpenSynapse.App;

public sealed partial class MainWindow
{
    private const uint WmDeviceChange = 0x0219;
    private const uint WmPowerBroadcast = 0x0218;
    private const nuint DbtDeviceArrival = 0x8000;
    private const nuint DbtDeviceRemoveComplete = 0x8004;
    private const nuint DbtDevNodesChanged = 0x0007;
    private const uint PbtPowerSettingChange = 0x8013;
    private const nuint PbtApmSuspend = 0x0004;
    private const nuint PbtApmResumeSuspend = 0x0007;
    private const nuint PbtApmResumeAutomatic = 0x0012;
    private const uint DeviceNotifyWindowHandle = 0;
    private static readonly Guid ConsoleDisplayStateGuid = new("6fe69556-704a-47a0-8f24-c28d936fda47");
    private SubclassProcedure? _powerSubclassProcedure;
    private nint _powerNotificationHandle;
    private bool _displaySuspended;
    private int _suspendPreparationInFlight;

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs args)
    {
        if (args.Mode == PowerModes.Suspend)
        {
            PrepareForSuspend();
        }
        else if (args.Mode == PowerModes.Resume)
        {
            _dispatcherQueue.TryEnqueue(_viewModel.RequestDeviceRefresh);
        }
    }

    private void InitializeDisplayPowerNotification()
    {
        _powerSubclassProcedure = HandlePowerWindowMessage;
        var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        if (!SetWindowSubclass(windowHandle, _powerSubclassProcedure, 0x4F5350, UIntPtr.Zero)) return;
        var guid = ConsoleDisplayStateGuid;
        _powerNotificationHandle = RegisterPowerSettingNotification(windowHandle, ref guid, DeviceNotifyWindowHandle);
        if (_powerNotificationHandle == 0) RemoveWindowSubclass(windowHandle, _powerSubclassProcedure, 0x4F5350);
    }

    private void ShutdownDisplayPowerNotification()
    {
        var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        if (_powerNotificationHandle != 0)
        {
            UnregisterPowerSettingNotification(_powerNotificationHandle);
            _powerNotificationHandle = 0;
        }
        if (_powerSubclassProcedure is not null)
            RemoveWindowSubclass(windowHandle, _powerSubclassProcedure, 0x4F5350);
    }

    private nint HandlePowerWindowMessage(nint windowHandle, uint message, nint wParam, nint lParam, nuint subclassId, nuint referenceData)
    {
        if (message == WmDeviceChange &&
            (unchecked((nuint)wParam.ToInt64()) is DbtDeviceArrival or DbtDeviceRemoveComplete or DbtDevNodesChanged))
        {
            _viewModel.RequestDeviceRefresh();
        }

        if (message == PbtPowerSettingChange && lParam != 0)
        {
            var setting = Marshal.PtrToStructure<PowerSettingChange>(lParam);
            if (setting.PowerSetting == ConsoleDisplayStateGuid)
            {
                if (setting.Data == 0 && !_displaySuspended)
                {
                    _displaySuspended = true;
                    PrepareForSuspend();
                }
                else if (setting.Data == 1 && _displaySuspended)
                {
                    _displaySuspended = false;
                    _dispatcherQueue.TryEnqueue(_viewModel.RequestDeviceRefresh);
                }
            }
        }
        else if (message == WmPowerBroadcast)
        {
            switch (unchecked((nuint)wParam.ToInt64()))
            {
                case PbtApmSuspend:
                    PrepareForSuspend();
                    break;
                case PbtApmResumeSuspend:
                case PbtApmResumeAutomatic:
                    _displaySuspended = false;
                    _dispatcherQueue.TryEnqueue(_viewModel.RequestDeviceRefresh);
                    break;
            }
        }
        return DefSubclassProc(windowHandle, message, wParam, lParam);
    }

    private void PrepareForSuspend()
    {
        if (Interlocked.Exchange(ref _suspendPreparationInFlight, 1) != 0)
        {
            return;
        }

        try
        {
            _viewModel.PrepareForSuspendAsync().GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            _dispatcherQueue.TryEnqueue(() =>
                _viewModel.ReportApplicationError(AppStrings.FormatText("SuspendFanRestoreError",
                    exception.Message)));
        }
        finally
        {
            Interlocked.Exchange(ref _suspendPreparationInFlight, 0);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PowerSettingChange { public Guid PowerSetting; public uint DataLength; public uint Data; }

    private delegate nint SubclassProcedure(nint windowHandle, uint message, nint wParam, nint lParam, nuint subclassId, nuint referenceData);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint RegisterPowerSettingNotification(nint recipient, ref Guid powerSettingGuid, uint flags);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UnregisterPowerSettingNotification(nint handle);
    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowSubclass(nint windowHandle, SubclassProcedure subclassProcedure, nuint subclassId, nuint referenceData);
    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool RemoveWindowSubclass(nint windowHandle, SubclassProcedure subclassProcedure, nuint subclassId);
    [DllImport("comctl32.dll")]
    private static extern nint DefSubclassProc(nint windowHandle, uint message, nint wParam, nint lParam);
}
