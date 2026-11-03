using System.Collections.ObjectModel;
using OpenSynapse.Core.Lighting;

namespace OpenSynapse.App.ViewModels;

/// <summary>One visible tree row: a layer or group plus its nesting depth.</summary>
public sealed record StudioLayerRow(StudioLayerItem Layer, int Depth)
{
    public bool IsGroup => Layer.IsGroup;
}

/// <summary>Profile, group, tree, and undo commands for the studio page.</summary>
public sealed partial class LightingStudioViewModel
{
    private StudioLayerRow? _selectedLayerRow;
    private StudioLayerItem? _selectedGroup;

    public ObservableCollection<StudioLayerRow> LayerRows { get; } = new();

    public ObservableCollection<StudioLayerItem> GroupChoices { get; } = new();

    public ObservableCollection<StudioProfile> Profiles => _project.Profiles;

    public StudioProfile? SelectedProfile
    {
        get => _project.SelectedProfile;
        set => SelectProfile(value);
    }

    public StudioLayerRow? SelectedLayerRow
    {
        get => _selectedLayerRow;
        set
        {
            if (ReferenceEquals(_selectedLayerRow, value)) return;
            _selectedLayerRow = value;
            SelectedLayer = value?.Layer;
            OnPropertyChanged();
        }
    }

    public StudioLayerItem? SelectedGroup
    {
        get => _selectedGroup;
        set
        {
            if (ReferenceEquals(_selectedGroup, value)) return;
            _selectedGroup = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Fills the visible rows on page entry; the constructor only fills the flat mirror.</summary>
    public void EnsureLayerRows()
    {
        if (LayerRows.Count > 0) return;
        RebuildLayerRows();
        SelectedLayerRow = LayerRows.FirstOrDefault(row => row.Layer == _selectedLayer);
    }

    public bool CanUndo => _history?.CanUndo == true;

    public bool CanRedo => _history?.CanRedo == true;

    public void SelectProfile(StudioProfile? profile)
    {
        if (profile is null || _project.SelectedProfileId == profile.Id) return;
        var previous = _project.SelectedProfile;
        _history?.Capture();
        _project.SelectedProfileId = profile.Id;
        LoadProfile(previous);
        _history?.Capture();
    }

    public void AddProfile()
    {
        var previous = _project.SelectedProfile;
        _history?.Capture();
        _project.AddProfile(AppStrings.FormatText("LightingStudioProfileName", _project.Profiles.Count + 1));
        LoadProfile(previous);
        _history?.Capture();
    }

    public void DuplicateProfile()
    {
        var previous = _project.SelectedProfile;
        if (previous is null || _project.DuplicateProfile(previous) is null) return;
        _history?.Capture();
        LoadProfile(previous);
        _history?.Capture();
    }

    public void RemoveProfile()
    {
        var previous = _project.SelectedProfile;
        if (previous is null || _project.Profiles.Count <= 1) return;
        _history?.Capture();
        _project.RemoveProfile(previous);
        LoadProfile(previous);
        _history?.Capture();
    }

    public void RenameProfile(string? title)
    {
        if (_project.SelectedProfile is not { } profile) return;
        if (!_project.RenameProfile(profile, title)) return;
        _history?.Capture();
        OnPropertyChanged(nameof(SelectedProfile));
    }

    public void AddGroup()
    {
        if (_project.SelectedProfile is not { } profile) return;
        var group = profile.AddGroup(AppStrings.FormatText(
            "LightingStudioGroupName", profile.Items.Count + 1));
        _history?.Capture();
        RebuildLayerRows();
        SelectedLayer = group;
        SelectedLayerRow = LayerRows.FirstOrDefault(row => row.Layer == group);
        NotifyDocumentChanged();
    }

    public void MoveIntoGroup()
    {
        if (_selectedLayer is null || _selectedGroup is null) return;
        MoveLayerIntoGroup(_selectedLayer, _selectedGroup);
    }

    public void MoveToRoot()
    {
        if (_selectedLayer is null) return;
        MoveLayerToRoot(_selectedLayer);
    }

    /// <summary>Groups keep their place at the root, so only layers can be dragged.</summary>
    public bool CanDragLayer(StudioLayerItem layer) => !layer.IsGroup;

    /// <summary>Any group row accepts a layer, including the group it already sits in.</summary>
    public bool CanDropIntoGroup(StudioLayerItem layer, StudioLayerItem group) =>
        !layer.IsGroup && group.IsGroup;

    /// <summary>Only a layer nested in a group can be dragged back to the root stack.</summary>
    public bool CanDropToRoot(StudioLayerItem layer) => !layer.IsGroup && IsInsideGroup(layer);

    public bool MoveLayerIntoGroup(StudioLayerItem layer, StudioLayerItem group)
    {
        if (!CanDropIntoGroup(layer, group) || _project.SelectedProfile is not { } profile) return false;
        if (group.Children.Contains(layer)) return true;
        if (!profile.MoveIntoGroup(layer, group)) return false;
        CommitLayerMove(layer);
        return true;
    }

    public bool MoveLayerToRoot(StudioLayerItem layer)
    {
        if (!CanDropToRoot(layer) || _project.SelectedProfile is not { } profile) return false;
        if (!profile.MoveToRoot(layer)) return false;
        CommitLayerMove(layer);
        return true;
    }

    public void DuplicateLayer()
    {
        if (_selectedLayer is null || _project.SelectedProfile is not { } profile) return;
        var copy = profile.DuplicateItem(_selectedLayer);
        if (copy is null) return;
        _history?.Capture();
        RebuildLayerRows();
        SelectedLayer = copy;
        SelectedLayerRow = LayerRows.FirstOrDefault(row => row.Layer == copy);
        NotifyDocumentChanged();
    }

    public void MoveDevice(string instanceId, double x, double y)
    {
        if (_project.SelectedProfile is not { } profile || string.IsNullOrEmpty(instanceId)) return;
        var placement = profile.Devices.FirstOrDefault(item =>
            string.Equals(item.InstanceId, instanceId, StringComparison.OrdinalIgnoreCase));
        if (placement is null)
        {
            placement = new StudioDevicePlacement { InstanceId = instanceId };
            profile.Devices.Add(placement);
        }

        placement.X = Math.Clamp(x, 0, 10000);
        placement.Y = Math.Clamp(y, 0, 10000);
        _history?.Capture();
        NotifyDocumentChanged();
    }

    public void Undo()
    {
        if (_history?.Undo() is not { } project) return;
        ApplyProject(project, resetHistory: false);
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    public void Redo()
    {
        if (_history?.Redo() is not { } project) return;
        ApplyProject(project, resetHistory: false);
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    private void LoadProfile(StudioProfile? previous)
    {
        DetachProfile(previous);
        AttachProfile(_project.SelectedProfile);
        SyncLayersFromProfile();
        RebuildLayerRows();
        SelectedLayer = Layers.FirstOrDefault();
        SelectedLayerRow = LayerRows.FirstOrDefault();
        OnPropertyChanged(nameof(Profile));
        OnPropertyChanged(nameof(Profiles));
        OnPropertyChanged(nameof(SelectedProfile));
        NotifyDocumentChanged();
    }

    private void RebuildLayerRows()
    {
        LayerRows.Clear();
        GroupChoices.Clear();
        if (_project.SelectedProfile is { } profile)
        {
            foreach (var item in profile.Items) AddRow(item, 0);
        }

        if (_selectedGroup is not null && !GroupChoices.Contains(_selectedGroup))
        {
            _selectedGroup = GroupChoices.FirstOrDefault();
            OnPropertyChanged(nameof(SelectedGroup));
        }

        OnPropertyChanged(nameof(LayerRows));
        OnPropertyChanged(nameof(GroupChoices));
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    private void AddRow(StudioLayerItem item, int depth)
    {
        LayerRows.Add(new StudioLayerRow(item, depth));
        if (item.IsGroup) GroupChoices.Add(item);
        foreach (var child in item.Children) AddRow(child, depth + 1);
    }

    private void MoveSelected(int delta)
    {
        if (_selectedLayer is null || _project.SelectedProfile is not { } profile) return;
        var container = FindContainer(profile, _selectedLayer);
        if (container is null) return;
        var index = container.IndexOf(_selectedLayer);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= container.Count) return;
        container.Move(index, target);
        _history?.Capture();
        RebuildLayerRows();
        SelectedLayerRow = LayerRows.FirstOrDefault(row => row.Layer == _selectedLayer);
        NotifyDocumentChanged();
    }

    private bool IsInsideGroup(StudioLayerItem layer) =>
        _project.SelectedProfile is { } profile &&
        profile.EnumerateLayers().Any(group => group.IsGroup && group.Children.Contains(layer));

    private void CommitLayerMove(StudioLayerItem layer)
    {
        _history?.Capture();
        RebuildLayerRows();
        SelectedLayer = layer;
        SelectedLayerRow = LayerRows.FirstOrDefault(row => row.Layer == layer);
        NotifyDocumentChanged();
    }

    private static ObservableCollection<StudioLayerItem>? FindContainer(
        StudioProfile profile, StudioLayerItem item)
    {
        if (profile.Items.Contains(item)) return profile.Items;
        foreach (var group in profile.EnumerateLayers())
        {
            if (group.IsGroup && group.Children.Contains(item)) return group.Children;
        }

        return null;
    }
}
