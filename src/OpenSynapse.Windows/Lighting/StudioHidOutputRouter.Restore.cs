using OpenSynapse.Windows.Devices;

namespace OpenSynapse.Windows.Lighting;

public sealed partial class StudioHidOutputRouter
{
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
