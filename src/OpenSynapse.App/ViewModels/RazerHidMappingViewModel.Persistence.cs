using OpenSynapse.Core.Profiles;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHidMappingViewModel
{
    private sealed record AssignmentBackup(
        RazerHidMappingRowViewModel Row,
        RazerMappingAssignment Assignment);

    private async Task RestoreAssignmentsAsync(IReadOnlyList<AssignmentBackup> backups)
    {
        using var restore = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        foreach (var backup in backups)
        {
            try
            {
                await _transport.SetAssignmentAsync(
                    backup.Row.ButtonId,
                    Mode,
                    backup.Assignment.FunctionId,
                    backup.Assignment.Data,
                    restore.Token).ConfigureAwait(true);
            }
            catch (Exception exception) when (IsExpected(exception) ||
                                               exception is OperationCanceledException)
            {
                _log?.Invoke(
                    $"mapping button {backup.Row.ButtonId} restore failed: {exception.Message}");
            }
        }
    }

    private async Task PersistMappingProfileAsync(CancellationToken cancellationToken)
    {
        var mappings = new List<ButtonMappingProfile>(Rows.Count);
        foreach (var row in Rows)
        {
            try
            {
                var assignment = await _transport.GetAssignmentAsync(
                    row.ButtonId, Mode, cancellationToken).ConfigureAwait(true);
                var decoded = RazerKeypadInputCatalog.UsesHostMapping(_productId)
                    ? RazerHidMappingActions.Decode(assignment.FunctionId, assignment.Data)
                    : _catalog is RazerViperMappingActionCatalog
                    ? null
                    : _catalog.Decode(assignment.FunctionId, assignment.Data);
                var mapping = RazerHidMappingActions.ToProfileMapping(
                    row.ButtonId,
                    Mode,
                    assignment.FunctionId,
                    assignment.Data,
                    decoded);
                if (RazerHuntsmanV3ProKeys.UsesFirmwareKeyIds(_productId))
                {
                    mapping.Extra ??= new Dictionary<string, string>(StringComparer.Ordinal);
                    mapping.Extra[RazerHuntsmanV3ProKeys.ProfileKeyIdMarker] =
                        RazerHuntsmanV3ProKeys.ProfileFirmwareKeyIds;
                }

                mappings.Add(mapping);
            }
            catch (Exception exception) when (IsExpected(exception))
            {
                _log?.Invoke(
                    $"mapping button {row.ButtonId} profile snapshot failed: {exception.Message}");
                if (RazerKeypadInputCatalog.UsesHostMapping(_productId))
                {
                    throw;
                }
            }
        }

        if (mappings.Count > 0)
        {
            await _mappingProfileSaver!(mappings, cancellationToken).ConfigureAwait(true);
        }
    }
}
