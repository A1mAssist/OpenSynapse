using Microsoft.UI.Xaml;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;

namespace OpenSynapse.App.ViewModels;

/// <summary>
/// Intel RAPL package wall. The index is user intent; the register is only written from
/// <see cref="ApplyCpuPowerLimitAsync"/>, never while a report refreshes the picker.
/// </summary>
public sealed partial class MainViewModel
{
    private static readonly double[] CpuPowerLimitPresets =
        [0, 15, 25, 35, 45, 55, 65, 75, 85, 95];

    private CpuPowerLimitReport? _cpuPowerLimitReport;
    private int _cpuPowerLimitIndex = -1;
    private bool _isRefreshingCpuPowerLimit;
    private bool _cpuPowerLimitBusyWatchAttached;

    public IReadOnlyList<string> CpuPowerLimitOptions =>
    [
        AppStrings.Text("CpuPowerLimitAuto"),
        .. CpuPowerLimitPresets.Skip(1).Select(watts => $"{watts:0} W"),
    ];

    /// <summary>Hidden states would leave the user with an unexplained missing feature.</summary>
    public Visibility CpuPowerLimitVisibility => Visibility.Visible;

    public int CpuPowerLimitIndex
    {
        get => _cpuPowerLimitIndex;
        set => SetField(ref _cpuPowerLimitIndex, value);
    }

    public string CpuPowerLimitText => _cpuPowerLimitReport switch
    {
        null => "--",
        { Availability: CpuPowerLimitAvailability.Available } report =>
            AppStrings.FormatText("CpuPowerLimitValue", report.Pl1Watts, report.Pl2Watts),
        { Availability: CpuPowerLimitAvailability.PawnIoMissing } =>
            AppStrings.Text("CpuPowerLimitPawnIoMissing"),
        { Availability: CpuPowerLimitAvailability.ModuleMissing } =>
            AppStrings.Text("CpuPowerLimitModuleMissing"),
        { Availability: CpuPowerLimitAvailability.AccessDenied } =>
            AppStrings.Text("CpuPowerLimitAccessDenied"),
        _ => AppStrings.Text("CpuPowerLimitUnavailable"),
    };

    public string CpuPowerLimitDetailText => _cpuPowerLimitReport switch
    {
        null => AppStrings.Text("CpuPowerLimitChecking"),
        { IsLocked: true } => AppStrings.Text("CpuPowerLimitLocked"),
        { Availability: CpuPowerLimitAvailability.Available } report =>
            AppStrings.FormatText("CpuPowerLimitCeiling", report.MaximumWatts),
        _ => string.Empty,
    };

    public Visibility CpuPowerLimitDetailVisibility =>
        CpuPowerLimitDetailText.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

    public bool CanSetCpuPowerLimit =>
        !IsBusy &&
        _cpuPowerLimitReport is
        {
            Availability: CpuPowerLimitAvailability.Available,
            IsLocked: false,
        };

    public bool IsRefreshingCpuPowerLimit
    {
        get => _isRefreshingCpuPowerLimit;
        private set => _isRefreshingCpuPowerLimit = value;
    }

    public async Task RefreshCpuPowerLimitAsync(CancellationToken cancellationToken = default)
    {
        EnsureCpuPowerLimitBusyWatch();

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

        if (report.Availability == CpuPowerLimitAvailability.Available)
        {
            CpuPowerBaselineStore.Instance.Capture(report);
        }

        PublishCpuPowerLimit(report);
    }

    /// <summary>Startup path: a failed read must explain itself, not fault the window.</summary>
    public async Task RefreshCpuPowerLimitSafelyAsync()
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

    public async Task ApplyCpuPowerLimitAsync(CancellationToken cancellationToken = default)
    {
        var requestedIndex = _cpuPowerLimitIndex;
        if (requestedIndex < 0 || requestedIndex >= CpuPowerLimitPresets.Length)
        {
            return;
        }

        await RunDeviceOperationAsync(
            AppStrings.Text("CpuPowerLimitTitle"),
            async () =>
            {
                var report = requestedIndex == 0
                    ? await CpuPowerLimitBroker.RestoreAsync(
                        CpuPowerBaselineStore.Instance.Current,
                        cancellationToken)
                    : await ApplyCpuPowerLimitPresetAsync(requestedIndex, cancellationToken);
                PublishCpuPowerLimit(report);
                if (report.Availability != CpuPowerLimitAvailability.Available)
                {
                    throw new InvalidOperationException(
                        CpuPowerLimitDetailText is { Length: > 0 } reason
                            ? reason
                            : AppStrings.Text("CpuPowerLimitUnavailable"));
                }
            },
            cancellationToken,
            () => RestoreCpuPowerLimitIndex(requestedIndex));

        // The report is published while RunDeviceOperationAsync still holds IsBusy, so the
        // picker would stay disabled. Re-raise CanSet now that the operation has cleared it.
        OnPropertyChanged(nameof(CanSetCpuPowerLimit));
    }

    /// <summary>
    /// Resume replay: firmware resets the wall during sleep, so write the chosen preset
    /// again. Auto (0) must not overwrite the firmware default, and a failure here is
    /// background noise rather than a user-visible error.
    /// </summary>
    public async Task ReplayCpuPowerLimitAfterResumeAsync(
        CancellationToken cancellationToken = default)
    {
        var requestedIndex = _cpuPowerLimitIndex;
        if (requestedIndex <= 0 || requestedIndex >= CpuPowerLimitPresets.Length)
        {
            return;
        }

        try
        {
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

    private void PublishCpuPowerLimit(CpuPowerLimitReport report)
    {
        _cpuPowerLimitReport = report;
        // A firmware default (150 W) is not a preset; a blank picker would hide that the
        // card is on Automatic.
        var index = report.Availability == CpuPowerLimitAvailability.Available
            ? Math.Max(0, Array.IndexOf(CpuPowerLimitPresets, report.Pl1Watts))
            : -1;
        SetCpuPowerLimitIndex(index);
        OnPropertyChanged(nameof(CpuPowerLimitText));
        OnPropertyChanged(nameof(CpuPowerLimitDetailText));
        OnPropertyChanged(nameof(CpuPowerLimitDetailVisibility));
        OnPropertyChanged(nameof(CanSetCpuPowerLimit));
    }

    private void RestoreCpuPowerLimitIndex(int index) => SetCpuPowerLimitIndex(index);

    /// <summary>
    /// CanSet derives from IsBusy, and IsBusy's own notification does not cover derived
    /// properties. The view model owns the picker, so it re-raises the derived one itself.
    /// </summary>
    private void EnsureCpuPowerLimitBusyWatch()
    {
        if (_cpuPowerLimitBusyWatchAttached)
        {
            return;
        }

        _cpuPowerLimitBusyWatchAttached = true;
        PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is null or "" || args.PropertyName == nameof(IsBusy))
            {
                OnPropertyChanged(nameof(CanSetCpuPowerLimit));
            }
        };
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
}
