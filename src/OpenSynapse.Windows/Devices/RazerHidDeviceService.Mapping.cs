using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed partial class RazerHidDeviceService
{
    public async Task<RazerHidButtonAssignment> GetButtonAssignmentAsync(
        RazerHidDeviceConnection connection,
        byte buttonId,
        byte mode,
        CancellationToken cancellationToken = default)
    {
        var request = UsesKeyAssignments(connection)
            ? RazerHidMappingProtocol.GetKeyAssignment(buttonId, mode)
            : RazerHidMappingProtocol.GetAssignment(buttonId, mode);
        return RazerHidMappingProtocol.ParseAssignment(
            await ExecuteAsync(connection, request, cancellationToken).ConfigureAwait(false));
    }

    // Keyboards act on the key table; the button table accepts writes but never
    // drives a key press. Synapse's OBM engine makes the same split.
    private static bool UsesKeyAssignments(RazerHidDeviceConnection connection) =>
        connection.Definition.Category == DeviceCategory.Keyboard;

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

        var request = UsesKeyAssignments(connection)
            ? RazerHidMappingProtocol.SetKeyAssignment(buttonId, mode, functionId, data.ToArray())
            : RazerHidMappingProtocol.SetAssignment(buttonId, mode, functionId, data.ToArray());
        await ExecuteWithoutResultAsync(connection, request, cancellationToken).ConfigureAwait(false);
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