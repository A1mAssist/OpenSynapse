namespace OpenSynapse.Core.Sensors;

/// <summary>
/// A taskbar battery selection kept by key so it survives the device disappearing.
/// </summary>
public sealed class TaskbarBatteryPreference
{
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}
