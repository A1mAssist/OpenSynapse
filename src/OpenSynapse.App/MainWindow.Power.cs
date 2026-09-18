using System.Runtime.InteropServices;

namespace OpenSynapse.App;

public sealed partial class MainWindow
{
    private const uint WmDeviceChange = 0x0219;
    private const uint WmPowerBroadcast = 0x0218;
    private const nuint DbtDeviceArrival = 0x8000;
    private const nuint DbtDeviceRemoveComplete = 0x8004;
    private const nuint DbtDevNodesChanged = 0x0007;
    private const uint PbtApmSuspend = 0x0004;
    private const uint PbtApmResumeSuspend = 0x0007;
    private const uint PbtApmResumeAutomatic = 0x0012;
    private const uint DeviceNotifyCallback = 0x00000002;
    private static readonly Guid ConsoleDisplayStateGuid = new("6fe69556-704a-47a0-8f24-c28d936fda47");
    private SubclassProcedure? _powerSubclassProcedure;
    private SuspendResumeCallback? _displayStateCallback;
    private SuspendResumeCallback? _suspendResumeCallback;
    private nint _powerNotificationHandle;
    private nint _suspendResumeNotificationHandle;
    private bool _displayStateOn = true;
    private bool _systemSuspended;
    private readonly object _powerTransitionGate = new();
    private Task _powerTransition = Task.CompletedTask;

    private void InitializeDisplayPowerNotification()
    {
        _powerSubclassProcedure = HandlePowerWindowMessage;
        var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        if (!SetWindowSubclass(windowHandle, _powerSubclassProcedure, 0x4F5350, UIntPtr.Zero)) return;
        _displayStateCallback = HandleDisplayStateNotification;
        var guid = ConsoleDisplayStateGuid;
        var displayParameters = new DeviceNotifySubscribeParameters
        {
            Callback = Marshal.GetFunctionPointerForDelegate(_displayStateCallback),
            Context = IntPtr.Zero,
        };
        _ = PowerSettingRegisterNotification(
            ref guid,
            DeviceNotifyCallback,
            ref displayParameters,
            out _powerNotificationHandle);

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
            PowerSettingUnregisterNotification(_powerNotificationHandle);
            _powerNotificationHandle = 0;
        }
        if (_powerSubclassProcedure is not null)
            RemoveWindowSubclass(windowHandle, _powerSubclassProcedure, 0x4F5350);
        if (_suspendResumeNotificationHandle != 0)
        {
            PowerUnregisterSuspendResumeNotification(_suspendResumeNotificationHandle);
            _suspendResumeNotificationHandle = 0;
        }
        _displayStateCallback = null;
        _suspendResumeCallback = null;
    }

    private nint HandlePowerWindowMessage(nint windowHandle, uint message, nint wParam, nint lParam, nuint subclassId, nuint referenceData)
    {
        if (message == WmDeviceChange &&
            (unchecked((nuint)wParam.ToInt64()) is DbtDeviceArrival or DbtDeviceRemoveComplete or DbtDevNodesChanged))
        {
            _viewModel.RequestDeviceRefresh();
        }

        if (message == WmPowerBroadcast)
        {
            switch (unchecked((uint)wParam.ToInt64()))
            {
                case PbtApmSuspend:
                    PrepareForSuspend();
                    break;
                case PbtApmResumeSuspend:
                case PbtApmResumeAutomatic:
                    ResumeFromSuspend();
                    break;
            }
        }
        return DefSubclassProc(windowHandle, message, wParam, lParam);
    }

    private uint HandleDisplayStateNotification(nint context, uint eventType, nint setting)
    {
        if (setting != 0)
        {
            var notification = Marshal.PtrToStructure<PowerBroadcastSettingHeader>(setting);
            if (notification.PowerSetting == ConsoleDisplayStateGuid && notification.DataLength == sizeof(uint))
            {
                // State 0 is display off; state 1 is on and state 2 is dimmed.
                // Dimming must not stop the keyboard lighting session.
                SetConsoleDisplayState(
                    Marshal.ReadInt32(setting, Marshal.SizeOf<PowerBroadcastSettingHeader>()) != 0);
            }
        }
        return 0;
    }

    private void PrepareForSuspend()
    {
        lock (_powerTransitionGate)
        {
            if (_systemSuspended)
            {
                return;
            }
            _systemSuspended = true;
            QueueDisplayTransition(suspending: true);
        }
    }

    private void SetConsoleDisplayState(bool available)
    {
        lock (_powerTransitionGate)
        {
            _displayStateOn = available;
            QueueDisplayTransition(suspending: false);
        }
    }

    private void ResumeFromSuspend()
    {
        lock (_powerTransitionGate)
        {
            if (!_systemSuspended)
            {
                return;
            }
            _systemSuspended = false;
            _viewModel.RequestDeviceRefresh();
            QueueDisplayTransition(suspending: false);
        }
    }

    private void QueueDisplayTransition(bool suspending)
    {
        var available = _displayStateOn && !_systemSuspended;
        _powerTransition = _powerTransition.ContinueWith(
            _ => RunDisplayTransitionAsync(available, suspending),
            CancellationToken.None,
            TaskContinuationOptions.None,
            TaskScheduler.Default).Unwrap();
    }

    private async Task RunDisplayTransitionAsync(bool available, bool suspending)
    {
        try
        {
            if (suspending)
            {
                await _viewModel.PrepareForSuspendAsync().ConfigureAwait(false);
            }
            else
            {
                await _viewModel.SetDisplayAvailableAsync(available).ConfigureAwait(false);
            }

            if (_setBladeIndicatorDisplayAvailable is not null)
            {
                await _setBladeIndicatorDisplayAvailable(available)
                    .WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
            }
        }
        catch (Exception exception)
        {
            _dispatcherQueue.TryEnqueue(() =>
                _viewModel.ReportApplicationError(AppStrings.FormatText(
                    "SuspendFanRestoreError",
                    exception.Message)));
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
                ResumeFromSuspend();
                break;
        }

        return 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceNotifySubscribeParameters
    {
        public nint Callback;
        public nint Context;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PowerBroadcastSettingHeader
    {
        public Guid PowerSetting;
        public uint DataLength;
    }

    private delegate nint SubclassProcedure(nint windowHandle, uint message, nint wParam, nint lParam, nuint subclassId, nuint referenceData);
    private delegate uint SuspendResumeCallback(nint context, uint eventType, nint setting);

    [DllImport("PowrProf.dll", SetLastError = true)]
    private static extern uint PowerSettingRegisterNotification(
        ref Guid settingGuid,
        uint flags,
        ref DeviceNotifySubscribeParameters recipient,
        out nint registrationHandle);
    [DllImport("PowrProf.dll", SetLastError = true)]
    private static extern uint PowerSettingUnregisterNotification(nint registrationHandle);
    [DllImport("PowrProf.dll", SetLastError = true)]
    private static extern uint PowerRegisterSuspendResumeNotification(
        uint flags,
        ref DeviceNotifySubscribeParameters recipient,
        out nint registrationHandle);
    [DllImport("PowrProf.dll", SetLastError = true)]
    private static extern uint PowerUnregisterSuspendResumeNotification(nint registrationHandle);
    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowSubclass(nint windowHandle, SubclassProcedure subclassProcedure, nuint subclassId, nuint referenceData);
    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool RemoveWindowSubclass(nint windowHandle, SubclassProcedure subclassProcedure, nuint subclassId);
    [DllImport("comctl32.dll")]
    private static extern nint DefSubclassProc(nint windowHandle, uint message, nint wParam, nint lParam);
}
