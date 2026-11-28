using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using OpenSynapse.App.ViewModels;
using OpenSynapse.Core.Lighting;
using Windows.Graphics;
using Windows.System;
using Windows.UI;

namespace OpenSynapse.App.Views;

/// <summary>Standalone visual editor for one .oschroma preset.</summary>
public sealed partial class PresetEditorWindow : Window
{
    private const int ShowWindowRestore = 9;
    private readonly LightingStudioViewModel? _studio;
    private Action<StudioPresetConfig>? _onSaved;
    private readonly PresetEditorViewModel _editor;
    private readonly DispatcherTimer _previewTimer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly DispatcherTimer _autosaveTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly Stopwatch _renderClock = new();
    private bool _allowClose;
    private bool _closePromptActive;
    private bool _loaded;
    private bool _recoveryPrompted;
    private bool _syncingUi;
    private bool _syncingLayerControls;
    private string? _statusNote;

    public PresetEditorWindow(
        AppWindow owner,
        double scale,
        LightingStudioViewModel? studio,
        StudioPresetConfig? initial,
        string? initialPath,
        Action<StudioPresetConfig>? onSaved = null)
    {
        InitializeComponent();
        _studio = studio;
        _onSaved = onSaved;
        if (_studio is not null)
        {
            _studio.Devices.CollectionChanged += OnStudioDevicesChanged;
            _studio.DeviceVisuals.CollectionChanged += OnStudioVisualsChanged;
        }
        _editor = new PresetEditorViewModel(initial, initialPath);
        _editor.Changed += EditorChanged;
        _editor.DocumentReplaced += EditorDocumentReplaced;
        _editor.StateChanged += (_, _) => UpdateUiState();
        RootLayout.DataContext = _editor;

        if (!WindowAppearance.Apply(this))
        {
            RootLayout.Background = (Brush)Application.Current.Resources["CanvasBrush"];
        }

        var appIconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "OpenSynapse.ico");
        if (File.Exists(appIconPath)) AppWindow.SetIcon(appIconPath);

        AppWindow.Closing += OnPresetEditorClosing;
        AppWindow.Changed += OnPreviewWindowChanged;
        Closed += OnClosed;
        RootLayout.Loaded += OnRootLoaded;
        ConfigurePresenter(scale);
        ApplyTitleBarColors();
        PositionOnOwnerDisplay(scale);
        _previewTimer.Tick += (_, _) => RenderPreviewTick();
        _autosaveTimer.Tick += (_, _) => AutosaveIfDirty();
        BindEditor();
        // WinUI's XAML parser rejects a decimal Slider.Minimum attribute here;
        // the runtime property accepts the same value. Set it after the layer
        // values are bound so the clamp does not fire ValueChanged first.
        SpeedSlider.Minimum = 0.1;
        RefreshLocalization();
        PreviewSurface.SizeChanged += (_, _) => LayoutPreviewSurface();
    }

    internal nint Handle => WinRT.Interop.WindowNative.GetWindowHandle(this);

    internal void BringToFront()
    {
        var windowHandle = Handle;
        if (IsIconic(windowHandle)) ShowWindow(windowHandle, ShowWindowRestore);
        Activate();
        SetForegroundWindow(windowHandle);
    }

    internal void RefreshLocalization()
    {
        if (RootLayout is null) return;
        Localized.RefreshTree(RootLayout);
        Title = PresetEditorText.Get("PresetEditorTitle", "Preset editor");
        NewButton.Content = PresetEditorText.Get("PresetEditorNew", "New");
        OpenButton.Content = PresetEditorText.Get("PresetEditorOpen", "Open");
        SaveButton.Content = PresetEditorText.Get("PresetEditorSave", "Save");
        SaveAsButton.Content = PresetEditorText.Get("LightingStudioPresetSaveAs", "Save as");
        ValidateButton.Content = PresetEditorText.Get("PresetEditorValidate", "Validate");
        ValidateAllButton.Content = PresetEditorText.Get("PresetEditorValidateAll", "Validate all");
        UndoButton.Content = PresetEditorText.Get("PresetEditorUndo", "Undo");
        RedoButton.Content = PresetEditorText.Get("PresetEditorRedo", "Redo");
        PlayButton.Content = _playing
            ? PresetEditorText.Get("PresetEditorPause", "Pause")
            : PresetEditorText.Get("PresetEditorPlay", "Play");
        AutomationProperties.SetName(PlayButton, PlayButton.Content as string ?? "Play");
        ToolTipService.SetToolTip(OpenButton,
            PresetEditorText.Get("PresetEditorOpenFileTitle", "Open .oschroma"));
        ToolTipService.SetToolTip(SaveAsButton,
            PresetEditorText.Get("PresetEditorSaveAsTitle", "Save preset as"));
        AutomationProperties.SetName(RootLayout,
            PresetEditorText.Get("PresetEditorPresetLibrary", "Preset library"));

        InfoHeader.Text = PresetEditorText.Get("PresetEditorInfo", "Preset information");
        NameLabel.Text = PresetEditorText.Get("PresetEditorName", "Name");
        IdLabel.Text = PresetEditorText.Get("PresetEditorId", "ID");
        IdHintText.Text = PresetEditorText.Get("PresetEditorIdHint",
            "Lowercase letters, digits and hyphens.");
        KindLabel.Text = PresetEditorText.Get("PresetEditorKind", "Kind");
        CategoryLabel.Text = PresetEditorText.Get("PresetEditorCategory", "Category");
        NoteLabel.Text = PresetEditorText.Get("PresetEditorNote", "Note");
        LayersHeader.Text = PresetEditorText.Get("LightingStudioPresetLayers", "Layers");
        AddLayerButton.Content = PresetEditorText.Get("PresetEditorAddLayer", "Add layer");
        RemoveLayerButton.Content = PresetEditorText.Get("PresetEditorRemoveLayer", "Remove layer");
        DuplicateLayerButton.Content = PresetEditorText.Get("PresetEditorDuplicateLayer", "Duplicate layer");
        MoveLayerUpButton.Content = PresetEditorText.Get("PresetEditorMoveUp", "Move up");
        MoveLayerDownButton.Content = PresetEditorText.Get("PresetEditorMoveDown", "Move down");
        LayerLimitNotice.Text = _editor.LayerLimitText;
        AutomationProperties.SetName(AddLayerButton, AddLayerButton.Content as string);

        PreviewHeader.Text = PresetEditorText.Get("PresetEditorPreview", "Preview");
        TimelineLabel.Text = PresetEditorText.Get("PresetEditorTimeline", "Timeline");
        LoopToggle.OnContent = PresetEditorText.Get("PresetEditorLoop", "Loop");
        LoopToggle.OffContent = LoopToggle.OnContent;
        AutomationProperties.SetName(DevicePicker, PresetEditorText.Get("PresetEditorDevice", "Device"));
        SimulationHeader.Text = PresetEditorText.Get("PresetEditorSimulation", "Simulation");
        SimKeysToggle.Header = PresetEditorText.Get("PresetEditorSimKeys", "Simulate keys");
        SimScreenButton.Content = PresetEditorText.Get("PresetEditorSimScreenPick", "Choose image");
        AutomationProperties.SetName(SimScreenPanel,
            PresetEditorText.Get("PresetEditorSimScreen", "Screen image"));
        ToolTipService.SetToolTip(SimScreenButton,
            PresetEditorText.Get("PresetEditorSelectScreenImage", "Select a screen image"));
        AutomationProperties.SetName(SimScreenButton, SimScreenButton.Content as string);

        ModuleHeader.Text = PresetEditorText.Get("PresetEditorModule", "Module");
        ParametersHeader.Text = PresetEditorText.Get("PresetEditorParameters", "Parameters");
        PaletteHeader.Text = PresetEditorText.Get("PresetEditorPalette", "Palette");
        PaletteAddButton.Content = PresetEditorText.Get("LightingStudioPresetAddColor", "Add color");
        PaletteResetButton.Content = PresetEditorText.Get("PresetEditorPaletteReset", "Reset palette");
        BlendLabel.Text = PresetEditorText.Get("LightingStudioBlend", "Blend");
        OpacityLabel.Text = PresetEditorText.Get("LightingStudioOpacity", "Opacity");
        SpeedLabel.Text = PresetEditorText.Get("LightingStudioParamSpeed", "Speed");
        LayerSettingsHeader.Text = PresetEditorText.Get("PresetEditorParameters", "Layer output");
        NoIssuesText.Text = PresetEditorText.Get("PresetEditorNoIssues", "No issues");
        NoPreviewText.Text = PresetEditorText.Get("PresetEditorNoPreview", "No preview");
        WorldHintText.Text = PresetEditorText.Get("PresetEditorWorldHint",
            "Using preview devices: a 6 × 22 keyboard and a 1 × 1 zone.");

        BuildKindPicker();
        BuildBlendPicker();
        RefreshPreviewModeItems();
        if (_loaded)
        {
            BuildParameterEditor();
            BuildPaletteEditor();
            UpdateLayerControls();
            UpdateSimulationUi();
            UpdateUiState();
        }
    }

    private void ConfigurePresenter(double scale)
    {
        if (AppWindow.Presenter is not OverlappedPresenter presenter) return;
        var workArea = DisplayArea.Primary.WorkArea;
        presenter.PreferredMinimumWidth = Math.Min(
            (int)Math.Round(1080 * scale), Math.Max(1, workArea.Width));
        presenter.PreferredMinimumHeight = Math.Min(
            (int)Math.Round(680 * scale), Math.Max(1, workArea.Height));
    }

    private void ApplyTitleBarColors()
    {
        if (!AppWindowTitleBar.IsCustomizationSupported()) return;
        var titleBar = AppWindow.TitleBar;
        var background = Color.FromArgb(255, 0x20, 0x20, 0x20);
        var foreground = Color.FromArgb(255, 0xF4, 0xF4, 0xF4);
        titleBar.BackgroundColor = background;
        titleBar.ForegroundColor = foreground;
        titleBar.InactiveBackgroundColor = background;
        titleBar.InactiveForegroundColor = foreground;
        titleBar.ButtonBackgroundColor = background;
        titleBar.ButtonForegroundColor = foreground;
        titleBar.ButtonHoverBackgroundColor = Color.FromArgb(255, 0x38, 0x38, 0x38);
        titleBar.ButtonHoverForegroundColor = foreground;
        titleBar.ButtonPressedBackgroundColor = Color.FromArgb(255, 0x4C, 0x4C, 0x4C);
        titleBar.ButtonPressedForegroundColor = foreground;
    }

    private void PositionOnOwnerDisplay(double scale)
    {
        var workArea = DisplayArea.Primary.WorkArea;
        var logicalWidth = workArea.Width / scale;
        var logicalHeight = workArea.Height / scale;
        var width = (int)Math.Round(Math.Min(1440, logicalWidth) * scale);
        var height = (int)Math.Round(Math.Min(900, logicalHeight) * scale);
        AppWindow.MoveAndResize(new RectInt32(
            workArea.X + Math.Max(0, (workArea.Width - width) / 2),
            workArea.Y + Math.Max(0, (workArea.Height - height) / 2),
            width,
            height));
    }

    private async void OnRootLoaded(object sender, RoutedEventArgs e)
    {
        if (_loaded) return;
        _loaded = true;
        ResetPreviewClock();
        UpdatePreviewTimer();
        _autosaveTimer.Start();
        await HandleRecoveryAsync();
        BuildParameterEditor();
        BuildPaletteEditor();
        UpdateLayerControls();
        UpdateSimulationUi();
        RefreshPreviewDefinition(rebuildWorld: true);
        UpdateUiState();
    }

    private void BindEditor()
    {
        _syncingUi = true;
        try
        {
            NameBox.Text = _editor.Draft.Name;
            IdBox.Text = _editor.Draft.Id;
            CategoryBox.Text = _editor.Draft.Category;
            NoteBox.Text = _editor.Draft.Note ?? string.Empty;
            KindPicker.SelectedValue = _editor.Draft.Kind;
            LayerList.ItemsSource = _editor.Layers;
            LayerList.SelectedItem = _editor.SelectedLayer;
        }
        finally
        {
            _syncingUi = false;
        }

        BuildParameterEditor();
        BuildPaletteEditor();
        UpdateLayerControls();
        UpdateSimulationUi();
        if (_deviceOptions.Count == 0) BuildDeviceOptions();
        UpdateUiState();
    }

    private void EditorChanged(object? sender, EventArgs e)
    {
        RefreshPreviewDefinition(rebuildWorld: true);
        UpdateUiState();
    }

    private void EditorDocumentReplaced(object? sender, EventArgs e)
    {
        ResetPreviewState();
        BindEditor();
        BuildKindPicker();
        BuildBlendPicker();
        RefreshPreviewDefinition(rebuildWorld: true);
    }

    private void UpdateUiState()
    {
        if (RootLayout is null || NameBox is null) return;
        if (!_syncingUi)
        {
            if (NameBox.Text != _editor.Draft.Name) NameBox.Text = _editor.Draft.Name;
            if (IdBox.Text != _editor.Draft.Id) IdBox.Text = _editor.Draft.Id;
            if (CategoryBox.Text != _editor.Draft.Category) CategoryBox.Text = _editor.Draft.Category;
            var note = _editor.Draft.Note ?? string.Empty;
            if (NoteBox.Text != note) NoteBox.Text = note;
            if (KindPicker.SelectedValue is not StudioPresetKind kind || kind != _editor.Draft.Kind)
            {
                KindPicker.SelectedValue = _editor.Draft.Kind;
            }
            if (!ReferenceEquals(LayerList.SelectedItem, _editor.SelectedLayer))
            {
                LayerList.SelectedItem = _editor.SelectedLayer;
            }
        }

        UndoButton.IsEnabled = _editor.CanUndo;
        RedoButton.IsEnabled = _editor.CanRedo;
        AddLayerButton.IsEnabled = _editor.CanAddLayer && _editor.SelectedLayer is not null;
        RemoveLayerButton.IsEnabled = _editor.CanRemoveLayer;
        DuplicateLayerButton.IsEnabled = _editor.CanAddLayer && _editor.SelectedLayer is not null;
        MoveLayerUpButton.IsEnabled = _editor.SelectedLayer is not null &&
            _editor.Layers.IndexOf(_editor.SelectedLayer) > 0;
        MoveLayerDownButton.IsEnabled = _editor.SelectedLayer is not null &&
            _editor.Layers.IndexOf(_editor.SelectedLayer) >= 0 &&
            _editor.Layers.IndexOf(_editor.SelectedLayer) < _editor.Layers.Count - 1;
        LayerLimitNotice.Visibility = _editor.CanAddLayer ? Visibility.Collapsed : Visibility.Visible;
        SaveButton.IsEnabled = !_editor.HasIssues;
        LayerSummaryText.Text = _editor.LayerSummaryText;
        ValidationSummaryText.Text = _editor.IssueSummaryText;
        IssuesList.ItemsSource = _editor.Issues;
        IssuesList.Visibility = _editor.HasIssues ? Visibility.Visible : Visibility.Collapsed;
        NoIssuesText.Visibility = _editor.HasIssues ? Visibility.Collapsed : Visibility.Visible;
        BuiltInText.Text = _editor.IsBuiltIn
            ? PresetEditorText.Get("PresetEditorBuiltInReadOnly",
                "Built-in presets are read-only. Save creates a user copy.")
            : string.Empty;
        BuiltInText.Visibility = _editor.IsBuiltIn ? Visibility.Visible : Visibility.Collapsed;

        var name = string.IsNullOrWhiteSpace(_editor.Draft.Name)
            ? PresetEditorText.Get("PresetEditorNewName", "New preset")
            : _editor.Draft.Name;
        HeaderNameText.Text = name;
        HeaderMetaText.Text = $"{_editor.Draft.Id} · {LocalizedKindLabel(_editor.Draft.Kind)}";
        ModulePickerText.Text = _editor.SelectedLayer?.ModuleName ??
            PresetEditorText.Get("PresetEditorNoModule", "No module");
        ModulePickerHint.Text = _editor.SelectedLayer is null
            ? PresetEditorText.Get("PresetEditorNoModule", "No module")
            : string.Empty;
        UpdateSelectionPanels();
        UpdateStatusText();
    }

    private void UpdateSelectionPanels()
    {
        var hasLayer = _editor.SelectedLayer is not null;
        ModulePickerButton.IsEnabled = hasLayer;
        BlendPicker.IsEnabled = hasLayer;
        OpacitySlider.IsEnabled = hasLayer;
        OpacityBox.IsEnabled = hasLayer;
        SpeedSlider.IsEnabled = hasLayer;
        SpeedBox.IsEnabled = hasLayer;
        PaletteAddButton.IsEnabled = hasLayer &&
            _editor.SelectedLayer!.Layer.Palette.Count < 10;
    }

    private void UpdateStatusText()
    {
        var pathLabel = _editor.Path is { } path
            ? PresetEditorText.Format("PresetEditorPath", "Path: {0}", path)
            : _editor.IsBuiltIn
                ? PresetEditorText.Get("PresetEditorStatusBuiltIn", "Built-in preset")
                : PresetEditorText.Get("PresetEditorStatusNew", "New preset");
        var saved = _editor.Path is null
            ? string.Empty
            : _editor.IsDirty
                ? PresetEditorText.Get("PresetEditorUnsaved", "Unsaved changes")
                : PresetEditorText.Get("PresetEditorSaved", "Saved");
        var note = string.IsNullOrWhiteSpace(_statusNote) ? string.Empty : _statusNote + "  ";
        var parts = string.IsNullOrEmpty(saved)
            ? new[] { pathLabel, _editor.IssueSummaryText }
            : [pathLabel, saved, _editor.IssueSummaryText];
        StatusText.Text = $"{note}{string.Join("    ", parts)}";
    }

    private void SetStatus(string note)
    {
        _statusNote = note;
        UpdateStatusText();
    }

    private void BuildKindPicker()
    {
        var selected = _editor.Draft.Kind;
        var options = Enum.GetValues<StudioPresetKind>()
            .Select(kind => new KeyValuePair<StudioPresetKind, string>(kind, LocalizedKindLabel(kind)))
            .ToList();
        _syncingUi = true;
        try
        {
            KindPicker.ItemsSource = null;
            KindPicker.DisplayMemberPath = "Value";
            KindPicker.SelectedValuePath = "Key";
            KindPicker.ItemsSource = options;
            KindPicker.SelectedIndex = options.FindIndex(option => option.Key == selected);
        }
        finally
        {
            _syncingUi = false;
        }
    }

    private void BuildBlendPicker()
    {
        var selected = _editor.SelectedLayer?.Layer.Blend ?? StudioBlendMode.Normal;
        var options = Enum.GetValues<StudioBlendMode>()
            .Select(mode => new KeyValuePair<StudioBlendMode, string>(mode, LocalizedBlendLabel(mode)))
            .ToList();
        _syncingUi = true;
        try
        {
            BlendPicker.ItemsSource = null;
            BlendPicker.DisplayMemberPath = "Value";
            BlendPicker.SelectedValuePath = "Key";
            BlendPicker.ItemsSource = options;
            BlendPicker.SelectedIndex = options.FindIndex(option => option.Key == selected);
        }
        finally
        {
            _syncingUi = false;
        }
    }

    private static string LocalizedKindLabel(StudioPresetKind kind) => kind switch
    {
        StudioPresetKind.Music => PresetEditorText.Get("LightingStudioPresetCategoryMusic", "Music"),
        StudioPresetKind.Reactive => PresetEditorText.Get("LightingStudioPresetCategoryReactive", "Reactive"),
        StudioPresetKind.Screen => PresetEditorText.Get("PresetEditorKindScreen", "Screen"),
        StudioPresetKind.System => PresetEditorText.Get("PresetEditorKindSystem", "System"),
        _ => PresetEditorText.Get("PresetEditorKindAmbient", "Ambient"),
    };

    private static string LocalizedBlendLabel(StudioBlendMode mode) => mode switch
    {
        StudioBlendMode.Add => PresetEditorText.Get("LightingStudioBlendAdd", "Add"),
        StudioBlendMode.Multiply => PresetEditorText.Get("LightingStudioBlendMultiply", "Multiply"),
        StudioBlendMode.Screen => PresetEditorText.Get("LightingStudioBlendScreen", "Screen"),
        StudioBlendMode.Max => PresetEditorText.Get("LightingStudioBlendMax", "Max"),
        _ => PresetEditorText.Get("LightingStudioBlendNormal", "Normal"),
    };

    private void NewClick(object sender, RoutedEventArgs e) => _ = NewDocumentAsync();

    private void OpenClick(object sender, RoutedEventArgs e) => _ = OpenDocumentAsync();

    private void SaveClick(object sender, RoutedEventArgs e) => _ = SaveAsync();

    private void SaveAsClick(object sender, RoutedEventArgs e) => _ = SaveAsAsync();

    private void ValidateClick(object sender, RoutedEventArgs e) => _editor.Validate(renderSmoke: true);

    private void UndoClick(object sender, RoutedEventArgs e) => _editor.Undo();

    private void RedoClick(object sender, RoutedEventArgs e) => _editor.Redo();

    private void PlayClick(object sender, RoutedEventArgs e) => TogglePlayback();

    private void NameChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncingUi) return;
        _editor.SetName(NameBox.Text);
    }

    private void IdChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncingUi) return;
        _editor.SetId(IdBox.Text);
    }

    private void CategoryChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncingUi) return;
        _editor.SetCategory(CategoryBox.Text);
    }

    private void NoteChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncingUi) return;
        _editor.SetNote(string.IsNullOrWhiteSpace(NoteBox.Text) ? null : NoteBox.Text);
    }

    private void KindChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingUi) return;
        if (KindPicker.SelectedValue is not StudioPresetKind kind) return;
        _editor.SetKind(kind);
        UpdateSimulationUi();
    }

    private void OnClosed(object? sender, WindowEventArgs args)
    {
        _loaded = false;
        _previewTimer.Stop();
        _autosaveTimer.Stop();
        _editor.Changed -= EditorChanged;
        _editor.DocumentReplaced -= EditorDocumentReplaced;
        if (_studio is not null)
        {
            _studio.Devices.CollectionChanged -= OnStudioDevicesChanged;
            _studio.DeviceVisuals.CollectionChanged -= OnStudioVisualsChanged;
        }
        AppWindow.Closing -= OnPresetEditorClosing;
        AppWindow.Changed -= OnPreviewWindowChanged;
    }

    private bool CanRenderPreview => _loaded && AppWindow.IsVisible && !IsIconic(Handle);

    private void OnPreviewWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (!args.DidVisibilityChange && !args.DidPresenterChange && !args.DidSizeChange) return;
        UpdatePreviewTimer();
        if (CanRenderPreview) RenderPreviewNow();
    }

    private void UpdatePreviewTimer()
    {
        _lastPreviewTimestamp = Stopwatch.GetTimestamp();
        if (CanRenderPreview && _playing) _previewTimer.Start();
        else _previewTimer.Stop();
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint windowHandle, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint windowHandle);
}
