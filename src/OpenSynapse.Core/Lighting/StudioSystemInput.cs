namespace OpenSynapse.Core.Lighting;

/// <summary>
/// Time and optional machine state for effects that read the system. Null load
/// fields mean "not sampled"; a provider fills them when the effect needs it.
/// </summary>
public readonly record struct StudioSystemInput(
    DateTimeOffset Now,
    double? CpuLoad = null,
    double? GpuLoad = null,
    double? BatteryPercent = null,
    string? ForegroundProcess = null)
{
    /// <summary>Physical memory in use, 0..1; null when unavailable.</summary>
    public double? MemoryLoad { get; init; }
}
