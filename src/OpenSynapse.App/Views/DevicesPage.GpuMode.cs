using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Win32;
using OpenSynapse.App.ViewModels;
using OpenSynapse.Core.Displays;
using OpenSynapse.Core.Sensors;
using OpenSynapse.Windows.Lifecycle;
using System.Diagnostics;

namespace OpenSynapse.App.Views;

public sealed partial class DevicesPage
{
    private bool _gpuModeUiReady;
    private bool _gpuModeBusy;
    private int _gpuModeRefreshVersion;
    private bool _legacyIntegratedRecoveryApplied;

    internal void RefreshDedicatedGpuMode() => _ = RefreshGpuModeAsync();

    /// <summary>
    /// The four modes are one selector over two executors. The controller is the
    /// single entry for reading and changing them; this page only maps its result
    /// into the card and the restart prompt.
    /// </summary>
    private async Task RefreshGpuModeAsync()
    {
        var version = ++_gpuModeRefreshVersion;
        BladeGpuModeState state;
        try
        {
            state = await ViewModel.Blade.ReadGpuModeStateAsync(Lifetime);
        }
        catch (OperationCanceledException) when (Lifetime.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception) when (RuntimeErrors.IsExpected(exception))
        {
            if (version == _gpuModeRefreshVersion)
            {
                SetGpuModeDetail(AppStrings.FormatText(
                    "DedicatedGpuModeError",
                    exception.Message));
            }
            return;
        }

        if (Lifetime.IsCancellationRequested || version != _gpuModeRefreshVersion)
        {
            return;
        }

        ApplyGpuModeStatus(state);

        if (!_legacyIntegratedRecoveryApplied &&
            ViewModel.Blade.BladeProductId != 0 &&
            state.Snapshot is { PnpSwitchAllowed: false, DedicatedGpuEnabled: false })
        {
            _legacyIntegratedRecoveryApplied = true;
            _ = RecoverLegacyIntegratedModeAsync();
        }
    }

    private static string GpuModeSummary(BladeGpuMode mode) =>
        AppStrings.Text(mode switch
        {
            BladeGpuMode.Integrated => "GpuModeIntegratedSummary",
            BladeGpuMode.Discrete => "GpuModeDiscreteSummary",
            BladeGpuMode.Auto => "GpuModeAutoSummary",
            _ => "GpuModeStandardSummary",
        });

    private void ApplyGpuModeStatus(BladeGpuModeState state)
    {
        var snapshot = state.Snapshot;
        GpuModeCard.Visibility =
            DedicatedGpuModePolicy.ShouldShow(state.Dedicated.BlockReason) || snapshot.MuxPresent
                ? Visibility.Visible
                : Visibility.Collapsed;
        _gpuModeUiReady = false;
        try
        {
            var faulted = state.Dedicated.DeviceState.IsFaulted();
            GpuModeRestartButton.Visibility = faulted ? Visibility.Visible : Visibility.Collapsed;
            GpuModeRestartButton.IsEnabled = faulted && !_gpuModeBusy;

            var stored = ViewModel.Blade.StoredGpuMode;
            var current = BladeGpuModes.GetCurrentMode(snapshot);
            var preferred = stored == BladeGpuMode.Auto &&
                BladeGpuModes.IsAvailable(snapshot, BladeGpuMode.Auto)
                    ? BladeGpuMode.Auto
                    : current;
            var options = BladeGpuModes.GetAvailableModes(snapshot)
                .Select(mode => new GpuModeOptionViewModel(
                    mode,
                    GpuModeLabel(mode),
                    GpuModeSummary(mode),
                    mode == preferred,
                    !_gpuModeBusy &&
                        (mode == current || BladeGpuModes.IsAvailable(snapshot, mode))))
                .ToArray();
            GpuModeOptionsList.ItemsSource = options;
            GpuModeOptionsList.Visibility = options.Length > 0
                ? Visibility.Visible
                : Visibility.Collapsed;
            GpuModeStatusText.Text = AppStrings.FormatText("GpuModeStatusCurrent", GpuModeLabel(current));
            SetGpuModeDetail(GpuModeStatusMessage(state, stored));
        }
        finally
        {
            _gpuModeUiReady = true;
        }
    }

    private void SetGpuModeDetail(string text)
    {
        GpuModeDetailText.Text = text;
        GpuModeDetailText.Visibility = string.IsNullOrEmpty(text)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private string GpuModeStatusMessage(BladeGpuModeState state, BladeGpuMode? stored)
    {
        var dedicated = state.Dedicated;
        var snapshot = state.Snapshot;
        if (dedicated.DeviceState.IsFaulted())
        {
            return AppStrings.FormatText("DedicatedGpuModeStatusFaulted", dedicated.ProblemCode);
        }

        if (!dedicated.IsSupported && !snapshot.MuxPresent)
        {
            return AppStrings.Text("DedicatedGpuModeStatusBlockedUnsupported");
        }

        var available = BladeGpuModes.GetAvailableModes(snapshot);
        if (available is [BladeGpuMode.Discrete])
        {
            return AppStrings.Text("GpuModeStatusOnlyDiscrete");
        }

        if (stored == BladeGpuMode.Auto && available.Contains(BladeGpuMode.Auto))
        {
            return AppStrings.FormatText(
                "GpuModeStatusAuto",
                GpuModeLabel(BladeGpuModes.ResolveAutoTarget(snapshot, ViewModel.Blade.CurrentPowerSource)));
        }

        return string.Empty;
    }

    private static string GpuModeLabel(BladeGpuMode mode) =>
        mode switch
        {
            BladeGpuMode.Integrated => AppStrings.Text("GpuModeIntegrated"),
            BladeGpuMode.Discrete => AppStrings.Text("GpuModeDiscrete"),
            BladeGpuMode.Auto => AppStrings.Text("GpuModeAuto"),
            _ => AppStrings.Text("GpuModeStandard"),
        };

    private async void GpuModeOptionClick(object sender, RoutedEventArgs e)
    {
        if (!_gpuModeUiReady || _gpuModeBusy ||
            sender is not RadioButton { Tag: BladeGpuMode mode } radio)
        {
            return;
        }

        if (!radio.IsEnabled)
        {
            return;
        }

        _gpuModeBusy = true;
        _gpuModeUiReady = false;
        SetGpuModeDetail(AppStrings.Text("DisplayMuxModeStatusApplying"));

        BladeGpuModeApplyResult result;
        try
        {
            result = await ViewModel.Blade.ApplyGpuModeAsync(mode, Lifetime);
            if (result.Outcome is BladeGpuModeApplyOutcome.Success or
                BladeGpuModeApplyOutcome.RestartRequired)
            {
                ViewModel.Refresh.RequestDeviceRefresh();
            }
        }
        catch (OperationCanceledException) when (Lifetime.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception) when (RuntimeErrors.IsExpected(exception))
        {
            ViewModel.ReportApplicationError(AppStrings.FormatText(
                "DedicatedGpuModeError",
                exception.Message));
            result = new BladeGpuModeApplyResult(BladeGpuModeApplyOutcome.Failed, mode);
        }
        finally
        {
            _gpuModeBusy = false;
            _gpuModeUiReady = true;
        }

        await RefreshGpuModeAsync();
        SetGpuModeDetail(result.Outcome == BladeGpuModeApplyOutcome.Blocked
            ? GpuModeBlockedMessage(result)
            : GpuModeResultMessage(result));

        if (result.Outcome == BladeGpuModeApplyOutcome.RestartRequired)
        {
            await ShowDisplayMuxRestartPromptAsync();
        }
    }

    private static string GpuModeBlockedMessage(in BladeGpuModeApplyResult result) =>
        result.BlockReason switch
        {
            BladeGpuModeBlockReason.NotDeclared => AppStrings.Text("GpuModeStatusBlockedNotDeclared"),
            BladeGpuModeBlockReason.NoMux => AppStrings.Text("GpuModeStatusBlockedNoMux"),
            BladeGpuModeBlockReason.ExternalDisplay => AppStrings.Text("DedicatedGpuModeStatusBlockedExternal"),
            BladeGpuModeBlockReason.DisplayTopology => AppStrings.Text("DedicatedGpuModeStatusBlockedDisplay"),
            BladeGpuModeBlockReason.InternalDisplayUnsupported => AppStrings.Text("DedicatedGpuModeStatusBlockedInternal"),
            BladeGpuModeBlockReason.DiscreteRouteLocked => AppStrings.Text("GpuModeStatusDiscreteRouteLocked"),
            BladeGpuModeBlockReason.DeviceFaulted => AppStrings.FormatText(
                "DedicatedGpuModeStatusFaulted",
                result.ErrorCode),
            BladeGpuModeBlockReason.NoDevice => AppStrings.Text("DedicatedGpuModeStatusNoDevice"),
            _ => AppStrings.Text("DedicatedGpuModeStatusBlockedUnsupported"),
        };

    private static string GpuModeResultMessage(in BladeGpuModeApplyResult result) =>
        result.ErrorCode == DedicatedGpuOperationUncertainException.PendingError
            ? AppStrings.Text("GpuModeStatusPendingRestart")
            : result.Outcome switch
            {
                BladeGpuModeApplyOutcome.Success => AppStrings.Text("GpuModeStatusApplied"),
                BladeGpuModeApplyOutcome.RestartRequired => AppStrings.Text("DisplayMuxModeStatusRestartRequired"),
                BladeGpuModeApplyOutcome.Cancelled => AppStrings.Text("DedicatedGpuModeStatusCancelled"),
                BladeGpuModeApplyOutcome.NoDevice => AppStrings.Text("DedicatedGpuModeStatusNoDevice"),
                BladeGpuModeApplyOutcome.RolledBack => AppStrings.Text("GpuModeStatusRolledBack"),
                BladeGpuModeApplyOutcome.RollbackFailed => AppStrings.Text("GpuModeStatusRollbackFailed"),
                _ => AppStrings.FormatText("DedicatedGpuModeStatusFailed", result.ErrorCode),
            };

    private void SystemPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode is PowerModes.StatusChange or PowerModes.Resume)
        {
            DispatcherQueue.TryEnqueue(ViewModel.Refresh.RequestDeviceRefresh);
        }
    }

    private async Task RecoverLegacyIntegratedModeAsync()
    {
        if (_gpuModeBusy || Lifetime.IsCancellationRequested)
        {
            return;
        }

        _gpuModeBusy = true;
        try
        {
            var result = await ViewModel.Blade.RestoreDedicatedGpuSilentlyAsync(Lifetime);
            if (result.Outcome is BladeGpuModeApplyOutcome.Blocked or
                BladeGpuModeApplyOutcome.Failed or
                BladeGpuModeApplyOutcome.RollbackFailed)
            {
                SetGpuModeDetail(AppStrings.Text("GpuModeStatusRecoveryNeedsElevation"));
            }
        }
        catch (OperationCanceledException) when (Lifetime.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception) when (RuntimeErrors.IsExpected(exception))
        {
            ViewModel.ReportApplicationError(AppStrings.FormatText(
                "DedicatedGpuModeError",
                exception.Message));
        }
        finally
        {
            _gpuModeBusy = false;
        }

        await RefreshGpuModeAsync();
    }

    private async Task ShowDisplayMuxRestartPromptAsync()
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = AppStrings.Text("DisplayMuxModeRestartTitle"),
            Content = AppStrings.Text("DisplayMuxModeStatusRestartRequired"),
            PrimaryButtonText = AppStrings.Text("DisplayMuxModeRestartNow"),
            CloseButtonText = AppStrings.Text("DisplayMuxModeRestartLater"),
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            RestartSystem();
        }
    }

    private static void RestartSystem()
    {
        // No /f: let Windows close applications normally so unsaved work can still prompt.
        using var process = Process.Start(new ProcessStartInfo("shutdown.exe", "/r /t 5")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
        });
    }

    private async void DedicatedGpuModeRestartClick(object sender, RoutedEventArgs e)
    {
        if (_gpuModeBusy || Lifetime.IsCancellationRequested)
        {
            return;
        }

        _gpuModeBusy = true;
        GpuModeRestartButton.IsEnabled = false;
        SetGpuModeDetail(AppStrings.Text("DedicatedGpuModeStatusRestarting"));

        BladeGpuModeApplyResult result;
        try
        {
            result = await ViewModel.Blade.RestartDedicatedGpuAsync(Lifetime);
        }
        catch (OperationCanceledException) when (Lifetime.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception) when (RuntimeErrors.IsExpected(exception))
        {
            ViewModel.ReportApplicationError(AppStrings.FormatText(
                "DedicatedGpuModeError",
                exception.Message));
            result = new BladeGpuModeApplyResult(BladeGpuModeApplyOutcome.Failed, BladeGpuMode.Standard);
        }
        finally
        {
            _gpuModeBusy = false;
        }

        RefreshDedicatedGpuMode();
        SetGpuModeDetail(result.Outcome == BladeGpuModeApplyOutcome.Success
            ? AppStrings.Text("DedicatedGpuModeStatusRestarted")
            : GpuModeResultMessage(result));
    }
}
