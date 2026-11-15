using System.ComponentModel;
using System.Runtime.InteropServices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed partial class RazerHidDeviceService
{
    public async Task SetCustomFrameAsync(
        RazerHidDeviceConnection connection,
        IReadOnlyList<RazerHidColor> frame,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var dimensions = connection.Definition.MatrixDimensions ??
            throw new NotSupportedException("The RazerHid device does not expose matrix dimensions.");
        if (!connection.Capabilities.Contains(RazerHidBackendCapability.MatrixFrameWrite) ||
            frame.Count != dimensions.Rows * dimensions.Columns)
        {
            throw new NotSupportedException(
                $"RazerHid device 1532:{connection.Definition.ProductId:X4} does not support this matrix frame.");
        }

        RequireReady(connection);
        var reports = new byte[dimensions.Rows][];
        for (var row = 0; row < dimensions.Rows; row++)
        {
            reports[row] = RazerHidLightingProtocol.SetCustomRow(
                connection.Definition,
                checked((byte)row),
                0,
                frame.Skip(row * dimensions.Columns).Take(dimensions.Columns).ToArray()).Report;
        }

        try
        {
            await using var session = await _transport
                .OpenSessionAsync(connection.DevicePath!, cancellationToken).ConfigureAwait(false);
            await session.SendBatchAsync(reports, connection.Definition.DeviceWait, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (NotSupportedException)
        {
            for (var row = 0; row < dimensions.Rows; row++)
            {
                await SetCustomRowAsync(
                    connection,
                    checked((byte)row),
                    0,
                    frame.Skip(row * dimensions.Columns).Take(dimensions.Columns).ToArray(),
                    cancellationToken).ConfigureAwait(false);
            }
        }
        catch (RazerDeviceTimeoutException exception) when (!exception.IsRetryableBusy)
        {
            if (await IsWirelessLinkDownAsync(connection, cancellationToken).ConfigureAwait(false))
            {
                throw new RazerDeviceTimeoutException(WirelessLinkDownMessage, wirelessLinkDown: true);
            }

            throw;
        }
        catch (Exception exception) when (
            exception is Win32Exception or IOException or COMException)
        {
            _endpointCache.TryRemove(connection.InstanceId, out _);
            throw;
        }
    }
}
