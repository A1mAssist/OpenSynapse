namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHidDeviceViewModel
{
    internal int? BatteryPercent => _basicState?.BatteryPercent;
    internal bool? IsCharging => _basicState?.IsCharging;

    internal void ApplyBatteryState(int? percent, bool? charging)
    {
        if (_basicState is null) return;
        var nextPercent = percent ?? _basicState.BatteryPercent;
        var nextCharging = charging ?? _basicState.IsCharging;
        if (nextPercent == _basicState.BatteryPercent && nextCharging == _basicState.IsCharging) return;
        _basicState = _basicState with { BatteryPercent = nextPercent, IsCharging = nextCharging };
        OnPropertyChanged(nameof(BatteryText));
        OnPropertyChanged(nameof(ChargingText));
    }
}
