using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed partial class RazerHidDeviceService
{
    public Task SetWristRestColorAsync(
        RazerHidDeviceConnection connection,
        RazerHidColor color,
        CancellationToken cancellationToken = default) =>
        ExecuteWithoutResultAsync(connection,
            RazerHidWristRestProtocol.CreateSetColorRequest(connection.Definition, color), cancellationToken);
}
