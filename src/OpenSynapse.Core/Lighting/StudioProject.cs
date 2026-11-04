using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace OpenSynapse.Core.Lighting;

public enum StudioLayerType
{
    Layer,
    Group,
}

/// <summary>
/// Chroma Studio document: named profiles, each with a top-first layer tree,
/// regions that target specific device LEDs, and canvas device placement.
/// </summary>
public sealed class StudioProject : INotifyPropertyChanged
{
    /// <summary>Written as <c>openSynapse.schemaVersion</c>; readers reject newer documents.</summary>
    public const int CurrentSchemaVersion = 2;
    private string _name = "Untitled Studio";
    private string? _selectedProfileId;

    public StudioProject()
    {
        Profiles.CollectionChanged += OnProfilesChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name
    {
        get => _name;
        set
        {
            var trimmed = string.IsNullOrWhiteSpace(value) ? "Untitled Studio" : value.Trim();
            if (_name == trimmed) return;
            _name = trimmed;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
        }
    }

    public ObservableCollection<StudioProfile> Profiles { get; } = new();

    public string? SelectedProfileId
    {
        get => _selectedProfileId;
        set
        {
            if (_selectedProfileId == value) return;
            _selectedProfileId = value;
            Raise(nameof(SelectedProfileId));
            Raise(nameof(SelectedProfile));
        }
    }

    public StudioProfile? SelectedProfile =>
        Profiles.FirstOrDefault(profile => profile.Id == _selectedProfileId) ?? Profiles.FirstOrDefault();

    public static StudioProject CreateDefault()
    {
        var project = new StudioProject();
        var profile = new StudioProfile("Default") { IsDefault = true };
        profile.Items.Add(StudioLayerItem.CreateLayer("Background", StudioEffectKind.Solid));
        project.Profiles.Add(profile);
        project.SelectedProfileId = profile.Id;
        return project;
    }

    /// <summary>Adds a profile and selects it.</summary>
    public StudioProfile AddProfile(string? title = null)
    {
        var profile = new StudioProfile(title ?? string.Empty);
        Profiles.Add(profile);
        SelectedProfileId = profile.Id;
        return profile;
    }

    /// <summary>Renames a profile owned by this project; false for a foreign profile.</summary>
    public bool RenameProfile(StudioProfile profile, string? title)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (!Profiles.Contains(profile)) return false;
        profile.Title = title ?? string.Empty;
        return true;
    }

    /// <summary>Copies a profile after its source with fresh ids and selects the copy.</summary>
    public StudioProfile? DuplicateProfile(StudioProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var index = Profiles.IndexOf(profile);
        if (index < 0) return null;
        var copy = new StudioProfile(profile.Title + " copy");
        foreach (var item in profile.Items) copy.Items.Add(item.Clone());
        foreach (var device in profile.Devices) copy.Devices.Add(device.Clone());
        Profiles.Insert(index + 1, copy);
        SelectedProfileId = copy.Id;
        return copy;
    }

    /// <summary>
    /// Removes a profile and returns the resulting selection: the next profile,
    /// otherwise the previous one. The last profile is kept.
    /// </summary>
    public StudioProfile? RemoveProfile(StudioProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var index = Profiles.IndexOf(profile);
        if (index < 0) return null;
        if (Profiles.Count == 1) return SelectedProfile;
        if (SelectedProfileId == profile.Id)
            SelectedProfileId = Profiles[index + 1 < Profiles.Count ? index + 1 : index - 1].Id;
        Profiles.RemoveAt(index);
        return SelectedProfile;
    }

    private void OnProfilesChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (SelectedProfileId is null || Profiles.All(profile => profile.Id != SelectedProfileId))
        {
            _selectedProfileId = Profiles.FirstOrDefault()?.Id;
            Raise(nameof(SelectedProfileId));
        }

        Raise(nameof(SelectedProfile));
    }

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class StudioProfile : INotifyPropertyChanged
{
    private string _title;
    private bool _isDefault;

    public StudioProfile(string title)
    {
        Id = NewId();
        _title = string.IsNullOrWhiteSpace(title) ? "Profile" : title.Trim();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Twelve lowercase hex characters, matching the Chroma export ids.</summary>
    public string Id { get; init; }

    /// <summary>
    /// Chroma's optional <c>default</c> profile marker. Import normalizes a
    /// document to exactly one default profile, falling back to the first.
    /// </summary>
    public bool IsDefault
    {
        get => _isDefault;
        set
        {
            if (_isDefault == value) return;
            _isDefault = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDefault)));
        }
    }

    public string Title
    {
        get => _title;
        set
        {
            var trimmed = string.IsNullOrWhiteSpace(value) ? "Profile" : value.Trim();
            if (_title == trimmed) return;
            _title = trimmed;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Title)));
        }
    }

    /// <summary>Top of the stack first.</summary>
    public ObservableCollection<StudioLayerItem> Items { get; } = new();

    /// <summary>Canvas placement; exported as the profile-level Chroma devices.</summary>
    public ObservableCollection<StudioDevicePlacement> Devices { get; } = new();

    public IEnumerable<StudioLayerItem> EnumerateLayers()
    {
        foreach (var item in Items)
        {
            yield return item;
            foreach (var child in item.EnumerateDescendants()) yield return child;
        }
    }

    /// <summary>Adds an empty group at the top of the stack.</summary>
    public StudioLayerItem AddGroup(string? title = null)
    {
        var group = StudioLayerItem.CreateGroup(string.IsNullOrWhiteSpace(title) ? "Group" : title);
        Items.Insert(0, group);
        return group;
    }

    /// <summary>
    /// Moves a layer into a group at the top of its children. Groups cannot be
    /// moved into groups, into themselves or into their descendants.
    /// </summary>
    public bool MoveIntoGroup(StudioLayerItem item, StudioLayerItem group)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(group);
        if (item.IsGroup || !group.IsGroup) return false;
        if (ReferenceEquals(item, group) || item.EnumerateDescendants().Contains(group)) return false;
        var container = FindContainer(item, out _);
        if (container is null || FindContainer(group, out _) is null) return false;
        if (ReferenceEquals(container, group.Children)) return true;
        container.Remove(item);
        group.Children.Insert(0, item);
        if (!group.Visible) item.Visible = false;
        return true;
    }

    /// <summary>Moves an item back to the top-level stack at the given index.</summary>
    public bool MoveToRoot(StudioLayerItem item, int index = 0)
    {
        ArgumentNullException.ThrowIfNull(item);
        var container = FindContainer(item, out _);
        if (container is null) return false;
        if (ReferenceEquals(container, Items))
        {
            var current = Items.IndexOf(item);
            var target = Math.Clamp(index, 0, Items.Count - 1);
            if (current != target) Items.Move(current, target);
            return true;
        }

        container.Remove(item);
        Items.Insert(Math.Clamp(index, 0, Items.Count), item);
        return true;
    }

    /// <summary>Copies an item after its source, regenerating every id.</summary>
    public StudioLayerItem? DuplicateItem(StudioLayerItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var container = FindContainer(item, out _);
        if (container is null) return null;
        var copy = item.Clone();
        container.Insert(container.IndexOf(item) + 1, copy);
        return copy;
    }

    /// <summary>
    /// Removes an item and returns the next sibling, the previous sibling, the
    /// parent group, or null when the root becomes empty.
    /// </summary>
    public StudioLayerItem? RemoveItem(StudioLayerItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var container = FindContainer(item, out var parent);
        if (container is null) return null;
        var index = container.IndexOf(item);
        container.RemoveAt(index);
        if (container.Count > 0) return container[Math.Min(index, container.Count - 1)];
        return parent;
    }

    private ObservableCollection<StudioLayerItem>? FindContainer(StudioLayerItem item, out StudioLayerItem? parent)
    {
        parent = null;
        if (Items.Contains(item)) return Items;
        foreach (var candidate in EnumerateLayers())
        {
            if (!candidate.IsGroup || !candidate.Children.Contains(item)) continue;
            parent = candidate;
            return candidate.Children;
        }

        return null;
    }

    public static string NewId() => Guid.NewGuid().ToString("N")[..12];
}

public sealed class StudioLayerItem : INotifyPropertyChanged
{
    private string _title = "Layer";
    private bool _visible = true;
    private bool _opened;

    public event PropertyChangedEventHandler? PropertyChanged;

    public Guid Id { get; set; } = Guid.NewGuid();

    public StudioLayerType Type { get; set; }

    public bool IsGroup => Type == StudioLayerType.Group;

    public string Title
    {
        get => _title;
        set
        {
            var trimmed = string.IsNullOrWhiteSpace(value) ? "Layer" : value.Trim();
            if (_title == trimmed) return;
            _title = trimmed;
            Raise(nameof(Title));
        }
    }

    /// <summary>Toggling a group also sets its direct children, matching Chroma.</summary>
    public bool Visible
    {
        get => _visible;
        set
        {
            if (_visible == value) return;
            _visible = value;
            Raise(nameof(Visible));
            if (!IsGroup) return;
            foreach (var child in Children) child.Visible = value;
        }
    }

    public bool Opened
    {
        get => _opened;
        set
        {
            if (_opened == value) return;
            _opened = value;
            Raise(nameof(Opened));
        }
    }

    public StudioEffectParams Params { get; set; } = new();

    /// <summary>Effect kind for the layer panel; forwards to <see cref="Params"/>.</summary>
    public StudioEffectKind Effect => Params.Effect;

    /// <summary>Primary swatch for the layer panel; forwards to <see cref="Params"/>.</summary>
    public StudioRgb PrimaryColor => Params.Primary;

    /// <summary>Secondary swatch for the layer panel; forwards to <see cref="Params"/>.</summary>
    public StudioRgb SecondaryColor => Params.Secondary;

    /// <summary>Raises <see cref="Effect"/> after the params effect changes in place.</summary>
    public void NotifyEffectChanged() => Raise(nameof(Effect));

    public void NotifyColorChanged()
    {
        Raise(nameof(PrimaryColor));
        Raise(nameof(SecondaryColor));
    }

    public ObservableCollection<StudioRegion> Regions { get; } = new();

    /// <summary>Group children; empty for a layer.</summary>
    public ObservableCollection<StudioLayerItem> Children { get; } = new();

    /// <summary>Preserved from studio schema v1; not part of .ChromaEffects.</summary>
    public double Opacity { get; set; } = 100;

    /// <summary>Preserved from studio schema v1; not part of .ChromaEffects.</summary>
    public StudioBlendMode Blend { get; set; }

    public IEnumerable<StudioLayerItem> EnumerateDescendants()
    {
        foreach (var child in Children)
        {
            yield return child;
            foreach (var descendant in child.EnumerateDescendants()) yield return descendant;
        }
    }

    public static StudioLayerItem CreateLayer(string title, StudioEffectKind effect) => new()
    {
        Type = StudioLayerType.Layer,
        Title = title,
        Params = new StudioEffectParams { Effect = effect },
    };

    public static StudioLayerItem CreateGroup(string title) => new()
    {
        Type = StudioLayerType.Group,
        Title = title,
    };

    public StudioLayerItem Clone(bool regenerateIds = true)
    {
        var clone = new StudioLayerItem
        {
            Id = regenerateIds ? Guid.NewGuid() : Id,
            Type = Type,
            Title = Title,
            Visible = Visible,
            Opened = Opened,
            Params = Params.Clone(),
            Opacity = Opacity,
            Blend = Blend,
        };
        foreach (var region in Regions)
        {
            var regionClone = new StudioRegion { Params = region.Params.Clone() };
            foreach (var device in region.Devices) regionClone.Devices.Add(device.Clone());
            clone.Regions.Add(regionClone);
        }

        foreach (var child in Children) clone.Children.Add(child.Clone(regenerateIds));
        return clone;
    }

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class StudioRegion
{
    public StudioEffectParams Params { get; set; } = new();

    public ObservableCollection<StudioRegionDevice> Devices { get; } = new();
}

public sealed class StudioRegionDevice
{
    public string? Id { get; set; }

    public ushort ProductId { get; set; }

    public string? InstanceId { get; set; }

    public string? DeviceId { get; set; }

    public ObservableCollection<StudioLedRef> Leds { get; } = new();

    public StudioRegionDevice Clone()
    {
        var clone = new StudioRegionDevice
        {
            Id = Id,
            ProductId = ProductId,
            InstanceId = InstanceId,
            DeviceId = DeviceId,
        };
        foreach (var led in Leds) clone.Leds.Add(led);
        return clone;
    }
}

/// <summary>
/// One LED inside a region. Keyboard keys keep InputType/InputData from the
/// file; this importer preserves them as authored instead of rebuilding
/// coordinates from a different layout.
/// </summary>
public readonly record struct StudioLedRef(
    int Row,
    int Col,
    int? RegionId = null,
    int? RegionIndex = null,
    string? InputType = null,
    string? InputData = null,
    string? InstanceId = null);

/// <summary>One device placement from the profile's Chroma devices array.</summary>
public sealed class StudioDevicePlacement
{
    /// <summary>Stable Chroma device id; generated from the identity when absent.</summary>
    public string? Id { get; set; }

    public string InstanceId { get; set; } = string.Empty;

    public ushort ProductId { get; set; }

    public string? DeviceId { get; set; }

    public string? Category { get; set; }

    public string? ProductName { get; set; }

    public string? ProductNameZhCn { get; set; }

    /// <summary>Raw device config from the export; Chroma uses it for LED mapping.</summary>
    public string? ConfigJson { get; set; }

    /// <summary>Raw top-level device LED list, preserved for Chroma round-trips.</summary>
    public string? LedsJson { get; set; }

    /// <summary>Chroma layout: an integer or string depending on the device.</summary>
    public string? Layout { get; set; }

    public double Width { get; set; }

    public double Height { get; set; }

    public double X { get; set; } = 5000;

    public double Y { get; set; } = 5000;

    /// <summary>0, 90, 180 or 270 degrees.</summary>
    public int Rotation { get; set; }

    /// <summary>Parent device's product id; empty when the placement has no parent.</summary>
    public string? ParentId { get; set; }

    /// <summary>Canvas x of the parent when this placement was attached.</summary>
    public double? ParentX { get; set; }

    /// <summary>Canvas y of the parent when this placement was attached.</summary>
    public double? ParentY { get; set; }

    /// <summary>Placement slot Chroma picked under <see cref="ParentId"/>.</summary>
    public int? PlacementIndex { get; set; }

    /// <summary>Raw LED snapshot Chroma keeps to remap effects when the layout changes.</summary>
    public string? LedRefJson { get; set; }

    public StudioDevicePlacement Clone() => new()
    {
        Id = Id,
        InstanceId = InstanceId,
        ProductId = ProductId,
        DeviceId = DeviceId,
        Category = Category,
        ProductName = ProductName,
        ProductNameZhCn = ProductNameZhCn,
        ConfigJson = ConfigJson,
        LedsJson = LedsJson,
        Layout = Layout,
        Width = Width,
        Height = Height,
        X = X,
        Y = Y,
        Rotation = Rotation,
        ParentId = ParentId,
        ParentX = ParentX,
        ParentY = ParentY,
        PlacementIndex = PlacementIndex,
        LedRefJson = LedRefJson,
    };
}
