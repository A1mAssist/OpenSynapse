namespace OpenSynapse.Core.Displays;

public sealed record InternalDisplaySnapshot(
    int Width,
    int Height,
    int RefreshRateHertz,
    IReadOnlyList<int> SupportedRefreshRates)
{
    public bool CanSetRefreshRate => SupportedRefreshRates.Count > 1;
}

public interface IInternalDisplayController
{
    InternalDisplaySnapshot Read();

    InternalDisplaySnapshot SetRefreshRate(int refreshRateHertz);
}
