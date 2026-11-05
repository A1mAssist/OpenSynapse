using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed partial class RazerHidDeviceService
{
    public async Task<RazerHidButtonAssignment> GetButtonAssignmentAsync(
        RazerHidDeviceConnection connection,
        byte buttonId,
        byte mode,
        CancellationToken cancellationToken = default) =>
        RazerHidMappingProtocol.ParseAssignment(await ExecuteAsync(connection,
            RazerHidMappingProtocol.GetAssignment(buttonId, mode), cancellationToken).ConfigureAwait(false));

    public async Task SetButtonAssignmentAsync(
        RazerHidDeviceConnection connection,
        byte buttonId,
        byte mode,
        byte functionId,
        IReadOnlyList<byte> data,
        CancellationToken cancellationToken = default)
    {
        // Synapse flips a wireless mouse back to driver mode before touching onboard
        // mappings; in normal mode the firmware accepts the frame but keeps the old
        // assignment, which looked like "this device refuses edits".
        if (connection.Capabilities.Contains(RazerHidBackendCapability.DeviceModeWrite))
        {
            await EnsureDriverModeAsync(connection, cancellationToken).ConfigureAwait(false);
        }

        await ExecuteWithoutResultAsync(connection,
            RazerHidMappingProtocol.SetAssignment(buttonId, mode, functionId, data.ToArray()),
            cancellationToken).ConfigureAwait(false);
        var applied = await GetButtonAssignmentAsync(connection, buttonId, mode, cancellationToken)
            .ConfigureAwait(false);
        if (applied.FunctionId != functionId || !applied.Data.SequenceEqual(data))
        {
            throw new InvalidOperationException(
                "The device kept a different button assignment. Reconnect it over its 2.4 GHz " +
                "receiver or USB cable, then retry; Bluetooth links cannot write onboard mappings.");
        }
    }
}