using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed partial class RazerBleDeviceService
{
    private async Task<RazerBleButtonAssignment> GetKeyAssignmentAsync(byte profileId, byte keyId,
        byte mode, CancellationToken token)
    {
        var response = await _session.SendAsync(
            transaction => RazerBleProtocol30.CreateGetKeyAssignment(transaction, profileId, keyId, mode),
            "get key assignment", token).ConfigureAwait(false);
        return RazerBleProtocol30.ReadKeyAssignment(response, keyId, mode);
    }

    private async Task SetKeyAssignmentAsync(byte profileId, byte keyId, byte mode, byte functionId,
        byte[] data, CancellationToken token)
    {
        _ = RazerBleProtocol30.CreateSetKeyAssignment(0, profileId, keyId, mode, functionId, data);
        var previous = await GetKeyAssignmentAsync(profileId, keyId, mode, token).ConfigureAwait(false);
        if (previous.FunctionId == functionId && previous.Data.SequenceEqual(data)) return;
        try
        {
            await WriteKeyAssignmentAsync(functionId, data, token).ConfigureAwait(false);
        }
        catch (Exception failure)
        {
            try
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                await WriteKeyAssignmentAsync(previous.FunctionId, previous.Data.ToArray(), cleanup.Token).ConfigureAwait(false);
            }
            catch (Exception recovery)
            {
                throw new IOException("The BLE key mapping failed and could not be restored.",
                    new AggregateException(failure, recovery));
            }
            throw;
        }

        async Task WriteKeyAssignmentAsync(byte function, byte[] values, CancellationToken cancellationToken)
        {
            var response = await _session.SendAsync(
                transaction => RazerBleProtocol30.CreateSetKeyAssignment(transaction, profileId, keyId, mode, function, values),
                "set key assignment", cancellationToken).ConfigureAwait(false);
            RazerBleProtocol30.RequireKeyMappingReply(response, 0x01);
            var actual = await GetKeyAssignmentAsync(profileId, keyId, mode, cancellationToken).ConfigureAwait(false);
            if (actual.FunctionId != function || !actual.Data.SequenceEqual(values))
                throw new InvalidOperationException("The BLE keyboard kept a different key assignment.");
        }
    }
}
