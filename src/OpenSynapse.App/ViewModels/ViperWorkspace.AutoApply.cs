using OpenSynapse.Core.Devices;

namespace OpenSynapse.App.ViewModels;

public sealed partial class ViperWorkspace
{
    internal object? MouseAutoApplyContext => _statusDeviceId;
    private DeviceDescriptor? MouseAutoApplyDevice => _deviceDescriptors().FirstOrDefault(device =>
        device.ProtocolFamily == DeviceProtocolFamilies.Viper &&
        device.Access == DeviceAccessState.Available &&
        StringComparer.OrdinalIgnoreCase.Equals(device.Id, _statusDeviceId));

    internal Func<Task<string?>>? PreparePollingAutoApply(CancellationToken token)
    {
        var value = PollingRateIndex;
        return !CanSetPollingRate || value is < 0 or > 2 || value == _confirmedPollingRateIndex
            ? null : CaptureMouseApply(() => PollingRateIndex = value, () => ApplyPollingRateAsync(token));
    }

    internal Func<Task<string?>>? PrepareDpiAutoApply(CancellationToken token)
    {
        var (x, y) = (DpiXValue, DpiYValue);
        return !CanSetDpi || !ValidMouseNumber(x, 100, 30000) || !ValidMouseNumber(y, 100, 30000) ||
            (x == _confirmedDpiXValue && y == _confirmedDpiYValue)
            ? null : CaptureMouseApply(() => (DpiXValue, DpiYValue) = (x, y), () => ApplyDpiAsync(token));
    }

    internal Func<Task<string?>>? PrepareIdleAutoApply(CancellationToken token)
    {
        var value = IdleMinutesValue;
        return !CanSetIdle || !ValidMouseNumber(value, 1, 15) || value == _confirmedIdleMinutesValue
            ? null : CaptureMouseApply(() => IdleMinutesValue = value, () => ApplyIdleAsync(token));
    }

    internal Func<Task<string?>>? PrepareLowBatteryAutoApply(CancellationToken token)
    {
        var value = LowBatteryThresholdPercent;
        return !CanSetLowBatteryThreshold || !ValidMouseNumber(value, 5, 100) ||
            value == _confirmedLowBatteryThresholdPercent
            ? null : CaptureMouseApply(() => LowBatteryThresholdPercent = value,
                () => ApplyLowBatteryThresholdAsync(token));
    }

    internal Func<Task<string?>>? PrepareBatteryChemistryAutoApply(CancellationToken token)
    {
        var value = BatteryChemistryIndex;
        return !CanSetBatteryChemistry || value is < 0 or > 2 || value == _confirmedBatteryChemistryIndex
            ? null : CaptureMouseApply(() => BatteryChemistryIndex = value,
                () => ApplyBatteryChemistryAsync(token));
    }

    internal Func<Task<string?>>? PrepareDpiStagesAutoApply(CancellationToken token)
    {
        if (!CanSetDpiStages || DpiStages.Count is < 1 or > 5 ||
            ActiveDpiStage < 1 || ActiveDpiStage > DpiStages.Count ||
            DpiStages.Any(row => !ValidMouseNumber(row.X, 100, 30000) || !ValidMouseNumber(row.Y, 100, 30000)))
            return null;
        var draft = new ViperDpiStagesTelemetry((byte)ActiveDpiStage,
            DpiStages.Select(row => new ViperDpiStageTelemetry((byte)row.Number, (int)row.X, (int)row.Y)).ToArray());
        if (_confirmedDpiStages is { } confirmed && draft.ActiveStage == confirmed.ActiveStage &&
            draft.Stages.SequenceEqual(confirmed.Stages)) return null;
        return CaptureMouseApply(() => SetDpiStages(draft, confirm: false), () => ApplyDpiStagesAsync(token));
    }

    private Func<Task<string?>>? CaptureMouseApply(Action restoreDraft, Func<Task<string?>> apply)
    {
        if (MouseAutoApplyDevice is not { } target) return null;
        var profile = Profile;
        return async () =>
        {
            if (!StringComparer.OrdinalIgnoreCase.Equals(target.Id, MouseAutoApplyContext) ||
                !ReferenceEquals(profile, Profile)) return AppStrings.Text("SettingAutoApplyCancelled");
            restoreDraft();
            return await apply();
        };
    }

    private static bool ValidMouseNumber(double value, double minimum, double maximum) =>
        double.IsFinite(value) && value >= minimum && value <= maximum;

    private void EnsureMouseApplyTarget(DeviceDescriptor target)
    {
        if (!StringComparer.OrdinalIgnoreCase.Equals(target.Id, MouseAutoApplyContext))
            throw new InvalidOperationException(AppStrings.Text("SettingAutoApplyCancelled"));
    }
}
