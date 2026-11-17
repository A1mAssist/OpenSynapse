using OpenSynapse.Windows.Devices;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHidWorkspace
{
    private async Task RestoreAnalogJoystickModesAsync(
        IReadOnlyList<RazerHidDeviceConnection> connections,
        CancellationToken cancellationToken)
    {
        if (_deviceService is null)
        {
            return;
        }

        foreach (var entry in AnalogJoystickRecoveryStore.Shared.ReadAll())
        {
            var connection = connections.FirstOrDefault(candidate =>
                StringComparer.OrdinalIgnoreCase.Equals(candidate.InstanceId, entry.InstanceId));
            if (connection is null ||
                !connection.Capabilities.Contains(RazerHidBackendCapability.AnalogJoystickWrite))
            {
                continue;
            }

            try
            {
                await _deviceService.SetAnalogJoystickReportModeAsync(
                    connection, entry.PreviousMode, cancellationToken).ConfigureAwait(false);
                AnalogJoystickRecoveryStore.Shared.Clear(entry.InstanceId);
                _diagnosticLog.TryWrite("analog-joystick",
                    $"restored pid=0x{connection.Definition.ProductId:X4} " +
                    $"instance={entry.InstanceId} mode=0x{entry.PreviousMode:X2}");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _diagnosticLog.TryWrite("analog-joystick",
                    $"restore failed pid=0x{connection.Definition.ProductId:X4} " +
                    $"instance={entry.InstanceId}: {exception.GetType().Name}: {exception.Message}");
            }
        }
    }
}
