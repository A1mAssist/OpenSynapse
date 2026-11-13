namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerKeyboardViewModel
{
    internal void SetBatteryPercent(int percent)
    {
        BatteryText = $"{percent}%";
        BatteryPercent = percent;
        _hasBattery = true;
        OnPropertyChanged(nameof(BatteryPercent));
        OnPropertyChanged(nameof(BatteryVisibility));
    }
}
