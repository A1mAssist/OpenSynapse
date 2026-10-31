using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Win32;
using OpenSynapse.App.ViewModels;
using OpenSynapse.Core.Displays;
using OpenSynapse.Windows.Lifecycle;
using System.Diagnostics;

namespace OpenSynapse.App.Views;

public sealed partial class DevicesPage
{
    private bool _gpuModeUiReady;
    private bool _gpuModeBusy;
    private int _gpuModeRefreshVersion;
    private BladeGpuModeSnapshot? _gpuModeSnapshot;
    private bool _gpuModeStartupApplied;

    internal void RefreshDedicatedGpuMode() => _ = RefreshGpuModeAsync();

    /// <summary>
    /// The four modes are one selector over two executors: the PnP adapter switch and the
    /// firmware MUX. Availability comes from the model's own reports, so a laptop without
    /// a MUX simply shows the adapter-backed modes.
    /// </summary>
    private async Task RefreshGpuModeAsync()
    {
        var version = ++_gpuModeRefreshVersion;
        var dedicated = WindowsDedicatedGpuMode.GetStatus();
        BladeDisplayMuxStatus mux;
        try
        {
            mux = await ViewModel.Blade.ReadDisplayMuxStatusAsync(Lifetime);
        }
        catch (OperationCanceledException) when (Lifetime.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception) when (RuntimeErrors.IsExpected(exception))
        {
            mux = default;
        }

        if (Lifetime.IsCancellationRequested || version != _gpuModeRefreshVersion)
        {
            return;
        }

        var faulted = dedicated.DeviceState.IsFaulted();
        _gpuModeSnapshot = new BladeGpuModeSnapshot(
            DedicatedGpuSupported: !faulted && dedicated.IsSupported,
            DedicatedGpuEnabled: !dedicated.IsActive,
            Mux: mux);
        ApplyGpuModeStatus(dedicated, faulted);

        // Auto has to reconcile once after boot; later changes arrive as power events.
        if (!_gpuModeStartupApplied && !_gpuModeBusy &&
            ViewModel.Blade.StoredGpuMode == BladeGpuMode.Auto)
        {
            _gpuModeStartupApplied = true;
            _ = ApplyAutoGpuModeAsync();
        }
    }

    private sealed record GpuModeOption(string Label, BladeGpuMode Mode);

    private void ApplyGpuModeStatus(DedicatedGpuModeStatus dedicated, bool faulted)
    {
        GpuModeCard.Visibility = DedicatedGpuModePolicy.ShouldShow(dedicated.BlockReason)
            ? Visibility.Visible
            : Visibility.Collapsed;
        _gpuModeUiReady = false;
        try
        {
            GpuModeRestartButton.Visibility = faulted ? Visibility.Visible : Visibility.Collapsed;
            GpuModeRestartButton.IsEnabled = faulted && !_gpuModeBusy;

            var snapshot = _gpuModeSnapshot ?? default;
            var options = BladeGpuModes.GetAvailableModes(snapshot)
                .Select(mode => new GpuModeOption(GpuModeLabel(mode), mode))
                .ToArray();
            GpuModeCombo.ItemsSource = options;
            GpuModeCombo.Visibility = options.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            GpuModeCombo.IsEnabled = !_gpuModeBusy && options.Length > 0;

            var stored = ViewModel.Blade.StoredGpuMode;
            var preferred = stored == BladeGpuMode.Auto &&
                options.Any(option => option.Mode == BladeGpuMode.Auto)
                    ? BladeGpuMode.Auto
                    : BladeGpuModes.GetCurrentMode(snapshot);
            GpuModeCombo.SelectedItem = options.FirstOrDefault(option => option.Mode == preferred);
            GpuModeStatusText.Text = GpuModeStatusMessage(dedicated, faulted, snapshot, stored);
        }
        finally
        {
            _gpuModeUiReady = true;
        }
    }

    private string GpuModeStatusMessage(
        DedicatedGpuModeStatus dedicated,
        bool faulted,
        in BladeGpuModeSnapshot snapshot,
        BladeGpuMode? stored)
    {
        if (faulted)
        {
            return AppStrings.FormatText("DedicatedGpuModeStatusFaulted", dedicated.ProblemCode);
        }

        if (!dedicated.IsSupported)
        {
            return AppStrings.Text("DedicatedGpuModeStatusBlockedUnsupported");
        }

        if (stored == BladeGpuMode.Auto)
        {
            return AppStrings.FormatText(
                "GpuModeStatusAuto",
                GpuModeLabel(BladeGpuModes.ResolveAutoTarget(ViewModel.Blade.CurrentPowerSource)));
        }

        return AppStrings.FormatText(
            "GpuModeStatusCurrent",
            GpuModeLabel(BladeGpuModes.GetCurrentMode(snapshot)));
    }

    private static string GpuModeLabel(BladeGpuMode mode) =>
        mode switch
        {
            BladeGpuMode.Integrated => AppStrings.Text("GpuModeIntegrated"),
            BladeGpuMode.Discrete => AppStrings.Text("GpuModeDiscrete"),
            BladeGpuMode.Auto => AppStrings.Text("GpuModeAuto"),
            _ => AppStrings.Text("GpuModeStandard"),
        };

    private readonly record struct GpuModeApplyResult(string Message, bool RestartRequired);

    private async Task<GpuModeApplyResult> ApplyGpuModeAsync(
        BladeGpuMode mode,
        BladeGpuModeSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        if (mode == BladeGpuMode.Integrated)
        {
            // Disabling the adapter that currently drives the panel is a black screen.
            if (snapshot.MuxPresent && snapshot.Mux.Mode == BladeDisplayMuxMode.Discrete)
            {
                return new(AppStrings.Text("GpuModeStatusDiscreteRouteLocked"), false);
            }

            return MapDedicatedGpuResult(
                await WindowsDedicatedGpuMode.SetEnabledAsync(false, cancellationToken));
        }

        if (!snapshot.DedicatedGpuEnabled)
        {
            var enabled = await WindowsDedicatedGpuMode.SetEnabledAsync(true, cancellationToken);
            // A pending restart means the adapter is not usable yet; stop before the MUX.
            if (enabled.Outcome != DedicatedGpuSwitchOutcome.Success)
            {
                return MapDedicatedGpuResult(enabled);
            }
        }

        if (mode == BladeGpuMode.Standard)
        {
            if (!snapshot.MuxPresent || snapshot.Mux.Mode == BladeDisplayMuxMode.Hybrid)
            {
                return new(AppStrings.Text("DisplayMuxModeStatusSuccess"), false);
            }

            if (!BladeDisplayMuxModes.Supports(snapshot.Mux.Capabilities, BladeDisplayMuxMode.Hybrid))
            {
                return new(AppStrings.Text("GpuModeStatusNoHybrid"), false);
            }
        }
        else
        {
            if (!snapshot.MuxPresent ||
                !BladeDisplayMuxModes.Supports(snapshot.Mux.Capabilities, BladeDisplayMuxMode.Discrete))
            {
                return new(AppStrings.Text("DisplayMuxModeStatusNoPair"), false);
            }

            if (snapshot.Mux.Mode == BladeDisplayMuxMode.Discrete)
            {
                return new(AppStrings.Text("DisplayMuxModeStatusSuccess"), false);
            }
        }

        var target = mode == BladeGpuMode.Discrete
            ? BladeDisplayMuxMode.Discrete
            : BladeDisplayMuxMode.Hybrid;
        var result = await ViewModel.Blade.SetDisplayMuxModeAsync(target, cancellationToken);
        return result.Outcome switch
        {
            BladeDisplayMuxSwitchOutcome.Success =>
                new(AppStrings.Text("DisplayMuxModeStatusSuccess"), false),
            BladeDisplayMuxSwitchOutcome.RestartRequired =>
                new(AppStrings.Text("DisplayMuxModeStatusRestartRequired"), true),
            BladeDisplayMuxSwitchOutcome.Restored =>
                new(AppStrings.Text("DisplayMuxModeStatusRestored"), false),
            BladeDisplayMuxSwitchOutcome.RestoreFailed =>
                new(AppStrings.Text("DisplayMuxModeStatusRestoreFailed"), false),
            BladeDisplayMuxSwitchOutcome.NoDevice =>
                new(AppStrings.Text("DisplayMuxModeStatusNoDevice"), false),
            BladeDisplayMuxSwitchOutcome.Unsupported =>
                new(AppStrings.Text("DisplayMuxModeStatusNoPair"), false),
            _ => new(AppStrings.FormatText("DisplayMuxModeStatusFailed", result.ErrorCode), false),
        };
    }

    private static GpuModeApplyResult MapDedicatedGpuResult(DedicatedGpuSwitchResult result) =>
        result.Outcome switch
        {
            DedicatedGpuSwitchOutcome.Success =>
                new(AppStrings.Text("DedicatedGpuModeStatusSuccess"), false),
            DedicatedGpuSwitchOutcome.RestartRequired =>
                new(AppStrings.Text("DedicatedGpuModeStatusRestartRequired"), false),
            DedicatedGpuSwitchOutcome.Cancelled =>
                new(AppStrings.Text("DedicatedGpuModeStatusCancelled"), false),
            DedicatedGpuSwitchOutcome.NoDevice =>
                new(AppStrings.Text("DedicatedGpuModeStatusNoDevice"), false),
            DedicatedGpuSwitchOutcome.DeviceFaulted =>
                new(AppStrings.Text("DedicatedGpuModeStatusFaulted"), false),
            // Blocked keeps the refreshed status line, which names the actual obstacle.
            DedicatedGpuSwitchOutcome.Blocked => new(string.Empty, false),
            _ => new(AppStrings.FormatText("DedicatedGpuModeStatusFailed", result.ExitCode), false),
        };

    private async void GpuModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_gpuModeUiReady || _gpuModeBusy ||
            sender is not ComboBox { SelectedItem: GpuModeOption option } combo)
        {
            return;
        }

        var mode = option.Mode;
        _gpuModeBusy = true;
        _gpuModeUiReady = false;
        combo.IsEnabled = false;
        GpuModeStatusText.Text = AppStrings.Text("DisplayMuxModeStatusApplying");

        GpuModeApplyResult result;
        try
        {
            await ViewModel.Blade.SaveGpuModeAsync(
                mode == BladeGpuMode.Auto ? BladeGpuMode.Auto : null,
                Lifetime);

            var snapshot = _gpuModeSnapshot ?? default;
            var target = mode == BladeGpuMode.Auto
                ? BladeGpuModes.ResolveAutoTarget(ViewModel.Blade.CurrentPowerSource)
                : mode;
            result = await ApplyGpuModeAsync(target, snapshot, Lifetime);
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
            result = new(string.Empty, false);
        }
        finally
        {
            _gpuModeBusy = false;
            _gpuModeUiReady = true;
        }

        await RefreshGpuModeAsync();
        if (result.Message.Length > 0)
        {
            GpuModeStatusText.Text = result.Message;
        }

        if (result.RestartRequired)
        {
            await ShowDisplayMuxRestartPromptAsync();
        }
    }

    private void SystemPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode is PowerModes.StatusChange or PowerModes.Resume)
        {
            DispatcherQueue.TryEnqueue(() => _ = ApplyAutoGpuModeAsync());
        }
    }

    /// <summary>
    /// Auto follows the power source through the adapter switch only. Writing the MUX on
    /// every AC/battery change would restart the machine each time, so a panel that still
    /// sits on the discrete route is reported instead of rewritten.
    /// </summary>
    private async Task ApplyAutoGpuModeAsync()
    {
        if (_gpuModeBusy || Lifetime.IsCancellationRequested ||
            ViewModel.Blade.StoredGpuMode != BladeGpuMode.Auto)
        {
            return;
        }

        await RefreshGpuModeAsync();
        if (_gpuModeSnapshot is not { } snapshot)
        {
            return;
        }

        var target = BladeGpuModes.ResolveAutoTarget(ViewModel.Blade.CurrentPowerSource);
        if (!BladeGpuModes.IsAvailable(snapshot, target) ||
            BladeGpuModes.GetCurrentMode(snapshot) == target)
        {
            return;
        }

        if (target == BladeGpuMode.Standard && snapshot.MuxPresent &&
            snapshot.Mux.Mode == BladeDisplayMuxMode.Discrete)
        {
            GpuModeStatusText.Text = AppStrings.Text("GpuModeStatusDiscreteRouteLocked");
            return;
        }

        _gpuModeBusy = true;
        GpuModeApplyResult result;
        try
        {
            result = await ApplyGpuModeAsync(target, snapshot, Lifetime);
        }
        catch (OperationCanceledException) when (Lifetime.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception) when (RuntimeErrors.IsExpected(exception))
        {
            result = new(string.Empty, false);
        }
        finally
        {
            _gpuModeBusy = false;
        }

        await RefreshGpuModeAsync();
        if (result.Message.Length > 0)
        {
            GpuModeStatusText.Text = result.Message;
        }
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
        GpuModeStatusText.Text = AppStrings.Text("DedicatedGpuModeStatusRestarting");

        DedicatedGpuSwitchResult result;
        try
        {
            result = await WindowsDedicatedGpuMode.RestartFaultedAsync(Lifetime);
        }
        catch (OperationCanceledException) when (Lifetime.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            ViewModel.ReportApplicationError(AppStrings.FormatText(
                "DedicatedGpuModeError",
                exception.Message));
            result = new DedicatedGpuSwitchResult(DedicatedGpuSwitchOutcome.Failed);
        }
        finally
        {
            _gpuModeBusy = false;
        }

        if (Lifetime.IsCancellationRequested)
        {
            return;
        }

        RefreshDedicatedGpuMode();

        switch (result.Outcome)
        {
            case DedicatedGpuSwitchOutcome.Success:
                GpuModeStatusText.Text = AppStrings.Text("DedicatedGpuModeStatusRestarted");
                break;
            case DedicatedGpuSwitchOutcome.RestartRequired:
                GpuModeStatusText.Text = AppStrings.Text("DedicatedGpuModeStatusRestartRequired");
                break;
            case DedicatedGpuSwitchOutcome.Cancelled:
                GpuModeStatusText.Text = AppStrings.Text("DedicatedGpuModeStatusCancelled");
                break;
            case DedicatedGpuSwitchOutcome.NoDevice:
                GpuModeStatusText.Text = AppStrings.Text("DedicatedGpuModeStatusNoDevice");
                break;
            case DedicatedGpuSwitchOutcome.Failed:
                GpuModeStatusText.Text = AppStrings.FormatText(
                    "DedicatedGpuModeStatusFailed",
                    result.ExitCode);
                break;
        }
    }

}