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
        if (UsesAnalogKeyAssignments(connection))
        {
            return RazerHidMappingProtocol.ParseAnalogAssignment(await ExecuteAsync(connection,
                RazerHidMappingProtocol.GetAnalogKeyAssignment(buttonId, mode), cancellationToken)
                .ConfigureAwait(false));
        }

        var request = UsesKeyAssignments(connection)
            ? RazerHidMappingProtocol.GetKeyAssignment(buttonId, mode)
            : RazerHidMappingProtocol.GetAssignment(buttonId, mode);
        return RazerHidMappingProtocol.ParseAssignment(
            await ExecuteAsync(connection, request, cancellationToken).ConfigureAwait(false));
    }

    // Keyboards act on the key table; the button table accepts writes but never
    // drives a key press. Analog keyboards keep a separate table with the actuation band.
    // Synapse's OBM engine makes the same split.
    private static bool UsesKeyAssignments(RazerHidDeviceConnection connection) =>
        connection.Definition.Category == DeviceCategory.Keyboard;

    // 0266 Huntsman V2 Analog and 0282 Huntsman Mini Analog declare the analog OBM engine.
    private static bool UsesAnalogKeyAssignments(RazerHidDeviceConnection connection) =>
        connection.Definition.ProductId is 0x0266 or 0x0282;

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

        RazerHidRequest request;
        if (UsesAnalogKeyAssignments(connection))
        {
            // Preserve the key's actuation band; changing it would alter how the key feels.
            var current = await GetButtonAssignmentAsync(connection, buttonId, mode, cancellationToken)
                .ConfigureAwait(false);
            request = RazerHidMappingProtocol.SetAnalogKeyAssignment(buttonId, mode,
                current.ThresholdL, current.ThresholdH, functionId, data.ToArray());
        }
        else
        {
            request = UsesKeyAssignments(connection)
                ? RazerHidMappingProtocol.SetKeyAssignment(buttonId, mode, functionId, data.ToArray())
                : RazerHidMappingProtocol.SetAssignment(buttonId, mode, functionId, data.ToArray());
        }
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

    private async Task<bool> ProbeKeyAssignmentAsync(
        RazerHidDeviceDefinition definition,
        string devicePath,
        CancellationToken cancellationToken)
    {
        var analog = definition.ProductId is 0x0266 or 0x0282;
        // 0244/022B keypads map the first art key to SPAD_NUM_1, firmware key id 64 (0x40).
        // 0x02 is the generic keyboard "1" output key, not a keypad table entry.
        var keypad = definition.ProductId is 0x022B or 0x0244;
        var keyId = (byte)(keypad ? 0x40 : 0x02);
        var assignment = analog
            ? RazerHidMappingProtocol.GetAnalogKeyAssignment(
                keyId, RazerHidMappingProtocol.NormalMode)
            : RazerHidMappingProtocol.GetKeyAssignment(
                keyId, RazerHidMappingProtocol.NormalMode);
        return await ProbeMiddlewareAsync(definition.ProductId, devicePath, definition.DeviceWait,
            "mapping.keyAssignment", assignment.Report,
            response =>
            {
                if (analog)
                {
                    _ = RazerHidMappingProtocol.ParseAnalogAssignment(response);
                }
                else
                {
                    _ = RazerHidMappingProtocol.ParseAssignment(response);
                }
                return true;
            }, cancellationToken).ConfigureAwait(false);
    }
}
