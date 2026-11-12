using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace OpenSynapse.App.ViewModels;

internal sealed record AccessoryDiagnostic(
    string Key, string Name, string Backend, string Identity, string Status, string? Error);

public sealed partial class DiagnosticsWorkspace
{
    private readonly Func<IEnumerable<AccessoryDiagnostic>> _readAccessories;
    private readonly Dictionary<string, AccessoryDiagnostic> _accessorySnapshots = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<DiagnosticRowViewModel> _accessoryRows = [];

    internal int AccessoryErrorCount
    {
        get { CaptureAccessorySnapshot(); return _accessorySnapshots.Values.Count(row => !string.IsNullOrWhiteSpace(row.Error)); }
    }

    internal void ForgetAccessory(string key)
    {
        if (_accessorySnapshots.Remove(key)) RefreshAccessoryRows();
    }

    // Re-read at copy time as controls can fail without a discovery refresh. Keep an
    // observed failure after disappearance instead of hiding its only diagnostic identity.
    internal void CaptureAccessorySnapshot()
    {
        var current = _readAccessories().ToArray();
        var currentKeys = current.Select(row => row.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var key in _accessorySnapshots.Keys.Where(key => !currentKeys.Contains(key)).ToArray())
        {
            if (string.IsNullOrWhiteSpace(_accessorySnapshots[key].Error)) _accessorySnapshots.Remove(key);
            else _accessorySnapshots[key] = _accessorySnapshots[key] with { Status = AppStrings.Text("DiagnosticPreviouslyObserved") };
        }
        foreach (var row in current)
        {
            // Null means this device was enumerated but not read, not a successful read.
            _accessorySnapshots[row.Key] = row.Error is null && _accessorySnapshots.TryGetValue(row.Key, out var previous)
                ? row with { Error = previous.Error } : row;
        }
    }

    private void RefreshAccessoryRows()
    {
        foreach (var row in _accessoryRows) Rows.Remove(row);
        _accessoryRows.Clear();
        CaptureAccessorySnapshot();
        if (_accessorySnapshots.Count > 0 && Rows.Count == 1 &&
            Rows[0].Device == AppStrings.Text("Text_CAF15352"))
        {
            Rows.Clear();
        }
        foreach (var snapshot in _accessorySnapshots.Values)
        {
            var failed = !string.IsNullOrWhiteSpace(snapshot.Error);
            var brush = new SolidColorBrush(failed ? Color.FromArgb(255, 255, 107, 107) : Color.FromArgb(255, 68, 214, 44));
            var group = new DiagnosticRowViewModel(snapshot.Name, snapshot.Backend,
                snapshot.Status, snapshot.Identity, brush);
            if (failed) group.AddIssue(new DiagnosticRowViewModel(snapshot.Name, snapshot.Backend,
                AppStrings.Text("Text_6027BEB0"), snapshot.Error!, brush));
            Rows.Add(group);
            _accessoryRows.Add(group);
        }
    }
}
