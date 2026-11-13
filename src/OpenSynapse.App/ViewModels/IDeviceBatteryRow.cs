namespace OpenSynapse.App.ViewModels;

/// <summary>
/// Live battery state for one device-list row. The card binds through this interface so a
/// poll result repaints the row without rebuilding the list.
/// </summary>
public interface IDeviceBatteryRow
{
    bool IsBatteryAvailable { get; }
    string BatteryText { get; }
    int? BatteryPercent { get; }
}
