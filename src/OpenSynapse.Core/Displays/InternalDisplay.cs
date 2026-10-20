namespace OpenSynapse.Core.Displays;

public sealed record InternalDisplaySnapshot(
    int Width,
    int Height,
    int RefreshRateHertz,
    IReadOnlyList<int> SupportedRefreshRates,
    double? BrightnessLevel)
{
    public bool CanSetRefreshRate => SupportedRefreshRates.Count > 1;
    public bool CanSetBrightness => BrightnessLevel.HasValue;
}

/// <summary>
/// The machine has no active internal panel, which is normal when only an external
/// display is attached. Callers treat this as unavailable rather than as a failure.
/// </summary>
public sealed class NoActiveInternalDisplayException()
    : InvalidOperationException("Windows did not report one active internal display path.");

public interface IInternalDisplayController
{
    InternalDisplaySnapshot Read();

    InternalDisplaySnapshot SetRefreshRate(int refreshRateHertz);

    Task<double> SetBrightnessAsync(double level, CancellationToken cancellationToken = default);
}
