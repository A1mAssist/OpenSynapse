using OpenSynapse.Core.Displays;

namespace OpenSynapse.App.ViewModels;

public sealed partial class DisplayWorkspace
{
    private readonly EcoDisplayPolicy _ecoDisplayPolicy = new();

    internal bool SetEcoDisplayActive(bool active) => _ecoDisplayPolicy.SetActive(active);

    private int? ResolveDisplayRate(InternalDisplaySnapshot snapshot, int? configuredHertz) =>
        _ecoDisplayPolicy.Resolve(snapshot.SupportedRefreshRates, snapshot.RefreshRateHertz, configuredHertz);
}
