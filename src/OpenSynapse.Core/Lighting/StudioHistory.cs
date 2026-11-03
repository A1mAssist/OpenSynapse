namespace OpenSynapse.Core.Lighting;

/// <summary>
/// Linear undo/redo over whole-document snapshots from
/// <see cref="StudioProjectCodec"/>. Call <see cref="Capture"/> after each
/// mutation and apply the project returned by <see cref="Undo"/> or
/// <see cref="Redo"/> before capturing again.
/// </summary>
public sealed class StudioHistory
{
    /// <summary>Maximum number of undo steps retained.</summary>
    public const int Capacity = 20;

    private readonly List<string> _snapshots = new();
    private int _index;
    private StudioProject _project;

    public StudioHistory(StudioProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        _project = project;
        _snapshots.Add(StudioProjectCodec.Serialize(project));
    }

    public bool CanUndo => _index > 0;

    public bool CanRedo => _index < _snapshots.Count - 1;

    /// <summary>Records the current project state as a new undo point.</summary>
    public void Capture()
    {
        var json = StudioProjectCodec.Serialize(_project);
        if (_snapshots[_index] == json) return;
        if (_index < _snapshots.Count - 1)
            _snapshots.RemoveRange(_index + 1, _snapshots.Count - _index - 1);
        _snapshots.Add(json);
        if (_snapshots.Count > Capacity + 1) _snapshots.RemoveAt(0);
        _index = _snapshots.Count - 1;
    }

    /// <summary>Restores the previous snapshot; null when no undo step remains.</summary>
    public StudioProject? Undo()
    {
        if (!CanUndo) return null;
        _index--;
        _project = StudioProjectCodec.Deserialize(_snapshots[_index]);
        return _project;
    }

    /// <summary>Restores the next snapshot; null when no redo step remains.</summary>
    public StudioProject? Redo()
    {
        if (!CanRedo) return null;
        _index++;
        _project = StudioProjectCodec.Deserialize(_snapshots[_index]);
        return _project;
    }
}
