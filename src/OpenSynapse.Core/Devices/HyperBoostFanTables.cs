namespace OpenSynapse.Core.Devices;

public readonly record struct HyperBoostFanStep(
    int FanSpeedRpm,
    int CpuTripCelsius,
    int GpuTripCelsius,
    int CpuHysteresisCelsius,
    int GpuHysteresisCelsius);

/// <summary>
/// The cooling-pad fan table each Blade bundle injects for HyperBoost. The EC owns the
/// laptop fans; this table only drives the optional Razer pad (1532:0F43).
/// </summary>
public static class HyperBoostFanTables
{
    private static readonly HyperBoostFanStep[] Table029D =
    [
        new(1000, 60, 55, 0, 0),
        new(1200, 64, 58, 53, 50),
        new(1400, 68, 61, 57, 53),
        new(1600, 72, 64, 61, 56),
        new(1850, 76, 67, 65, 59),
        new(2100, 80, 70, 69, 62),
        new(2400, 84, 73, 73, 65),
        new(2700, 1000, 1000, 77, 68),
    ];

    private static readonly HyperBoostFanStep[] Table029F =
    [
        new(1000, 61, 56, 0, 0),
        new(1200, 65, 60, 53, 51),
        new(1400, 69, 64, 57, 55),
        new(1600, 73, 68, 61, 59),
        new(1850, 77, 72, 65, 63),
        new(2100, 81, 76, 69, 67),
        new(2400, 85, 80, 73, 71),
        new(2700, 1000, 1000, 77, 75),
    ];

    private static readonly HyperBoostFanStep[] Table02B7 =
    [
        new(1000, 57, 49, 0, 0),
        new(1200, 60, 52, 52, 47),
        new(1400, 65, 57, 55, 51),
        new(1600, 70, 61, 60, 55),
        new(1850, 75, 65, 65, 59),
        new(2100, 80, 69, 70, 63),
        new(2400, 85, 73, 75, 67),
        new(2700, 1000, 1000, 80, 71),
    ];

    // 02C6 (Blade 16 2025) and 02E0 (Blade 16 2026) inject the same table.
    private static readonly HyperBoostFanStep[] Table02C6 =
    [
        new(1000, 61, 53, 0, 0),
        new(1200, 65, 56, 53, 49),
        new(1400, 69, 59, 57, 52),
        new(1600, 73, 62, 61, 55),
        new(1850, 77, 65, 65, 58),
        new(2100, 81, 68, 69, 61),
        new(2400, 85, 71, 73, 64),
        new(2700, 1000, 1000, 77, 67),
    ];

    public static IReadOnlyList<HyperBoostFanStep> For(ushort productId) => productId switch
    {
        0x029D => Table029D,
        0x029F => Table029F,
        0x02B7 => Table02B7,
        0x02C6 or 0x02E0 => Table02C6,
        _ => [],
    };

    /// <summary>
    /// The bundle steps up while either temperature exceeds the current trip and steps
    /// down only when both are below the current hysteresis. Missing temperatures keep
    /// the last target instead of stopping the fan.
    /// </summary>
    public static int SelectRpm(
        IReadOnlyList<HyperBoostFanStep> table,
        double? cpuCelsius,
        double? gpuCelsius,
        int? lastRpm)
    {
        if (table.Count == 0)
        {
            return lastRpm ?? 0;
        }
        if (cpuCelsius is not double cpu || gpuCelsius is not double gpu)
        {
            return lastRpm ?? table[0].FanSpeedRpm;
        }

        // Hysteresis compares against the step the fan is already on, not the step the
        // current temperature would map to, or the fan would oscillate on every degree.
        var index = 0;
        if (lastRpm is int last)
        {
            for (var i = 0; i < table.Count; i++)
            {
                if (table[i].FanSpeedRpm == last)
                {
                    index = i;
                    break;
                }
            }
        }
        while (index + 1 < table.Count &&
            (cpu > table[index].CpuTripCelsius || gpu > table[index].GpuTripCelsius))
        {
            index++;
        }
        while (index > 0 &&
            cpu < table[index].CpuHysteresisCelsius &&
            gpu < table[index].GpuHysteresisCelsius)
        {
            index--;
        }

        return table[index].FanSpeedRpm;
    }
}
