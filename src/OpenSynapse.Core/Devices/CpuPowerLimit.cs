namespace OpenSynapse.Core.Devices;

/// <summary>
/// Why the CPU package power wall could not be read or written. Each value maps to a
/// different repair action, so the UI can explain the failure instead of hiding the card.
/// </summary>
public enum CpuPowerLimitAvailability
{
    Available,
    PawnIoMissing,
    ModuleMissing,
    AccessDenied,
    Failed,
    AmdDriverMissing,
}

/// <summary>
/// The two RAPL walls and their time windows. Watt values are clamped by <see cref="Clamp"/>
/// before encoding; time windows are carried for the explicit encoder and future UI.
/// </summary>
public readonly record struct CpuPowerLimit(
    double Pl1Watts,
    double Pl2Watts,
    double Pl1TimeSeconds,
    double Pl2TimeSeconds)
{
    public const double MinimumWatts = 8;
    public const double MaximumWatts = 100;
    public const double MaximumRestoreWatts = 250;
    public const double MinimumTimeSeconds = 0.001;
    public const double MaximumTimeSeconds = 0.25;

    private const ulong WattsMask = 0x7FFFUL;
    private const ulong TimeMask = 0xFFUL;
    private const int WattsShift = 32;
    private const int Pl1TimeShift = 16;
    private const int Pl2TimeShift = 48;

    public static double PowerUnitFromRegister(ulong msr) => UnitFromExponent((int)(msr & 0xF));

    public static double EnergyUnitFromRegister(ulong msr) => UnitFromExponent((int)((msr >> 8) & 0x1F));

    public static double TimeUnitFromRegister(ulong msr) => UnitFromExponent((int)((msr >> 16) & 0xF));

    public static CpuPowerLimit Decode(ulong register, double powerUnit, double timeUnit) => new(
        UnitsToWatts(register & WattsMask, powerUnit),
        UnitsToWatts((register >> WattsShift) & WattsMask, powerUnit),
        ((register >> Pl1TimeShift) & TimeMask) * timeUnit,
        ((register >> Pl2TimeShift) & TimeMask) * timeUnit);

    public static bool IsLocked(ulong register) => (register & (1UL << 63)) != 0;

    public static int WattsToUnits(double watts, double powerUnit)
    {
        ValidateUnit(powerUnit, nameof(powerUnit));
        if (!double.IsFinite(watts))
        {
            throw new ArgumentOutOfRangeException(nameof(watts));
        }

        return Math.Clamp(
            (int)Math.Floor(watts / powerUnit + 0.5),
            1,
            (int)WattsMask);
    }

    public static double UnitsToWatts(ulong units, double powerUnit)
    {
        ValidateUnit(powerUnit, nameof(powerUnit));
        return units * powerUnit;
    }

    /// <summary>
    /// Keeps the request inside the offered range and forces PL2 at or above PL1.
    /// The user ceiling is a constant, never the current register value.
    /// </summary>
    public CpuPowerLimit Clamp(double maximumWatts = MaximumWatts)
    {
        if (!double.IsFinite(maximumWatts) || maximumWatts < MinimumWatts)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumWatts));
        }

        if (!double.IsFinite(Pl1Watts) || !double.IsFinite(Pl2Watts))
        {
            throw new ArgumentOutOfRangeException(nameof(Pl1Watts));
        }

        var pl1 = Math.Clamp(Pl1Watts, MinimumWatts, maximumWatts);
        var pl2 = Math.Clamp(Pl2Watts, pl1, Math.Max(pl1, maximumWatts));
        return this with { Pl1Watts = pl1, Pl2Watts = pl2 };
    }

    /// <summary>
    /// Writes only the two watt fields. Enable, clamp, time-window and lock bits stay
    /// exactly as the firmware left them.
    /// </summary>
    public ulong EncodeWattsOnly(ulong original, double powerUnit)
    {
        var pl1 = (ulong)WattsToUnits(Pl1Watts, powerUnit);
        var pl2 = (ulong)WattsToUnits(Pl2Watts, powerUnit);
        var wattMask = WattsMask | (WattsMask << WattsShift);
        return (original & ~wattMask) | pl1 | (pl2 << WattsShift);
    }

    /// <summary>
    /// Writes watts and both PL1/PL2 time windows. Used by tests and migration paths only;
    /// production writes go through <see cref="EncodeWattsOnly"/>.
    /// </summary>
    public ulong Encode(ulong original, double powerUnit, double timeUnit)
    {
        ValidateUnit(timeUnit, nameof(timeUnit));
        var encoded = EncodeWattsOnly(original, powerUnit);
        var pl1Time = (ulong)Math.Clamp(
            (int)Math.Floor(Pl1TimeSeconds / timeUnit + 0.5),
            0,
            (int)TimeMask);
        var pl2Time = (ulong)Math.Clamp(
            (int)Math.Floor(Pl2TimeSeconds / timeUnit + 0.5),
            0,
            (int)TimeMask);
        var timeMask = (TimeMask << Pl1TimeShift) | (TimeMask << Pl2TimeShift);
        return (encoded & ~timeMask) |
            (pl1Time << Pl1TimeShift) |
            (pl2Time << Pl2TimeShift);
    }

    private static double UnitFromExponent(int exponent) => 1.0 / (1 << exponent);

    private static void ValidateUnit(double unit, string parameterName)
    {
        if (!double.IsFinite(unit) || unit <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}

/// <summary>
/// Cross-process snapshot. <see cref="FirmwarePl1Watts"/> is the register value read when
/// the controller opened, not the factory value; restore logic must use the persisted baseline.
/// </summary>
public sealed record CpuPowerLimitReport(
    CpuPowerLimitAvailability Availability,
    string? Detail,
    double Pl1Watts,
    double Pl2Watts,
    double FirmwarePl1Watts,
    double FirmwarePl2Watts,
    double MaximumWatts,
    bool IsLocked,
    bool UsesDirectSmu = false)
{
    public static CpuPowerLimitReport Unavailable(
        CpuPowerLimitAvailability availability,
        string? detail) => new(
            availability,
            detail,
            0,
            0,
            0,
            0,
            CpuPowerLimit.MaximumWatts,
            false);
}
