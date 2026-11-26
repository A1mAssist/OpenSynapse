namespace OpenSynapse.App.ViewModels;

public sealed partial class ViperWorkspace
{
    public async Task<string?> ApplyPollingRateAsync(CancellationToken cancellationToken = default)
    {
        var target = MouseAutoApplyDevice;
        if (target is null) return AppStrings.Text("SettingAutoApplyCancelled");
        if (!CanSetPollingRate) return AppStrings.Text("SettingAutoApplyCancelled");

        var hertz = PollingRateIndex switch { 0 => 125, 1 => 500, 2 => 1000, _ => 0 };
        return await RunDeviceOperationAsync(AppStrings.Text("Text_CAEC7015"), async () =>
        {
            var settings = Profile.Global.Viper;
            var previous = settings.PollingRateHertz;
            var actual = await _deviceTelemetryReader.SetViperPollingRateAsync(
                [target], hertz, cancellationToken);
            EnsureMouseApplyTarget(target);
            PollingRateText = $"{actual} Hz";
            PollingRateIndex = actual switch { 125 => 0, 500 => 1, _ => 2 };
            _confirmedPollingRateIndex = PollingRateIndex;
            if (!ReferenceEquals(settings, Profile.Global.Viper)) throw ProfileSaveFailed();
            settings.PollingRateHertz = actual;
            var saved = false;
            try { saved = await _saveProfileAsync(cancellationToken); }
            finally { if (!saved) settings.PollingRateHertz = previous; }
            if (!saved) throw ProfileSaveFailed();
        }, target, cancellationToken, () => PollingRateIndex = _confirmedPollingRateIndex);
    }

    public async Task<string?> ApplyDpiAsync(CancellationToken cancellationToken = default)
    {
        var target = MouseAutoApplyDevice;
        if (target is null) return AppStrings.Text("SettingAutoApplyCancelled");
        if (!CanSetDpi) return AppStrings.Text("SettingAutoApplyCancelled");

        var x = checked((int)Math.Round(DpiXValue, MidpointRounding.AwayFromZero));
        var y = checked((int)Math.Round(DpiYValue, MidpointRounding.AwayFromZero));
        return await RunDeviceOperationAsync(AppStrings.Text("Text_25083B1F"), async () =>
        {
            var settings = Profile.Global.Viper;
            var previousX = settings.DpiX;
            var previousY = settings.DpiY;
            var actual = await _deviceTelemetryReader.SetViperDpiAsync(
                [target], x, y, cancellationToken);
            EnsureMouseApplyTarget(target);
            DpiXValue = _confirmedDpiXValue = actual.X;
            DpiYValue = _confirmedDpiYValue = actual.Y;
            DpiText = $"{actual.X} × {actual.Y}";
            if (!ReferenceEquals(settings, Profile.Global.Viper)) throw ProfileSaveFailed();
            settings.DpiX = actual.X;
            settings.DpiY = actual.Y;
            var saved = false;
            try { saved = await _saveProfileAsync(cancellationToken); }
            finally
            {
                if (!saved)
                {
                    settings.DpiX = previousX;
                    settings.DpiY = previousY;
                }
            }
            if (!saved) throw ProfileSaveFailed();
        }, target, cancellationToken, () =>
        {
            DpiXValue = _confirmedDpiXValue;
            DpiYValue = _confirmedDpiYValue;
        });
    }

    public async Task<string?> ApplyIdleAsync(CancellationToken cancellationToken = default)
    {
        var target = MouseAutoApplyDevice;
        if (target is null) return AppStrings.Text("SettingAutoApplyCancelled");
        if (!CanSetIdle) return AppStrings.Text("SettingAutoApplyCancelled");

        var minutes = checked((int)Math.Round(IdleMinutesValue, MidpointRounding.AwayFromZero));
        return await RunDeviceOperationAsync(AppStrings.Text("Text_C41C6FC1"), async () =>
        {
            var settings = Profile.Global.Viper;
            var previous = settings.IdleSeconds;
            var actual = await _deviceTelemetryReader.SetViperIdleSecondsAsync(
                [target], checked(minutes * 60), cancellationToken);
            EnsureMouseApplyTarget(target);
            IdleMinutesValue = _confirmedIdleMinutesValue = actual / 60d;
            IdleText = FormatDuration(actual);
            if (!ReferenceEquals(settings, Profile.Global.Viper)) throw ProfileSaveFailed();
            settings.IdleSeconds = actual;
            var saved = false;
            try { saved = await _saveProfileAsync(cancellationToken); }
            finally { if (!saved) settings.IdleSeconds = previous; }
            if (!saved) throw ProfileSaveFailed();
        }, target, cancellationToken, () => IdleMinutesValue = _confirmedIdleMinutesValue);
    }

    public async Task<string?> ApplyBatteryChemistryAsync(CancellationToken cancellationToken = default)
    {
        var target = MouseAutoApplyDevice;
        if (target is null) return AppStrings.Text("SettingAutoApplyCancelled");
        if (!CanSetBatteryChemistry || BatteryChemistryIndex is < 0 or > 2) return AppStrings.Text("SettingAutoApplyCancelled");

        var chemistry = checked((byte)BatteryChemistryIndex);
        return await RunDeviceOperationAsync(AppStrings.Text("Text_94C31F83"), async () =>
        {
            var settings = Profile.Global.Viper;
            var previous = settings.BatteryChemistry;
            var actual = await _deviceTelemetryReader.SetViperBatteryChemistryAsync(
                [target], chemistry, cancellationToken);
            EnsureMouseApplyTarget(target);
            BatteryChemistryIndex = _confirmedBatteryChemistryIndex = actual;
            OnPropertyChanged(nameof(BatteryChemistryText));
            if (!ReferenceEquals(settings, Profile.Global.Viper)) throw ProfileSaveFailed();
            settings.BatteryChemistry = actual;
            var saved = false;
            try { saved = await _saveProfileAsync(cancellationToken); }
            finally { if (!saved) settings.BatteryChemistry = previous; }
            if (!saved) throw ProfileSaveFailed();
        }, target, cancellationToken);
    }

    private static InvalidOperationException ProfileSaveFailed() =>
        new(AppStrings.Text("ViperSettingAppliedProfileSaveFailed"));
}
