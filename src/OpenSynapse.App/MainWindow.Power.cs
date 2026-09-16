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
    private bool _displaySuspended;
    private bool _displayStateOn = true;
    private bool _systemSuspended;
    private readonly SemaphoreSlim _powerTransitionGate = new(1, 1);

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
                // Dimmed (2) is treated as unavailable because some systems use it
                // instead of Off when the automatic display timeout expires.
                SetConsoleDisplayState(Marshal.ReadInt32(setting, Marshal.SizeOf<PowerBroadcastSettingHeader>()) == 1);
            }
        }
        return 0;
    }

    private void PrepareForSuspend()
    {
        _powerTransitionGate.Wait();
        try
        {
            if (_systemSuspended)
            {
                return;
            }
            _systemSuspended = true;
            _displayStateOn = false;
            _displaySuspended = true;
            _setBladeIndicatorDisplayAvailable?.Invoke(false).GetAwaiter().GetResult();
            _viewModel.PrepareForSuspendAsync().GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            _dispatcherQueue.TryEnqueue(() =>
                _viewModel.ReportApplicationError(AppStrings.FormatText("SuspendFanRestoreError", exception.Message)));
        }
        finally
        {
            _powerTransitionGate.Release();
        }
    }

    private void SetConsoleDisplayState(bool available)
    {
        _powerTransitionGate.Wait();
        try
        {
            _displayStateOn = available;
            if (_displaySuspended == !available)
            {
                return;
            }
            _displaySuspended = !available;
            if (!available)
            {
                _setBladeIndicatorDisplayAvailable?.Invoke(false).GetAwaiter().GetResult();
            }
            _viewModel.SetDisplayAvailableAsync(available).GetAwaiter().GetResult();
            if (available)
            {
                _setBladeIndicatorDisplayAvailable?.Invoke(true).GetAwaiter().GetResult();
            }
        }
        catch (Exception exception)
        {
            _dispatcherQueue.TryEnqueue(() =>
                _viewModel.ReportApplicationError(AppStrings.FormatText("SuspendFanRestoreError", exception.Message)));
        }
        finally
        {
            _powerTransitionGate.Release();
        }
    }

    private void ResumeFromSuspend()
    {
        _powerTransitionGate.Wait();
        try
        {
            _systemSuspended = false;
            _viewModel.RequestDeviceRefresh();
            if (_displayStateOn && _displaySuspended)
            {
                _displaySuspended = false;
                _viewModel.SetDisplayAvailableAsync(true).GetAwaiter().GetResult();
                _setBladeIndicatorDisplayAvailable?.Invoke(true).GetAwaiter().GetResult();
            }
        }
        catch (Exception exception)
        {
            _dispatcherQueue.TryEnqueue(() =>
                _viewModel.ReportApplicationError(AppStrings.FormatText("SuspendFanRestoreError", exception.Message)));
        }
        finally
        {
            _powerTransitionGate.Release();
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
