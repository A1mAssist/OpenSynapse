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

public interface IInternalDisplayController
{
    InternalDisplaySnapshot Read();

    InternalDisplaySnapshot SetRefreshRate(int refreshRateHertz);

    Task<double> SetBrightnessAsync(double level, CancellationToken cancellationToken = default);
}
