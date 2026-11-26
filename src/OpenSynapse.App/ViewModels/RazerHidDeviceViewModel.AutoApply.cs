using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHidDeviceViewModel
{
    private RazerHidDpiStages? _confirmedMouseDpiStages;

    internal Func<Task<string?>>? PreparePollingAutoApply(CancellationToken token) =>
        PrepareMouseApply(SelectedPollingRate, _basicState?.PollingRate,
            PollingOptions.Contains(SelectedPollingRate), () => CanWritePolling,
            value => SelectedPollingRate = value, ApplyPollingAsync, token);

    internal Func<Task<string?>>? PrepareDpiAutoApply(CancellationToken token) =>
        PrepareMouseApply((DpiX, DpiY),
            _basicState is { DpiX: { } x, DpiY: { } y } ? (x, y) : null,
            DpiX >= 100 && DpiY >= 100 && DpiX <= MaximumDpi && DpiY <= MaximumDpi,
            () => CanWriteDpi, value => (DpiX, DpiY) = value, ApplyDpiAsync, token);

    internal Func<Task<string?>>? PrepareIdleAutoApply(CancellationToken token) =>
        PrepareMouseApply(IdleTimeoutSeconds, _basicState?.IdleTimeoutSeconds,
            IdleTimeoutSeconds is >= 60 and <= 900, () => CanWriteIdle,
            value => IdleTimeoutSeconds = value, ApplyIdleTimeoutAsync, token);

    internal Func<Task<string?>>? PrepareLowBatteryAutoApply(CancellationToken token) =>
        PrepareMouseApply(LowBatteryThresholdPercent, _basicState?.LowBatteryThresholdPercent,
            LowBatteryThresholdPercent is >= 5 and <= 100 && LowBatteryThresholdPercent % 5 == 0,
            () => CanWriteLowBattery, value => LowBatteryThresholdPercent = value,
            ApplyLowBatteryThresholdAsync, token);

    internal Func<Task<string?>>? PrepareAutoSleepAutoApply(CancellationToken token) =>
        PrepareMouseApply(AutoSleepEnabled, _basicState?.AutoSleepEnabled, true,
            () => CanWriteAutoSleep && !IsAutoSleepBusy, value => AutoSleepEnabled = value,
            ApplyAutoSleepAsync, token);

    internal Func<Task<string?>>? PrepareChargeLimiterAutoApply(CancellationToken token) =>
        PrepareMouseApply((ChargeLimiterEnabled, ChargeLimiterPercent),
            _basicState is { BatteryChargeLimiterEnabled: { } enabled, BatteryChargeLimiterPercent: { } percent }
                ? (enabled, percent) : null,
            ChargeLimiterPercent is >= 50 and <= 95 && ChargeLimiterPercent % 5 == 0,
            () => CanWriteChargeLimiter, value => (ChargeLimiterEnabled, ChargeLimiterPercent) = value,
            ApplyChargeLimiterAsync, token);

    internal Func<Task<string?>>? PrepareTimeToDimAutoApply(CancellationToken token) =>
        PrepareMouseApply((TimeToDimEnabled, TimeToDimSeconds, TimeToDimPercent),
            _basicState is { TimeToDimEnabled: { } enabled, TimeToDimSeconds: { } seconds, TimeToDimPercent: { } percent }
                ? (enabled, Math.Clamp(seconds, 60, 900), Math.Clamp(percent >= 1 ? (int)percent : 20, 1, 100)) : null,
            TimeToDimSeconds is >= 60 and <= 900 && TimeToDimPercent is >= 1 and <= 100,
            () => CanWriteTimeToDim, value => (TimeToDimEnabled, TimeToDimSeconds, TimeToDimPercent) = value,
            ApplyTimeToDimAsync, token);

    internal Func<Task<string?>>? PrepareIndicatorAutoApply(CancellationToken token) =>
        PrepareMouseApply(IndicatorLedEnabled,
            _basicState?.IndicatorLedConfiguration is { } value ? value != 0 : null,
            true, () => CanWriteIndicatorLed, value => IndicatorLedEnabled = value, ApplyIndicatorLedAsync, token);

    internal Func<Task<string?>>? PrepareIndicatorV2AutoApply(CancellationToken token) =>
        PrepareMouseApply((IndicatorLed1Index, IndicatorLed2Index, IndicatorLed3Index),
            _basicState?.IndicatorLedConfigurationV2 is { } value
                ? (Math.Clamp((int)value.Led1, 0, 4), Math.Clamp((int)value.Led2, 0, 4), Math.Clamp((int)value.Led3, 0, 4)) : null,
            true, () => CanWriteIndicatorLedV2,
            value => (IndicatorLed1Index, IndicatorLed2Index, IndicatorLed3Index) = value, ApplyIndicatorLedV2Async, token);

    internal Func<Task<string?>>? PrepareBatteryChemistryAutoApply(CancellationToken token) =>
        PrepareMouseApply(SelectedBatteryCurveIndex,
            _basicState?.BatteryDischargeCurve is { } value ? Math.Clamp((int)value, 0, 2) : null,
            SelectedBatteryCurveIndex is >= 0 and <= 2, () => CanWriteBatteryCurve,
            value => SelectedBatteryCurveIndex = value, ApplyBatteryCurveAsync, token);

    internal Func<Task<string?>>? PrepareDpiStagesAutoApply(CancellationToken token)
    {
        if (_confirmedMouseDpiStages is not { } confirmed || DpiStages.Count is < 1 or > 5 ||
            ActiveDpiStage < 1 || ActiveDpiStage > DpiStages.Count ||
            DpiStages.Any(row => row.X < 100 || row.Y < 100 || row.X > MaximumDpi || row.Y > MaximumDpi)) return null;
        var draft = new RazerHidDpiStages(ActiveDpiStage,
            DpiStages.Select(row => new RazerHidDpiStage(row.Number, row.X, row.Y, row.Z)).ToArray());
        if (draft.ActiveStage == confirmed.ActiveStage && draft.Stages.SequenceEqual(confirmed.Stages)) return null;
        return CaptureMouseApply(() => CanWriteDpiStages,
            () => ReplaceDpiStages(draft, confirm: false), ApplyDpiStagesAsync, token);
    }

    private Func<Task<string?>>? PrepareMouseApply<T>(T draft, T? confirmed, bool valid,
        Func<bool> canApply, Action<T> restoreDraft, Func<CancellationToken, Task> apply, CancellationToken token)
        where T : struct =>
        !valid || confirmed is null || EqualityComparer<T>.Default.Equals(draft, confirmed.Value)
            ? null : CaptureMouseApply(canApply, () => restoreDraft(draft), apply, token);

    private Func<Task<string?>> CaptureMouseApply(Func<bool> canApply, Action restoreDraft,
        Func<CancellationToken, Task> apply, CancellationToken token)
    {
        var connection = Connection;
        var profile = _getActiveProfile?.Invoke();
        var document = _getAutoApplyProfileDocument?.Invoke();
        var profileName = _getAutoApplyProfileDocument?.Invoke().ActiveProfileName;
        var powerSource = _powerStateProvider?.Invoke();
        var onboardProfile = ActiveOnboardProfileId;
        return () => _service.RunExclusiveAsync<string?>(connection, async cancellationToken =>
        {
            if (_disposed != 0 || !ReferenceEquals(connection, Connection) ||
                !canApply() || !ReferenceEquals(profile, _getActiveProfile?.Invoke()))
                return AppStrings.Text("SettingAutoApplyCancelled");
            if (onboardProfile != ActiveOnboardProfileId || powerSource != _powerStateProvider?.Invoke() ||
                profileName != _getAutoApplyProfileDocument?.Invoke().ActiveProfileName ||
                !ReferenceEquals(document, _getAutoApplyProfileDocument?.Invoke()))
                return AppStrings.Text("SettingAutoApplyCancelled");
            restoreDraft();
            _operationSucceeded = false;
            await apply(cancellationToken);
            if (!string.IsNullOrEmpty(ErrorText)) return ErrorText;
            if (!_operationSucceeded) return AppStrings.Text("SettingAutoApplyCancelled");
            return _operationStatusOverride is { } status && status != AppStrings.Text("RazerHidOperationSaved")
                ? status : null;
        }, token);
    }
}
