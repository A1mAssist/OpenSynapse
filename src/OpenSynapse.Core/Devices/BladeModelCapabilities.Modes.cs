namespace OpenSynapse.Core.Devices;

public static partial class BladeModelCapabilities
{
    public const ushort Blade14_2022ProductId = 0x028C;
    public const ushort Blade16_2026ProductId = 0x02E0;
    public const ushort Blade18_2026ProductId = 0x02E1;
    public const ushort RazerBook13ProductId = 0x026A;

    /// <summary>Custom CPU levels every audited model captures (0x0D:0x07 payload level).</summary>
    private static readonly BladeCpuBoostMode[] LowMediumHigh =
        [BladeCpuBoostMode.Low, BladeCpuBoostMode.Medium, BladeCpuBoostMode.High];

    /// <summary>No audited admission. Used when a product id is unknown to this table.</summary>
    private static readonly BladeCpuBoostMode[] None = [];

    private static readonly BladePerformanceMode[] StandardPerformanceModes =
    [
        BladePerformanceMode.Balanced,
        BladePerformanceMode.Performance,
        BladePerformanceMode.Custom,
        BladePerformanceMode.Silent,
    ];

    // Synapse on 028C exposes the first three. The owner confirmed the firmware also
    // accepts 0x02 and reads it back, so keep it as an extra mode after the official list.
    private static readonly BladePerformanceMode[] Blade14_2022PerformanceModes =
    [
        BladePerformanceMode.Balanced,
        BladePerformanceMode.Silent,
        BladePerformanceMode.Custom,
        BladePerformanceMode.Performance,
    ];

    // HyperBoost only exists on the models whose middleware lists it. It rides the same
    // thermal channel as the presets, so the write itself needs no extra capability.
    private static readonly BladePerformanceMode[] HyperBoostPerformanceModes =
    [
        .. StandardPerformanceModes,
        BladePerformanceMode.Hyperboost,
    ];

    // Exact mode lists the middleware publishes, in the order its UI shows them.
    private static readonly BladePerformanceMode[] Modes16Hyper =
    [
        BladePerformanceMode.Balanced,
        BladePerformanceMode.Silent,
        BladePerformanceMode.Custom,
        BladePerformanceMode.Hyperboost,
    ];

    private static readonly BladePerformanceMode[] Modes02C6 =
    [
        BladePerformanceMode.Balanced,
        BladePerformanceMode.Silent,
        BladePerformanceMode.Performance,
        BladePerformanceMode.Custom,
        BladePerformanceMode.Hyperboost,
        BladePerformanceMode.BatterySaver,
    ];

    private static readonly BladePerformanceMode[] Modes02C5 =
    [
        BladePerformanceMode.Balanced,
        BladePerformanceMode.Silent,
        BladePerformanceMode.Performance,
        BladePerformanceMode.Custom,
        BladePerformanceMode.BatterySaver,
    ];

    private static readonly BladePerformanceMode[] Modes02B7 =
    [
        BladePerformanceMode.Balanced,
        BladePerformanceMode.Silent,
        BladePerformanceMode.Gaming,
        BladePerformanceMode.Custom,
        BladePerformanceMode.Hyperboost,
    ];

    private static readonly BladePerformanceMode[] Modes029D =
    [
        BladePerformanceMode.Balanced,
        BladePerformanceMode.Silent,
        BladePerformanceMode.Custom,
        BladePerformanceMode.Hyperboost,
    ];

    // 02B6 publishes Gaming as wire value 0x01.
    private static readonly BladePerformanceMode[] Modes02B6 =
    [
        BladePerformanceMode.Balanced,
        BladePerformanceMode.Silent,
        BladePerformanceMode.Gaming,
        BladePerformanceMode.Custom,
    ];

    private static readonly BladePerformanceMode[] Modes0245 =
    [
        BladePerformanceMode.Balanced,
        BladePerformanceMode.Custom,
    ];

    // 02B8 publishes Balanced, Silent, Gaming and Custom. 02A0 publishes only
    // Balanced, Silent and Custom, so it keeps the three-mode list below.
    private static readonly BladePerformanceMode[] Modes02B8 =
    [
        BladePerformanceMode.Balanced,
        BladePerformanceMode.Silent,
        BladePerformanceMode.Gaming,
        BladePerformanceMode.Custom,
    ];

    // 02C7 and 02E1 publish Gaming in addition to the 02C5 list.
    private static readonly BladePerformanceMode[] Modes02C7 =
    [
        BladePerformanceMode.Balanced,
        BladePerformanceMode.Silent,
        BladePerformanceMode.Performance,
        BladePerformanceMode.Gaming,
        BladePerformanceMode.Custom,
        BladePerformanceMode.BatterySaver,
    ];

    // 02A0/028A/028B/029E share the older three-mode list; only 02B8 adds Gaming.
    private static readonly BladePerformanceMode[] Modes028A =
    [
        BladePerformanceMode.Balanced,
        BladePerformanceMode.Silent,
        BladePerformanceMode.Custom,
    ];

    // 0255 lists Balanced, Productivity_Creator and Gaming. Productivity_Creator is the
    // same wire value as Performance (0x02); Gaming is 0x01.
    private static readonly BladePerformanceMode[] Modes0255 =
    [
        BladePerformanceMode.Balanced,
        BladePerformanceMode.Performance,
        BladePerformanceMode.Gaming,
    ];

    // 026A publishes Balanced, Battery Saver and Performance, each with the same
    // 2700..5500 RPM fan range and a 4000 RPM default. It publishes no Custom mode.
    private static readonly BladePerformanceMode[] Modes026A =
    [
        BladePerformanceMode.Balanced,
        BladePerformanceMode.BatterySaver,
        BladePerformanceMode.Performance,
    ];

    private static readonly BladePerformanceMode[] ModesBalancedPerformance =
    [
        BladePerformanceMode.Balanced,
        BladePerformanceMode.Performance,
    ];

    private static readonly BladePerformanceMode[] ModesBalancedGaming =
    [
        BladePerformanceMode.Balanced,
        BladePerformanceMode.Gaming,
    ];

    private static readonly BladePerformanceMode[] ModesBalancedPerformanceGaming =
    [
        BladePerformanceMode.Balanced,
        BladePerformanceMode.Performance,
        BladePerformanceMode.Gaming,
    ];

    private static readonly BladePerformanceMode[] ModesBalancedBatterySaverGaming =
    [
        BladePerformanceMode.Balanced,
        BladePerformanceMode.BatterySaver,
        BladePerformanceMode.Gaming,
    ];

    private static readonly BladePerformanceMode[] ModesBalancedPerformanceBatterySaver =
    [
        BladePerformanceMode.Balanced,
        BladePerformanceMode.Performance,
        BladePerformanceMode.BatterySaver,
    ];

    private static readonly BladePerformanceMode[] ModesBalancedCustom =
    [
        BladePerformanceMode.Balanced,
        BladePerformanceMode.Custom,
    ];

    private static readonly BladePerformanceMode[] ModesBalancedOnly =
    [
        BladePerformanceMode.Balanced,
    ];

    private static readonly BladePerformanceMode[] ModesBalancedBatterySaver =
    [
        BladePerformanceMode.Balanced,
        BladePerformanceMode.BatterySaver,
    ];

    private static IReadOnlyDictionary<BladePerformanceMode, BladeFanTargetRange> Ranges(
        params (BladePerformanceMode Mode, int Minimum, int Maximum, int Default)[] items)
    {
        var map = new Dictionary<BladePerformanceMode, BladeFanTargetRange>(items.Length);
        foreach (var item in items)
        {
            map[item.Mode] = new BladeFanTargetRange(item.Minimum, item.Maximum, item.Default);
        }

        return map;
    }
}
