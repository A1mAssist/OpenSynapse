namespace OpenSynapse.Core.Devices;

public enum BatteryLevel
{
    Unknown,
    Good,
    Low,
    Critical,
}

/// <summary>Shared math for the device card's battery glyph.</summary>
public static class BatteryDisplay
{
    public const double MaxFillWidth = 14;

    public static double FillWidth(int? percent) =>
        percent is { } value ? Math.Clamp(value, 0, 100) * MaxFillWidth / 100d : 0d;

    public static BatteryLevel Level(int? percent) => percent switch
    {
        null => BatteryLevel.Unknown,
        <= 10 => BatteryLevel.Critical,
        <= 20 => BatteryLevel.Low,
        _ => BatteryLevel.Good,
    };
}
