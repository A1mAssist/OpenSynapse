namespace OpenSynapse.Core.Sensors;

public interface IPowerSourceProvider
{
    bool? IsPluggedIn { get; }

    /// <summary>Remaining battery charge percentage, or null when no battery is present.</summary>
    int? BatteryPercent { get; }

    /// <summary>True while charging, false while not charging, null when unknown.</summary>
    bool? IsCharging { get; }
}

