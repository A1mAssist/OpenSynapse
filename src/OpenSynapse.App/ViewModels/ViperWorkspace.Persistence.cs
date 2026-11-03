namespace OpenSynapse.App.ViewModels;

public sealed partial class ViperWorkspace
{
    public async Task ApplyPollingRateAsync(CancellationToken cancellationToken = default)
    {
        if (!CanSetPollingRate) return;

        var hertz = PollingRateIndex switch { 0 => 125, 1 => 500, 2 => 1000, _ => 0 };
        await RunDeviceOperationAsync(AppStrings.Text("Text_CAEC7015"), async () =>
        {
            var settings = Profile.Global.Viper;
            var previous = settings.PollingRateHertz;
            var actual = await _deviceTelemetryReader.SetViperPollingRateAsync(
                _deviceDescriptors(), hertz, cancellationToken);
            PollingRateText = $"{actual} Hz";
            PollingRateIndex = actual switch { 125 => 0, 500 => 1, _ => 2 };
            _confirmedPollingRateIndex = PollingRateIndex;
            if (!ReferenceEquals(settings, Profile.Global.Viper)) throw ProfileSaveFailed();
            settings.PollingRateHertz = actual;
            var saved = false;
            try { saved = await _saveProfileAsync(cancellationToken); }
            finally { if (!saved) settings.PollingRateHertz = previous; }
            if (!saved) throw ProfileSaveFailed();
        }, cancellationToken, () => PollingRateIndex = _confirmedPollingRateIndex);
    }

    public async Task ApplyDpiAsync(CancellationToken cancellationToken = default)
    {
        if (!CanSetDpi) return;

        await RunDeviceOperationAsync(AppStrings.Text("Text_25083B1F"), async () =>
        {
            var settings = Profile.Global.Viper;
            var previousX = settings.DpiX;
            var previousY = settings.DpiY;
            var x = checked((int)Math.Round(DpiXValue, MidpointRounding.AwayFromZero));
            var y = checked((int)Math.Round(DpiYValue, MidpointRounding.AwayFromZero));
            var actual = await _deviceTelemetryReader.SetViperDpiAsync(
                _deviceDescriptors(), x, y, cancellationToken);
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
        }, cancellationToken, () =>
        {
            DpiXValue = _confirmedDpiXValue;
            DpiYValue = _confirmedDpiYValue;
        });
    }

    public async Task ApplyIdleAsync(CancellationToken cancellationToken = default)
    {
        if (!CanSetIdle) return;

        await RunDeviceOperationAsync(AppStrings.Text("Text_C41C6FC1"), async () =>
        {
            var settings = Profile.Global.Viper;
            var previous = settings.IdleSeconds;
            var minutes = checked((int)Math.Round(IdleMinutesValue, MidpointRounding.AwayFromZero));
            var actual = await _deviceTelemetryReader.SetViperIdleSecondsAsync(
                _deviceDescriptors(), checked(minutes * 60), cancellationToken);
            IdleMinutesValue = _confirmedIdleMinutesValue = actual / 60d;
            IdleText = FormatDuration(actual);
            if (!ReferenceEquals(settings, Profile.Global.Viper)) throw ProfileSaveFailed();
            settings.IdleSeconds = actual;
            var saved = false;
            try { saved = await _saveProfileAsync(cancellationToken); }
            finally { if (!saved) settings.IdleSeconds = previous; }
            if (!saved) throw ProfileSaveFailed();
        }, cancellationToken, () => IdleMinutesValue = _confirmedIdleMinutesValue);
    }

    public async Task ApplyBatteryChemistryAsync(CancellationToken cancellationToken = default)
    {
        if (BatteryChemistryIndex is < 0 or > 2) return;

        var chemistry = checked((byte)BatteryChemistryIndex);
        await RunDeviceOperationAsync(AppStrings.Text("Text_94C31F83"), async () =>
        {
            var settings = Profile.Global.Viper;
            var previous = settings.BatteryChemistry;
            var actual = await _deviceTelemetryReader.SetViperBatteryChemistryAsync(
                _deviceDescriptors(), chemistry, cancellationToken);
            BatteryChemistryIndex = _confirmedBatteryChemistryIndex = actual;
            OnPropertyChanged(nameof(BatteryChemistryText));
            if (!ReferenceEquals(settings, Profile.Global.Viper)) throw ProfileSaveFailed();
            settings.BatteryChemistry = actual;
            var saved = false;
            try { saved = await _saveProfileAsync(cancellationToken); }
            finally { if (!saved) settings.BatteryChemistry = previous; }
            if (!saved) throw ProfileSaveFailed();
        }, cancellationToken);
    }

    private static InvalidOperationException ProfileSaveFailed() =>
        new(AppStrings.Text("ViperSettingAppliedProfileSaveFailed"));
}
