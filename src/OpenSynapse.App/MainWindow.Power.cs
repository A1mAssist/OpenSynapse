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
    private const uint PbtApmSuspend = 0x0004;
    private const uint PbtApmResumeSuspend = 0x0007;
    private const uint PbtApmResumeAutomatic = 0x0012;
    private const uint DeviceNotifyCallback = 0x00000002;
    private const uint DeviceNotifyWindowHandle = 0;
    private static readonly Guid ConsoleDisplayStateGuid = new("6fe69556-704a-47a0-8f24-c28d936fda47");
    private SubclassProcedure? _powerSubclassProcedure;
    private SuspendResumeCallback? _suspendResumeCallback;
    private nint _powerNotificationHandle;
    private nint _suspendResumeNotificationHandle;
    private bool _displaySuspended;
    private bool _displayStateOn = true;
    private int _suspendPreparationInFlight;

    private void InitializeDisplayPowerNotification()
    {
        _powerSubclassProcedure = HandlePowerWindowMessage;
        var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        if (!SetWindowSubclass(windowHandle, _powerSubclassProcedure, 0x4F5350, UIntPtr.Zero)) return;
        var guid = ConsoleDisplayStateGuid;
        _powerNotificationHandle = RegisterPowerSettingNotification(windowHandle, ref guid, DeviceNotifyWindowHandle);
        if (_powerNotificationHandle == 0) RemoveWindowSubclass(windowHandle, _powerSubclassProcedure, 0x4F5350);

        _suspendResumeCallback = HandleSuspendResumeNotification;
        var parameters = new DeviceNotifySubscribeParameters
        {
            Callback = Marshal.GetFunctionPointerForDelegate(_suspendResumeCallback),
            Context = IntPtr.Zero,
        };
        _ = PowerRegisterSuspendResumeNotification(
            DeviceNotifyCallback,
            ref parameters,
            out _suspendResumeNotificationHandle);
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
        if (_suspendResumeNotificationHandle != 0)
        {
            PowerUnregisterSuspendResumeNotification(_suspendResumeNotificationHandle);
            _suspendResumeNotificationHandle = 0;
        }
        _suspendResumeCallback = null;
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
                // Windows may report automatic display timeout as dimmed (2)
                // before or instead of fully off (0). Keep lighting off for
                // every state except the explicitly-on state (1).
                _displayStateOn = setting.Data == 1;
                if (!_displayStateOn && !_displaySuspended)
                {
                    _displaySuspended = true;
                    PrepareForSuspend();
                }
                else if (_displayStateOn && _displaySuspended)
                {
                    _displaySuspended = false;
                    _dispatcherQueue.TryEnqueue(_viewModel.RequestDeviceRefresh);
                }
            }
        }
        else if (message == WmPowerBroadcast)
        {
            switch (unchecked((uint)wParam.ToInt64()))
            {
                case PbtApmSuspend:
                    PrepareForSuspend();
                    break;
                case PbtApmResumeSuspend:
                case PbtApmResumeAutomatic:
                    if (_displayStateOn)
                    {
                        _displaySuspended = false;
                        _dispatcherQueue.TryEnqueue(_viewModel.RequestDeviceRefresh);
                    }
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
                _viewModel.ReportApplicationError(AppStrings.FormatText("SuspendFanRestoreError", exception.Message)));
        }
        finally
        {
            Interlocked.Exchange(ref _suspendPreparationInFlight, 0);
        }
    }

    private uint HandleSuspendResumeNotification(nint context, uint eventType, nint setting)
    {
        switch (eventType)
        {
            case PbtApmSuspend:
                PrepareForSuspend();
                break;
            case PbtApmResumeSuspend:
            case PbtApmResumeAutomatic:
                if (_displayStateOn)
                {
                    _displaySuspended = false;
                    _dispatcherQueue.TryEnqueue(_viewModel.RequestDeviceRefresh);
                }
                break;
        }

        return 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PowerSettingChange { public Guid PowerSetting; public uint DataLength; public uint Data; }

    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceNotifySubscribeParameters
    {
        public nint Callback;
        public nint Context;
    }

    private delegate nint SubclassProcedure(nint windowHandle, uint message, nint wParam, nint lParam, nuint subclassId, nuint referenceData);
    private delegate uint SuspendResumeCallback(nint context, uint eventType, nint setting);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint RegisterPowerSettingNotification(nint recipient, ref Guid powerSettingGuid, uint flags);
    [DllImport("PowrProf.dll", SetLastError = true)]
    private static extern uint PowerRegisterSuspendResumeNotification(
        uint flags,
        ref DeviceNotifySubscribeParameters recipient,
        out nint registrationHandle);
    [DllImport("PowrProf.dll", SetLastError = true)]
    private static extern uint PowerUnregisterSuspendResumeNotification(nint registrationHandle);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UnregisterPowerSettingNotification(nint handle);
    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowSubclass(nint windowHandle, SubclassProcedure subclassProcedure, nuint subclassId, nuint referenceData);
    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool RemoveWindowSubclass(nint windowHandle, SubclassProcedure subclassProcedure, nuint subclassId);
    [DllImport("comctl32.dll")]
    private static extern nint DefSubclassProc(nint windowHandle, uint message, nint wParam, nint lParam);
}
