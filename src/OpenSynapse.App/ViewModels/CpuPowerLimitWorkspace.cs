using Microsoft.UI.Xaml;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Windows.Devices;

namespace OpenSynapse.App.ViewModels;

/// <summary>
/// Intel RAPL package wall. The index is user intent; the register is only written from
/// <see cref="ApplyCpuPowerLimitAsync"/>, never while a report refreshes the picker.
/// </summary>
public sealed class CpuPowerLimitWorkspace : INotifyPropertyChanged
{
    private static readonly double[] CpuPowerLimitPresets =
        [0, 15, 25, 35, 45, 55, 65, 75, 85, 95];

    /// <summary>The component Razer ships with Synapse for AMD PPT control.</summary>
    internal const string AmdOverClockDownloadUri =
        "https://app-assets.razer.com/files/synapse/devices/common/blades/RazerAMDOverClockSetup-v1.0.4.0.exe";

    internal const string PawnIoDownloadUri =
        "https://github.com/namazso/PawnIO.Setup/releases/latest";

    private CpuPowerLimitReport? _cpuPowerLimitReport;
    private int _cpuPowerLimitIndex = -1;
    private bool _isRefreshingCpuPowerLimit;
    private readonly Func<bool> _isBusy;
    private readonly Func<BladePerformanceMode?> _getPerformanceMode;
    private readonly Func<string, Func<Task>, CancellationToken, Action?, Task> _runOperation;
    private readonly Func<BladeProfileSettings> _getProfileSettings;
    private readonly Func<CancellationToken, Task<bool>> _saveProfileAsync;

    internal CpuPowerLimitWorkspace(Func<bool> isBusy,
        Func<BladePerformanceMode?> getPerformanceMode,
        Func<string, Func<Task>, CancellationToken, Action?, Task> runOperation,
        Func<BladeProfileSettings> getProfileSettings,
        Func<CancellationToken, Task<bool>> saveProfileAsync)
    {
        _isBusy = isBusy;
        _getPerformanceMode = getPerformanceMode;
        _runOperation = runOperation;
        _getProfileSettings = getProfileSettings;
        _saveProfileAsync = saveProfileAsync;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<string> CpuPowerLimitOptions =>
    [
        AppStrings.Text("CpuPowerLimitAuto"),
        .. CpuPowerLimitPresets.Skip(1).Select(watts => $"{watts:0} W"),
    ];

    public int CpuPowerLimitIndex
    {
        get => _cpuPowerLimitIndex;
        set => SetField(ref _cpuPowerLimitIndex, value);
    }

    public string CpuPowerLimitText => _cpuPowerLimitReport switch
    {
        null => "--",
        { Availability: CpuPowerLimitAvailability.Available } report =>
            AppStrings.FormatText("CpuPowerLimitValue", report.Pl1Watts, report.Pl2Watts) +
            VoltageOffsetSuffix(report.VoltageOffsetMillivolts),
        { Availability: CpuPowerLimitAvailability.PawnIoMissing } =>
            AppStrings.Text("CpuPowerLimitPawnIoMissing"),
        { Availability: CpuPowerLimitAvailability.ModuleMissing } =>
            AppStrings.Text("CpuPowerLimitModuleMissing"),
        { Availability: CpuPowerLimitAvailability.AccessDenied } =>
            AppStrings.Text("CpuPowerLimitAccessDenied"),
        { Availability: CpuPowerLimitAvailability.AmdDriverMissing } =>
            AppStrings.Text("CpuPowerLimitAmdDriverMissing"),
        _ => AppStrings.Text("CpuPowerLimitUnavailable"),
    };

    // Numeric plus unit reads the same in every locale we ship, so it stays out of the
    // nine resource files until a write path exists and the wording matters.
    private static string VoltageOffsetSuffix(int? millivolts) =>
        millivolts is { } value
            ? $" · {(value >= 0 ? "+" : "-")}{Math.Abs(value)} mV"
            : string.Empty;

    public string CpuPowerLimitDetailText => _cpuPowerLimitReport switch
    {
        null => AppStrings.Text("CpuPowerLimitChecking"),
        { IsLocked: true } => AppStrings.Text("CpuPowerLimitLocked"),
        { Availability: CpuPowerLimitAvailability.Available, UsesDirectSmu: true } report =>
            $"{AppStrings.FormatText("CpuPowerLimitCeiling", report.MaximumWatts)} · " +
            AppStrings.Text("CpuPowerLimitDirectSmu"),
        { Availability: CpuPowerLimitAvailability.Available } report =>
            AppStrings.FormatText("CpuPowerLimitCeiling", report.MaximumWatts),
        { Detail: { Length: > 0 } detail } => detail,
        _ => string.Empty,
    };

    public Visibility CpuPowerLimitDetailVisibility =>
        CpuPowerLimitDetailText.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility CpuPowerLimitAmdDownloadVisibility =>
        _cpuPowerLimitReport?.Availability == CpuPowerLimitAvailability.AmdDriverMissing
            ? Visibility.Visible
            : Visibility.Collapsed;

    public Visibility CpuPowerLimitPawnIoInstallVisibility =>
        _cpuPowerLimitReport?.Availability == CpuPowerLimitAvailability.PawnIoMissing
            ? Visibility.Visible
            : Visibility.Collapsed;

    public bool CanInstallCpuPowerLimitPawnIo =>
        !_isBusy() && _cpuPowerLimitReport?.Availability == CpuPowerLimitAvailability.PawnIoMissing;

    public bool CanSetCpuPowerLimit =>
        !_isBusy() &&
        CpuPowerLimit.SupportsPerformanceMode(_getPerformanceMode()) &&
        _cpuPowerLimitReport is
        {
            Availability: CpuPowerLimitAvailability.Available,
            IsLocked: false,
        };

    internal bool IsRefreshingCpuPowerLimit
    {
        get => _isRefreshingCpuPowerLimit;
        private set => _isRefreshingCpuPowerLimit = value;
    }

    private async Task RefreshCpuPowerLimitAsync(CancellationToken cancellationToken = default)
    {
        var mode = _getPerformanceMode();
        CpuPowerLimitReport report;
        try
        {
            report = await CpuPowerLimitBroker.ReadAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            report = CpuPowerLimitReport.Unavailable(
                CpuPowerLimitAvailability.Failed,
                exception.Message);
        }

        if (report.Availability == CpuPowerLimitAvailability.Available && mode == _getPerformanceMode())
        {
            CpuPowerBaselineStore.ForMode(mode).Capture(
                report,
                allowCapture: mode is not null and not (BladePerformanceMode.Custom or BladePerformanceMode.Hyperboost));
        }

        PublishCpuPowerLimit(report);
    }

    /// <summary>Startup path: a failed read must explain itself, not fault the window.</summary>
    internal async Task RefreshCpuPowerLimitSafelyAsync()
    {
        try
        {
            await RefreshCpuPowerLimitAsync();
        }
        catch (Exception exception)
        {
            PublishCpuPowerLimit(CpuPowerLimitReport.Unavailable(
                CpuPowerLimitAvailability.Failed,
                exception.Message));
        }
    }

    [ProfileSetting("blade.cpu-power-limit", DeviceCategory.Laptop, powerScoped: true)]
    public async Task ApplyCpuPowerLimitAsync(CancellationToken cancellationToken = default)
    {
        var requestedIndex = _cpuPowerLimitIndex;
        var mode = _getPerformanceMode();
        if (requestedIndex < 0 || requestedIndex >= CpuPowerLimitPresets.Length ||
            !CpuPowerLimit.SupportsPerformanceMode(mode) ||
            _cpuPowerLimitReport is not { Availability: CpuPowerLimitAvailability.Available, IsLocked: false })
        {
            return;
        }

        var target = _getProfileSettings();
        var previousProfile = target.CpuPowerLimitWatts;
        var previousReport = _cpuPowerLimitReport;
        await _runOperation(
            AppStrings.Text("CpuPowerLimitTitle"),
            async () =>
            {
                await CpuPowerBaselineStore.CaptureHyperBoostAsync(mode, cancellationToken);
                if (mode != _getPerformanceMode())
                    throw new InvalidOperationException(AppStrings.Text("CpuPowerLimitUnavailable"));
                var report = requestedIndex == 0
                    ? await RestoreAutoCpuPowerLimitAsync(mode, cancellationToken)
                    : await ApplyCpuPowerLimitPresetAsync(requestedIndex, cancellationToken);
                if (report.Availability != CpuPowerLimitAvailability.Available)
                {
                    PublishCpuPowerLimit(report);
                    throw new InvalidOperationException(
                        CpuPowerLimitDetailText is { Length: > 0 } reason
                            ? reason
                            : AppStrings.Text("CpuPowerLimitUnavailable"));
                }

                target.CpuPowerLimitWatts = requestedIndex == 0
                    ? 0
                    : (int)CpuPowerLimitPresets[requestedIndex];
                try
                {
                    if (!await _saveProfileAsync(cancellationToken))
                    {
                        throw new InvalidOperationException(
                            AppStrings.Text("CpuPowerLimitUnavailable"));
                    }
                }
                catch
                {
                    target.CpuPowerLimitWatts = previousProfile;
                    await RestorePowerReportAsync(previousReport);
                    throw;
                }

                PublishCpuPowerLimit(report);
            },
            cancellationToken,
            () => SetCpuPowerLimitIndex(previousProfile is int watts
                ? Array.IndexOf(CpuPowerLimitPresets, (double)watts) : 0));

        // The report is published while RunDeviceOperationAsync still holds IsBusy, so the
        // picker would stay disabled. Re-raise CanSet now that the operation has cleared it.
        OnPropertyChanged(nameof(CanSetCpuPowerLimit));
        OnPropertyChanged(nameof(CanInstallCpuPowerLimitPawnIo));
    }

    /// <summary>
    /// Firmware resets the wall during sleep and when the performance preset changes,
    /// so write the chosen preset again. Auto (0) must not overwrite the firmware
    /// default, and a failure here is background noise rather than a user-visible error.
    /// </summary>
    internal async Task ReplayCpuPowerLimitAsync(
        CancellationToken cancellationToken = default)
    {
        var configured = _getProfileSettings().CpuPowerLimitWatts;
        var requestedIndex = configured is int watts && watts > 0
            ? Array.IndexOf(CpuPowerLimitPresets, (double)watts)
            : 0;
        if (requestedIndex <= 0 || requestedIndex >= CpuPowerLimitPresets.Length)
        {
            return;
        }

        var mode = _getPerformanceMode();
        if (!CpuPowerLimit.SupportsPerformanceMode(mode))
        {
            return;
        }

        try
        {
            await CpuPowerBaselineStore.CaptureHyperBoostAsync(mode, cancellationToken);
            if (mode != _getPerformanceMode()) return;
            var report = await ApplyCpuPowerLimitPresetAsync(requestedIndex, cancellationToken);
            if (report.Availability == CpuPowerLimitAvailability.Available)
            {
                PublishCpuPowerLimit(report);
            }
        }
        catch (Exception)
        {
        }
    }

    private static Task<CpuPowerLimitReport> ApplyCpuPowerLimitPresetAsync(
        int index,
        CancellationToken cancellationToken)
    {
        var pl1 = CpuPowerLimitPresets[index];
        return CpuPowerLimitBroker.ApplyAsync(
            new CpuPowerLimit(
                pl1,
                Math.Min(pl1 + 15, CpuPowerLimit.MaximumWatts),
                CpuPowerLimit.MinimumTimeSeconds,
                CpuPowerLimit.MaximumTimeSeconds),
            cancellationToken);
    }

    private static Task<CpuPowerLimitReport> RestoreAutoCpuPowerLimitAsync(
        BladePerformanceMode? mode,
        CancellationToken cancellationToken) =>
        CpuPowerBaselineStore.ForMode(mode).Current is { } baseline
            ? CpuPowerLimitBroker.RestoreAsync(baseline, cancellationToken)
            : Task.FromResult(CpuPowerLimitReport.Unavailable(
                CpuPowerLimitAvailability.Failed,
                "No trusted CPU power baseline is available for the current performance mode."));

    private static async Task RestorePowerReportAsync(CpuPowerLimitReport? report)
    {
        if (report is not { Availability: CpuPowerLimitAvailability.Available })
        {
            return;
        }

        try
        {
            await CpuPowerLimitBroker.RestoreAsync(new CpuPowerLimit(
                report.Pl1Watts, report.Pl2Watts,
                CpuPowerLimit.MinimumTimeSeconds, CpuPowerLimit.MaximumTimeSeconds), CancellationToken.None);
        }
        catch
        {
            // The save failure is the error the caller must see.
        }
    }

    private void PublishCpuPowerLimit(CpuPowerLimitReport report)
    {
        _cpuPowerLimitReport = report;
        // The picker shows the profile's intent, not the live register: an EC preset
        // such as Silent also reads 25 W but is not a user-selected 25 W override.
        var index = -1;
        if (report.Availability == CpuPowerLimitAvailability.Available)
        {
            var configured = _getProfileSettings().CpuPowerLimitWatts;
            index = configured is int watts
                ? Array.IndexOf(CpuPowerLimitPresets, (double)watts)
                : 0;
        }
        SetCpuPowerLimitIndex(index);
        NotifyStateChanged();
    }

    internal void NotifyBusyChanged()
    {
        OnPropertyChanged(nameof(CanSetCpuPowerLimit));
        OnPropertyChanged(nameof(CanInstallCpuPowerLimitPawnIo));
    }

    internal void NotifyModeChanged()
    {
        OnPropertyChanged(nameof(CanSetCpuPowerLimit));
        if (_getPerformanceMode() is not null and not BladePerformanceMode.Custom)
        {
            // The startup read can happen before the performance mode is known.
            // HyperBoost captures into its own store; Custom keeps the legacy baseline.
            _ = RefreshCpuPowerLimitSafelyAsync();
        }
    }

    /// <summary>
    /// The notification must stay inside the flag: the binding pushes the new index into the
    /// ComboBox, whose SelectionChanged would otherwise write the same value back (INV-06).
    /// </summary>
    private void SetCpuPowerLimitIndex(int index)
    {
        IsRefreshingCpuPowerLimit = true;
        try
        {
            _cpuPowerLimitIndex = index;
            OnPropertyChanged(nameof(CpuPowerLimitIndex));
        }
        finally
        {
            IsRefreshingCpuPowerLimit = false;
        }
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        OnPropertyChanged(name);
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private void NotifyStateChanged() => OnPropertyChanged(string.Empty);
}
