using Microsoft.UI.Xaml;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerBleDeviceViewModel
{
    private int? _confirmedMousePolling;
    private (int X, int Y)? _confirmedMouseDpi;
    private RazerBleDpiStages? _confirmedMouseDpiStages;
    private RazerBlePowerState? _confirmedMousePower;
    private int? _confirmedMousePowerPlan;
    private int? _confirmedMouseChemistry;

    internal Func<Task<string?>>? PreparePollingAutoApply(CancellationToken token)
    {
        if (SelectedPollingIndex < 0 || SelectedPollingIndex >= PollingRates.Count) return null;
        var value = PollingRates[SelectedPollingIndex];
        return _confirmedMousePolling is null || value == _confirmedMousePolling
            ? null : CaptureMouseApply(() => ApplyPollingRateAsync(value, token));
    }

    internal Func<Task<string?>>? PrepareDpiAutoApply(CancellationToken token)
    {
        if (!ValidMouseNumber(DpiX, 1, 65535) || !ValidMouseNumber(DpiY, 1, 65535)) return null;
        var value = ((int)Math.Round(DpiX), (int)Math.Round(DpiY));
        return _confirmedMouseDpi is null || value == _confirmedMouseDpi
            ? null : CaptureMouseApply(() => ApplyDpiAsync(value, token));
    }

    private RazerBleDpiStages CaptureMouseDpiStages() => new((byte)ActiveDpiStage,
        DpiStages.Select(row => new RazerBleDpiStage(row.Index,
            (int)Math.Round(row.X), (int)Math.Round(row.Y), row.Z)).ToArray());

    internal Func<Task<string?>>? PrepareDpiStagesAutoApply(CancellationToken token)
    {
        if (_confirmedMouseDpiStages is not { } confirmed || DpiStages.Count is < 1 or > 5 ||
            ActiveDpiStage < 1 || ActiveDpiStage > DpiStages.Count ||
            DpiStages.Any(row => !ValidMouseNumber(row.X, 1, 65535) || !ValidMouseNumber(row.Y, 1, 65535))) return null;
        var value = CaptureMouseDpiStages();
        return value.ActiveStage == confirmed.ActiveStage && value.Stages.SequenceEqual(confirmed.Stages)
            ? null : CaptureMouseApply(() => ApplyDpiStagesAsync(value, token));
    }

    private RazerBlePowerState CaptureMousePower() => new(
        PowerLowBatteryVisibility == Visibility.Visible ? (int)Math.Round(LowBatteryThresholdPercent) : null,
        PowerAutoSleepVisibility == Visibility.Visible ? AutoSleepEnabled : null,
        PowerSleepTimeVisibility == Visibility.Visible ? (int)Math.Round(SleepMinutes) : null,
        PowerTimeToDimVisibility == Visibility.Visible
            ? new RazerBleTimeToDimState(TimeToDimEnabled, (int)Math.Round(TimeToDimMinutes * 60), _timeToDimPercentage)
            : null, Array.Empty<string>());

    internal Func<Task<string?>>? PreparePowerAutoApply(CancellationToken token)
    {
        if (_confirmedMousePower is not { } confirmed ||
            (PowerLowBatteryVisibility == Visibility.Visible && !ValidMouseNumber(LowBatteryThresholdPercent, 0, 100)) ||
            (PowerSleepTimeVisibility == Visibility.Visible && !ValidMouseNumber(SleepMinutes, 1, 65535)) ||
            (PowerTimeToDimVisibility == Visibility.Visible && !ValidMouseNumber(TimeToDimMinutes, 1, 15))) return null;
        var value = CaptureMousePower();
        return value.LowBatteryPercent == confirmed.LowBatteryPercent && value.AutoSleep == confirmed.AutoSleep &&
            value.SleepMinutes == confirmed.SleepMinutes && value.TimeToDim == confirmed.TimeToDim
            ? null : CaptureMouseApply(() => ApplyPowerAsync(value, token));
    }

    internal Func<Task<string?>>? PreparePowerPlanAutoApply(CancellationToken token)
    {
        var value = SelectedPowerPlanIndex;
        return PowerPlanVisibility != Visibility.Visible || _confirmedMousePowerPlan is null ||
            value is < 0 or > 2 || value == _confirmedMousePowerPlan
            ? null : CaptureMouseApply(() => ApplyPowerPlanAsync(value, token));
    }

    internal Func<Task<string?>>? PrepareBatteryChemistryAutoApply(CancellationToken token)
    {
        var value = SelectedBatteryChemistryIndex;
        return BatteryChemistryVisibility != Visibility.Visible || _confirmedMouseChemistry is null ||
            value is < 0 or > 2 || value == _confirmedMouseChemistry
            ? null : CaptureMouseApply(() => ApplyBatteryChemistryAsync(value, token));
    }

    private Func<Task<string?>>? CaptureMouseApply(Func<Task<bool>> apply)
    {
        if (_service is not { } service) return null;
        var profile = _getProfile?.Invoke();
        var profileName = profile?.ActiveProfileName;
        var onboardProfile = service.MappingProfileId;
        return async () =>
        {
            if (!CanEdit || !ReferenceEquals(service, _service) || !ReferenceEquals(profile, _getProfile?.Invoke()))
                return AppStrings.Text("SettingAutoApplyCancelled");
            if (profileName != _getProfile?.Invoke().ActiveProfileName || onboardProfile != service.MappingProfileId)
                return AppStrings.Text("SettingAutoApplyCancelled");
            return await apply() ? null : string.IsNullOrEmpty(ErrorText)
                ? AppStrings.Text("SettingAutoApplyCancelled") : ErrorText;
        };
    }

    private static bool ValidMouseNumber(double value, double minimum, double maximum) =>
        double.IsFinite(value) && value >= minimum && value <= maximum;
}
