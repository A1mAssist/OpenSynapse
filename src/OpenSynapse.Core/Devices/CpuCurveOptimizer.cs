namespace OpenSynapse.Core.Devices;

/// <summary>
/// All-core PBO Curve Optimizer offset in steps. Negative values undervolt; the SMU
/// encodes the step count against a 0x100000 base, so -20 is sent as 0x000FFFEC.
/// </summary>
public readonly record struct CpuCurveOptimizer(int Steps)
{
    /// <summary>Deeper undervolts need per-sample validation, so the app stops at 30.</summary>
    public const int MinimumSteps = -30;

    public const int MaximumSteps = 0;

    public CpuCurveOptimizer Clamp() =>
        new(Math.Clamp(Steps, MinimumSteps, MaximumSteps));

    public uint ToSmuArgument() => (uint)(0x100000 + Clamp().Steps);
}
