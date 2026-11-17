using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed partial class RazerHidDeviceService
{
    public async Task<byte> GetAnalogJoystickReportModeAsync(
        RazerHidDeviceConnection connection,
        CancellationToken cancellationToken = default)
    {
        RequireReady(connection);
        var request = RazerAnalogJoystickProtocol.CreateGetReportModeRequest(MiddlewareTransactionId);
        var response = await ExecuteAsync(connection,
            new RazerHidRequest("razer_analog_joystick_get_report_mode", request),
            cancellationToken).ConfigureAwait(false);
        return RazerAnalogJoystickProtocol.ParseReportMode(response);
    }

    /// <summary>
    /// Writes only a known 0/1 mode and verifies it with a read-back. The previous mode is
    /// restored when the firmware accepts the frame but keeps another value.
    /// </summary>
    public async Task SetAnalogJoystickReportModeAsync(
        RazerHidDeviceConnection connection,
        byte reportMode,
        CancellationToken cancellationToken = default)
    {
        RequireReady(connection);
        if (reportMode is not (0 or 1))
        {
            throw new ArgumentOutOfRangeException(nameof(reportMode), "Joystick mode must be 0 or 1.");
        }

        var previous = await GetAnalogJoystickReportModeAsync(connection, cancellationToken)
            .ConfigureAwait(false);
        if (previous is not (0 or 1))
        {
            throw new NotSupportedException(
                $"The device already uses a custom analog-joystick mode 0x{previous:X2}.");
        }
        if (previous == reportMode)
        {
            return;
        }

        try
        {
            await WriteAnalogJoystickReportModeAsync(connection, reportMode, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception writeException)
        {
            await RestoreAnalogJoystickModeAsync(connection, previous, writeException)
                .ConfigureAwait(false);
            throw;
        }

        byte readback;
        try
        {
            readback = await GetAnalogJoystickReportModeAsync(connection, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception readException)
        {
            await RestoreAnalogJoystickModeAsync(connection, previous, readException)
                .ConfigureAwait(false);
            throw;
        }

        if (readback == reportMode)
        {
            _diagnosticLog?.TryWrite("analog-joystick",
                $"pid=0x{connection.Definition.ProductId:X4} old=0x{previous:X2} " +
                $"target=0x{reportMode:X2} readback=0x{readback:X2}");
            return;
        }

        await RestoreAnalogJoystickModeAsync(connection, previous, null).ConfigureAwait(false);
        throw new InvalidOperationException(
            $"The analog-joystick readback returned 0x{readback:X2}, expected 0x{reportMode:X2}.");
    }

    public async Task<RazerAnalogJoystickMappingMode> GetAnalogJoystickMappingModeAsync(
        RazerHidDeviceConnection connection,
        byte profileId,
        CancellationToken cancellationToken = default)
    {
        RequireReady(connection);
        var request = RazerAnalogJoystickProtocol.CreateGetMappingModeRequest(
            MiddlewareTransactionId, profileId);
        var response = await ExecuteAsync(connection,
            new RazerHidRequest("razer_analog_joystick_get_mapping_mode", request),
            cancellationToken).ConfigureAwait(false);
        return RazerAnalogJoystickProtocol.ParseMappingMode(response);
    }

    internal async Task ProbeAnalogJoystickAsync(
        RazerHidDeviceDefinition definition,
        string devicePath,
        ISet<RazerHidBackendCapability> result,
        CancellationToken cancellationToken)
    {
        if (definition.Category is not DeviceCategory.Keyboard ||
            definition.ProductId is not (0x02A6 or 0x02A7 or 0x02B0 or 0x02CF))
        {
            return;
        }

        byte? reportMode = null;
        var request = RazerAnalogJoystickProtocol.CreateGetReportModeRequest(MiddlewareTransactionId);
        if (await ProbeMiddlewareAsync(definition.ProductId, devicePath, definition.DeviceWait,
                "keyboard.analogJoystickMode", request, response =>
                {
                    reportMode = RazerAnalogJoystickProtocol.ParseReportMode(response);
                    return true;
                }, cancellationToken).ConfigureAwait(false))
        {
            result.Add(RazerHidBackendCapability.AnalogJoystickRead);
            if (reportMode is 0 or 1)
            {
                result.Add(RazerHidBackendCapability.AnalogJoystickWrite);
            }
        }
    }

    private Task WriteAnalogJoystickReportModeAsync(
        RazerHidDeviceConnection connection,
        byte reportMode,
        CancellationToken cancellationToken) =>
        ExecuteWithoutResultAsync(connection,
            new RazerHidRequest("razer_analog_joystick_set_report_mode",
                RazerAnalogJoystickProtocol.CreateSetReportModeRequest(
                    MiddlewareTransactionId, reportMode)),
            cancellationToken);

    private async Task RestoreAnalogJoystickModeAsync(
        RazerHidDeviceConnection connection,
        byte previous,
        Exception? cause)
    {
        try
        {
            await WriteAnalogJoystickReportModeAsync(connection, previous, CancellationToken.None)
                .ConfigureAwait(false);
            var restored = await GetAnalogJoystickReportModeAsync(
                connection, CancellationToken.None).ConfigureAwait(false);
            if (restored != previous)
            {
                throw new InvalidOperationException(
                    $"The analog-joystick restore readback returned 0x{restored:X2}.");
            }
        }
        catch (Exception restoreException)
        {
            _diagnosticLog?.TryWrite("analog-joystick",
                $"pid=0x{connection.Definition.ProductId:X4} restore=0x{previous:X2} failed: " +
                $"{restoreException.GetType().Name}: {restoreException.Message}");
            throw new InvalidOperationException(
                "The analog-joystick update failed and the previous mode could not be restored.",
                cause is null ? restoreException : new AggregateException(cause, restoreException));
        }
    }
}
