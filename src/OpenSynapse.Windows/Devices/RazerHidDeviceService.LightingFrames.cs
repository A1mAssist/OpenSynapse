using System.ComponentModel;
using System.Runtime.InteropServices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed partial class RazerHidDeviceService
{
    public async Task SetCustomFrameAsync(
        RazerHidDeviceConnection connection,
        IReadOnlyList<RazerHidColor> frame,
        CancellationToken cancellationToken = default,
        bool prepareEffect = true)
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
        // Streaming owners prepare extended effects once; legacy matrices latch after every frame.
        var latchAfterRows = RazerHidLightingProtocol.RequiresCustomFrameLatch(connection.Definition);
        var selector = prepareEffect || latchAfterRows
            ? RazerHidLightingProtocol.TryCreateCustomEffectRequest(connection)?.Report : null;
        var offset = selector is not null && !latchAfterRows ? 1 : 0;
        var reports = new byte[dimensions.Rows + (selector is null ? 0 : 1)][];
        if (selector is not null) reports[latchAfterRows ? dimensions.Rows : 0] = selector;
        for (var row = 0; row < dimensions.Rows; row++)
        {
            reports[row + offset] = RazerHidLightingProtocol.SetCustomRow(
                connection.Definition,
                checked((byte)row),
                0,
                frame.Skip(row * dimensions.Columns).Take(dimensions.Columns).ToArray()).Report;
        }

        try
        {
            await using var session = await _transport
                .OpenSessionForProductAsync(connection.DevicePath!, connection.Definition.ProductId, cancellationToken)
                .ConfigureAwait(false);
            // A matrix frame is six row reports. Over a wireless dongle, reading every
            // acknowledgement is the difference between the 25 fps cadence and ~10 fps.
            // The final query also drains any replies the device kept.
            await session.SendBatchFastAsync(reports, connection.Definition.DeviceWait, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (NotSupportedException)
        {
            if (selector is not null && !latchAfterRows)
            {
                await SetCustomFrameEffectAsync(connection, cancellationToken).ConfigureAwait(false);
            }
            for (var row = 0; row < dimensions.Rows; row++)
            {
                await SetCustomRowAsync(
                    connection,
                    checked((byte)row),
                    0,
                    frame.Skip(row * dimensions.Columns).Take(dimensions.Columns).ToArray(),
                    cancellationToken).ConfigureAwait(false);
            }
            if (selector is not null && latchAfterRows)
                await SetCustomFrameEffectAsync(connection, cancellationToken).ConfigureAwait(false);
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
