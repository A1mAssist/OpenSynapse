using OpenSynapse.Windows.Devices;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHidDeviceViewModel
{
    public Task LoadSoundbarAsync(CancellationToken cancellationToken = default) =>
        Soundbar?.LoadAsync(cancellationToken) ?? Task.CompletedTask;

    internal static IReadOnlyList<DeviceProtocolDetailViewModel> BuildProtocolDetails(
        RazerHidDeviceConnection connection,
        IReadOnlySet<RazerHidBackendCapability>? unsupported = null,
        IReadOnlyDictionary<string, string>? errors = null)
    {
        if (connection.EndpointState != RazerHidEndpointState.Resolved)
        {
            return [];
        }

        // The diagnostics page only needs the checks that need attention. Listing every
        // declared capability made one mouse look like a wall of protocol rows.
        return connection.Capabilities
            .OrderBy(capability => capability.ToString(), StringComparer.Ordinal)
            .Select(capability =>
            {
                var unsupportedCapability = unsupported?.Contains(capability) ?? false;
                string? reason = null;
                var failed = errors is not null &&
                    errors.TryGetValue(capability.ToString(), out reason);
                var available = !unsupportedCapability && !failed;
                var detail = failed
                    ? reason ?? AppStrings.Text("DiagnosticProtocolUnavailable")
                    : AppStrings.Text("DiagnosticProtocolUnavailable");

                return new DeviceProtocolDetailViewModel(
                    $"RazerHidCapability{capability}",
                    available,
                    detail,
                    failed: failed);
            })
            .Where(detail => !detail.IsAvailable)
            .ToArray();
    }
}
