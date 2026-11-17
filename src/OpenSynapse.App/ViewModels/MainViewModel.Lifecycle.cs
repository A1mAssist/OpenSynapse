namespace OpenSynapse.App.ViewModels;

public sealed partial class MainViewModel
{
    private void RefreshLocalizationCore()
    {
        Profiles.ProfileStatusText = AppStrings.FormatText("ProfileLoaded",
            Profiles.ActiveProfileName);

        if (_lastDeviceTelemetry is { } telemetry)
        {
            ApplyDeviceTelemetry(telemetry);
        }
        Blade.RefreshLocalization();

        foreach (var row in Devices)
        {
            row.RefreshLocalization();
        }
        RazerHid.RefreshLocalization();
        foreach (var row in BlackSharkDevices)
        {
            row.RefreshLocalization();
        }
        SelectedBlackShark?.RefreshLocalization();
        Accessories.SelectedGamepad?.RefreshLocalization();
        CoolingPad.RefreshLocalization();
        Kiyo.RefreshLocalization();
        foreach (var row in Diagnostics.Rows)
        {
            row.RefreshLocalization();
        }
        Viper.RefreshLocalization();

        Telemetry.RefreshLocalization();

        OnPropertyChanged(string.Empty);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await Profiles.LoadAsync(cancellationToken);
        await Telemetry.RefreshBatteryHealthAsync(cancellationToken);
        await Refresh.RefreshDevicesAsync(cancellationToken);
        Firmware.RefreshVersions();
        await Blade.InitializeAsync(cancellationToken);
        if (Telemetry.IsSamplingEnabled)
        {
            await Telemetry.RefreshPerformanceAsync(cancellationToken);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _deviceImages?.Dispose();
        // Dispose runs on the UI thread and still touches bound collections and
        // selections below; keep every continuation there.
        await RazerHid.DisposeAsync().ConfigureAwait(true);
        Macros.Dispose();
        _hue?.Dispose();
        CoolingPad.Dispose();
        Kiyo.Dispose();

        SelectedBlackShark = null;

        if (SelectedBle is { } ble)
        {
            await ble.DisposeAsync().ConfigureAwait(true);
            SelectedBle = null;
        }

        if (SelectedAwBleHeadset is { } awBleHeadset)
        {
            await awBleHeadset.DisposeAsync().ConfigureAwait(true);
            SelectedAwBleHeadset = null;
        }
        _selectedBlackSharkDevice = null;
        BlackSharkDevices.Clear();

        await Accessories.DisposeAsync().ConfigureAwait(true);
        DisposeDeviceHome();

        await Blade.DisposeAsync().ConfigureAwait(true);
        _ = await Fan.StopAsync("application-exit").ConfigureAwait(true);
        await Fan.DisposeAsync().ConfigureAwait(true);
    }
}
