using System.Collections.ObjectModel;
using System.ComponentModel;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using OpenSynapse.Core.Lighting;
using Windows.Foundation;
using Windows.UI;

namespace OpenSynapse.App.ViewModels;

/// <summary>One scene row in the browse panel.</summary>
public sealed class StudioSceneCard : INotifyPropertyChanged
{
    private static readonly SolidColorBrush AccentBrush =
        new(Color.FromArgb(255, 0x44, 0xD6, 0x2C));
    private static readonly SolidColorBrush ClearBrush =
        new(Colors.Transparent);
    private bool _selected;

    public StudioSceneCard(StudioScene scene)
    {
        Scene = scene;
        var layer = scene.Layers.FirstOrDefault();
        var primary = layer?.Primary ?? new StudioRgb(0x44, 0xD6, 0x2C);
        var secondary = layer?.Secondary ?? new StudioRgb(0x00, 0xBF, 0xFF);
        SwatchBrush = new SolidColorBrush(ToColor(primary));
        var gradient = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1),
            Opacity = 0.32,
        };
        gradient.GradientStops.Add(new GradientStop { Color = ToColor(primary), Offset = 0 });
        gradient.GradientStops.Add(new GradientStop { Color = ToColor(secondary), Offset = 1 });
        BackgroundBrush = gradient;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public StudioScene Scene { get; }

    public string Title => AppStrings.Text(Scene.NameKey);

    public string Detail => $"BPM {Scene.Bpm}";

    public string AutomationId => $"StudioSceneCard_{Scene.Id}";

    public Brush BackgroundBrush { get; }

    public Brush SwatchBrush { get; }

    public Brush SelectionBrush => _selected ? AccentBrush : ClearBrush;

    public Visibility CheckVisibility => _selected ? Visibility.Visible : Visibility.Collapsed;

    public void RefreshLocalization() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Title)));

    internal void SetSelected(bool selected)
    {
        if (_selected == selected) return;
        _selected = selected;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectionBrush)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CheckVisibility)));
    }

    private static Color ToColor(StudioRgb color) => Color.FromArgb(255, color.R, color.G, color.B);
}

/// <summary>Whole-set scenes, browse/edit mode, and scene materialization.</summary>
public sealed partial class LightingStudioViewModel
{
    private StudioScene? _activeScene;
    private StudioProfile? _sceneProfile;
    private bool _isEditing;

    public ObservableCollection<StudioSceneCard> SceneCards { get; } = new();

    public StudioSceneCard? SelectedSceneCard
    {
        get => SceneCards.FirstOrDefault(card => card.Scene == _activeScene);
        set
        {
            if (value is null || ReferenceEquals(value.Scene, _activeScene)) return;
            SelectScene(value.Scene);
        }
    }

    public bool IsEditing
    {
        get => _isEditing;
        private set
        {
            if (_isEditing == value) return;
            _isEditing = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(EditButtonText));
        }
    }

    public string EditButtonText =>
        AppStrings.Text(IsEditing ? "LightingStudioDone" : "LightingStudioEdit");

    public string DeviceSummary => AppStrings.FormatText(
        "LightingStudioDeviceSummary",
        Devices.Count(device => device.HasMatrix && !device.HasNoPixelOutput),
        Devices.Count(device => device.IsSingleZone || (device.Rows == 1 && device.Columns == 1)));

    public void ToggleEditing()
    {
        if (!IsEditing && _activeScene is { } scene) MaterializeScene(scene);
        IsEditing = !IsEditing;
        if (!IsEditing) SelectedSceneCard = null;
    }

    public void SelectScene(StudioScene scene)
    {
        _activeScene = scene;
        UpdateSceneSelection();
        _sceneProfile = null;
        InvalidateWorld();
        NotifyDocumentChanged();
        OnPropertyChanged(nameof(SelectedSceneCard));
        StatusText = AppStrings.FormatText("LightingStudioStatusSceneSelected", AppStrings.Text(scene.NameKey));
    }

    internal void LoadSceneCards()
    {
        SceneCards.Clear();
        foreach (var scene in StudioSceneCatalog.All) SceneCards.Add(new StudioSceneCard(scene));
    }

    internal StudioProfile? CurrentProfile =>
        _activeScene is { } scene
            ? _sceneProfile ??= StudioSceneRenderer.BuildProfile(
                scene, _project.SelectedProfile, AppStrings.Text(scene.NameKey))
            : _project.SelectedProfile;

    /// <summary>Applies the scene to the user's project only when they open the editor.</summary>
    private void MaterializeScene(StudioScene scene)
    {
        var profile = StudioSceneRenderer.BuildProfile(
            scene, _project.SelectedProfile, AppStrings.Text(scene.NameKey));
        _project.Profiles.Add(profile);
        _project.SelectedProfileId = profile.Id;
        AttachProfile(profile);
        SyncLayersFromProfile();
        RebuildLayerRows();
        SelectedLayer = Layers.FirstOrDefault();
        SelectedLayerRow = LayerRows.FirstOrDefault();
        RefreshRegionChoices();
        _history = new StudioHistory(_project);
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        OnPropertyChanged(nameof(Profile));
        OnPropertyChanged(nameof(Profiles));
        OnPropertyChanged(nameof(SelectedProfile));
        _activeScene = null;
        UpdateSceneSelection();
        _sceneProfile = null;
        InvalidateWorld();
        NotifyDocumentChanged();
        OnPropertyChanged(nameof(SelectedSceneCard));
        StatusText = AppStrings.FormatText("LightingStudioStatusSceneEditing", AppStrings.Text(scene.NameKey));
    }

    private void UpdateSceneSelection()
    {
        foreach (var card in SceneCards) card.SetSelected(ReferenceEquals(card.Scene, _activeScene));
    }
}
