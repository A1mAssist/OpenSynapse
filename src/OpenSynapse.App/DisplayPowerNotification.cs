using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using OpenSynapse.App.ViewModels;
using System.Runtime.InteropServices;

namespace OpenSynapse.App;

internal sealed class DisplayPowerNotification
{
    private const uint WmDeviceChange = 0x0219;
    private const uint WmPowerBroadcast = 0x0218;
    private const uint WmEndSession = 0x0016;
    private const nuint DbtDeviceArrival = 0x8000;
    private const nuint DbtDeviceRemoveComplete = 0x8004;
    private const nuint DbtDevNodesChanged = 0x0007;
    private const uint PbtApmSuspend = 0x0004;
    private const uint PbtApmPowerStatusChange = 0x000A;
    private const uint PbtApmResumeSuspend = 0x0007;
    private const uint PbtApmResumeAutomatic = 0x0012;
    private const uint DeviceNotifyCallback = 0x00000002;
    private static readonly Guid ConsoleDisplayStateGuid = new("6fe69556-704a-47a0-8f24-c28d936fda47");

    private readonly Window _window;
    private readonly MainViewModel _viewModel;
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly Func<bool, Task>? _setBladeIndicatorDisplayAvailable;
    private readonly Func<bool>? _turnOffLightingOnSessionEnding;
    private readonly CancellationToken _lifetime;
    private readonly Action<string>? _diagnostic;
    private SubclassProcedure? _powerSubclassProcedure;
    private SuspendResumeCallback? _displayStateCallback;
    private SuspendResumeCallback? _suspendResumeCallback;
    private nint _powerNotificationHandle;
    private nint _suspendResumeNotificationHandle;
    private bool _displayStateOn = true;
    private bool _systemSuspended;
    private readonly object _powerTransitionGate = new();
    private Task _powerTransition = Task.CompletedTask;

    internal DisplayPowerNotification(
        Window window,
        MainViewModel viewModel,
        DispatcherQueue dispatcherQueue,
        Func<bool, Task>? setBladeIndicatorDisplayAvailable,
        CancellationToken lifetime,
        Action<string>? diagnostic = null,
        Func<bool>? turnOffLightingOnSessionEnding = null)
    {
        _window = window;
        _viewModel = viewModel;
        _dispatcherQueue = dispatcherQueue;
        _setBladeIndicatorDisplayAvailable = setBladeIndicatorDisplayAvailable;
        _turnOffLightingOnSessionEnding = turnOffLightingOnSessionEnding;
        _lifetime = lifetime;
        _diagnostic = diagnostic;
    }

    internal void Initialize()
    {
        _powerSubclassProcedure = HandlePowerWindowMessage;
        var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(_window);
        if (!SetWindowSubclass(windowHandle, _powerSubclassProcedure, 0x4F5350, UIntPtr.Zero))
        {
            _diagnostic?.Invoke("Could not subclass the main window for power notifications.");
            return;
        }
        _displayStateCallback = HandleDisplayStateNotification;
        var guid = ConsoleDisplayStateGuid;
        var displayParameters = new DeviceNotifySubscribeParameters
        {
            Callback = Marshal.GetFunctionPointerForDelegate(_displayStateCallback),
            Context = IntPtr.Zero,
        };
        _powerNotificationHandle = 0;
        var displayResult = PowerSettingRegisterNotification(
            ref guid,
            DeviceNotifyCallback,
            ref displayParameters,
            out _powerNotificationHandle);
        if (displayResult != 0)
        {
            _diagnostic?.Invoke($"PowerSettingRegisterNotification failed: 0x{displayResult:X8}.");
        }

        _suspendResumeCallback = HandleSuspendResumeNotification;
        var parameters = new DeviceNotifySubscribeParameters
        {
            Callback = Marshal.GetFunctionPointerForDelegate(_suspendResumeCallback),
            Context = IntPtr.Zero,
        };
        _suspendResumeNotificationHandle = 0;
        var suspendResult = PowerRegisterSuspendResumeNotification(
            DeviceNotifyCallback,
            ref parameters,
            out _suspendResumeNotificationHandle);
        if (suspendResult != 0)
        {
            _diagnostic?.Invoke($"PowerRegisterSuspendResumeNotification failed: 0x{suspendResult:X8}.");
        }
    }

    internal void Dispose()
    {
        var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(_window);
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
        // A non-zero wParam means the session end was confirmed, not cancelled by
        // another app after WM_QUERYENDSESSION.
        if (message == WmEndSession && wParam != 0 &&
            _turnOffLightingOnSessionEnding?.Invoke() == true)
        {
            _viewModel.Accessories.TurnOffHeadsetLightingForShutdown(TimeSpan.FromSeconds(1));
            _viewModel.RazerHid.TurnOffLightingForShutdown(TimeSpan.FromSeconds(2));
        }

        if (message == WmDeviceChange &&
            (unchecked((nuint)wParam.ToInt64()) is DbtDeviceArrival or DbtDeviceRemoveComplete or DbtDevNodesChanged))
        {
            _viewModel.Refresh.RequestDeviceRefresh();
        }

        if (message == WmPowerBroadcast)
        {
            switch (unchecked((uint)wParam.ToInt64()))
            {
                case PbtApmPowerStatusChange:
                    _viewModel.Refresh.RequestDeviceRefresh();
                    break;
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
                // State 0 is off; states 1 and 2 (dimmed) are available.
                // Ignore repeated availability so dimming does not restart the effect.
                var state = Marshal.ReadInt32(setting, Marshal.SizeOf<PowerBroadcastSettingHeader>());
                if (state is >= 0 and <= 2)
                {
                    SetConsoleDisplayState(state != 0);
                }
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
            if (_displayStateOn == available)
            {
                return;
            }
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
            _viewModel.Refresh.RequestDeviceRefresh();
            QueueDisplayTransition(suspending: false);
            _dispatcherQueue.TryEnqueue(ReplayCpuPowerLimitAfterResume);
        }
    }

    /// <summary>
    /// Firmware resets the wall while the machine sleeps, so the user's preset must be
    /// written again. This is a background action: failures stay off the status bar.
    /// </summary>
    private async void ReplayCpuPowerLimitAfterResume()
    {
        if (_viewModel.IsLiteMode || _lifetime.IsCancellationRequested)
        {
            return;
        }

        try
        {
            await _viewModel.CpuPower.ReplayCpuPowerLimitAfterResumeAsync(_lifetime);
        }
        catch (Exception)
        {
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
                await _viewModel.Lighting.PrepareForSuspendAsync().ConfigureAwait(false);
            }
            else
            {
                await _viewModel.Lighting.SetDisplayAvailableAsync(available).ConfigureAwait(false);
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
