using OpenSynapse.Core.Profiles;
using OpenSynapse.Core.Sensors;
using OpenSynapse.Windows.Devices;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHidWorkspace
{
    [NotProfileScoped]
    internal async Task ApplyNommoProfilesAsync(PowerSourceKind powerSource, CancellationToken cancellationToken)
    {
        foreach (var connection in CurrentConnections.Where(c => c.IsReady && c.Definition.ProductId is 0x0517 or 0x0518))
        {
            try
            {
                if (SelectedDevice is { Speaker: { } speaker } selected &&
                    StringComparer.OrdinalIgnoreCase.Equals(selected.Connection.InstanceId, connection.InstanceId))
                {
                    await speaker.ApplyPersistedProfileAsync(powerSource, cancellationToken);
                    continue;
                }
                await using var service = RazerSpeakerAccessoryService.OpenNommo(connection);
                await service.ApplyNommoProfileAsync(_getProfile(), connection.ToDescriptor(), powerSource, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                _diagnosticLog.TryWrite("nommo", $"{connection.InstanceId}: profile replay failed: {exception.Message}");
            }
        }
    }
}
