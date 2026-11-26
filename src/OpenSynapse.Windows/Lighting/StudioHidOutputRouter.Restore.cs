using OpenSynapse.Windows.Devices;

namespace OpenSynapse.Windows.Lighting;

public sealed partial class StudioHidOutputRouter
{
    public IReadOnlyCollection<string> OwnedInstanceIds =>
        _outputs.Where(pair => pair.Value.Touched).Select(pair => pair.Key).ToArray();

    /// <summary>Optional card restore, called under the gate for live owned targets only.</summary>
    public Func<IReadOnlyCollection<string>, Task>? RestoreTargetsAsync { get; set; }

    /// <summary>
    /// Selects HID InstanceIds. Null means All; an empty collection selects nothing.
    /// Waits for in-flight writes and restores only departing targets. Failures stay
    /// pending and are reported through RestoreFailed; retained sources keep their timeline.
    /// </summary>
    public async Task SetTargetsAsync(
        IReadOnlyCollection<string>? targetIds, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        var selected = targetIds?.ToHashSet(StringComparer.OrdinalIgnoreCase);
        await _applyGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Volatile.Write(ref _targetIds, selected);
            await RestoreOutputsAsync(false, true, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _applyGate.Release();
        }
    }

    private bool IsSelected(string id) => Volatile.Read(ref _targetIds)?.Contains(id) ?? true;

    private void ReportRestoreFailure(
        RazerHidDeviceConnection connection,
        IReadOnlyList<Exception> restoreFailures,
        List<string> failures)
    {
        var error = restoreFailures.Count == 1
            ? restoreFailures[0]
            : new AggregateException(restoreFailures);
        RestoreFailed?.Invoke(connection.Definition.DisplayName, error);
        failures.Add($"{connection.Definition.DisplayName}: {error.Message}");
    }
}
