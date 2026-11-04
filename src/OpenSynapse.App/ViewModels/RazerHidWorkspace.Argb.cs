using OpenSynapse.Windows.Devices;

namespace OpenSynapse.App.ViewModels;

/// <summary>ARGB controller state shared with the lighting studio.</summary>
public sealed partial class RazerHidWorkspace
{
    private RazerHidSpecialLightingConnection? _argbConnection;

    internal RazerHidSpecialLightingService? SpecialLightingService => _specialLightingService;

    internal RazerHidSpecialLightingConnection? ArgbConnection => _argbConnection;

    private void CacheArgb(IReadOnlyList<RazerHidSpecialLightingConnection> connections) =>
        _argbConnection = connections.FirstOrDefault(connection =>
            connection.Kind == RazerHidSpecialLightingKind.Argb320 &&
            connection.IsReady && connection.Error is null);

    /// <summary>Re-reads the ARGB controller so a Studio hot-plug sees the current port.</summary>
    internal async Task RefreshArgbDiscoveredAsync(CancellationToken cancellationToken)
    {
        if (_specialLightingService is null) return;
        var connections = await _specialLightingService.DiscoverAsync(cancellationToken).ConfigureAwait(false);
        CacheArgb(connections);
    }
}
