using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Lighting;
using OpenSynapse.Core.Storage;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Lighting;
using Windows.UI;

namespace OpenSynapse.App.ViewModels;

public sealed record StudioEffectChoice(StudioEffectKind Kind, string Label);

public sealed record StudioBlendChoice(StudioBlendMode Mode, string Label);

public sealed record StudioColorChoice(string Label, string Hex, SolidColorBrush Brush);

/// <summary>
/// State and commands for the lighting studio page. Preview rendering is pulled
/// by the page timer; the hardware frame source renders the same layer list on
/// its own cadence with its own playback clocks.
/// </summary>
public sealed partial class LightingStudioViewModel : INotifyPropertyChanged, IDisposable
{
    private static readonly (string Label, string Hex)[] Palette =
    [
        ("LightingStudioColorRazerGreen", "#44D62C"),
        ("LightingStudioColorCyan", "#00D4D4"),
        ("LightingStudioColorBlue", "#0066FF"),
        ("LightingStudioColorPurple", "#8A2BE2"),
        ("LightingStudioColorRed", "#FF2B2B"),
        ("LightingStudioColorOrange", "#FF7A00"),
        ("LightingStudioColorYellow", "#FFD400"),
        ("LightingStudioColorWhite", "#FFFFFF"),
    ];

    private readonly IBladeLightingController? _lighting;
    private readonly Func<IReadOnlyList<DeviceDescriptor>> _devices;
    private readonly Func<IReadOnlyList<StudioDeviceOption>>? _matrixDevices;
    private readonly StudioHidOutputRouter? _hidRouter;
    private readonly StudioArgbOutputRouter? _argbRouter;
    private readonly StudioHeadsetOutputRouter? _headsetRouter;
    private readonly StudioInputSampler? _inputSampler;
    private readonly StudioWorldRenderer _world;
    private readonly StudioWorldRenderer _outputWorld;
    private readonly Func<IReadOnlyCollection<string>, Task>? _restoreAfterExternal;
    private readonly DispatcherQueue? _dispatcher;
    private readonly Dictionary<StudioEffectParams, string?> _loadedMedia =
        new(ReferenceEqualityComparer.Instance);
    private CancellationTokenSource? _outputCancellation;
    private Task? _outputTask;
    private readonly SemaphoreSlim _outputUpdateGate = new(1, 1);
    private readonly Stopwatch _outputClock = new();
    private StudioProject _project;
    private StudioHistory? _history;
    private StudioLayerItem? _selectedLayer;
    private bool _syncing;
    private StudioDeviceOption? _selectedDevice;
    private readonly StudioDeviceVisualLoader _visualLoader = new();
    private StudioBladeFrameSourceV2? _source;
    private StudioDeviceOption? _appliedBlade;
    private StudioDeviceVisual? _deviceVisual;
    private CancellationTokenSource? _visualCancellation;
    private CancellationTokenSource? _visualsCancellation;
    private string _statusText = AppStrings.Text("LightingStudioStatusHint");
    private bool _isBusy;
    private bool _isApplied;
    private bool _isPlaying;
    private bool _worldDirty = true;
    private bool _worldInitialized;
    private bool _outputWorldDirty = true;
    private int _disposed;

    internal void RefreshLocalization()
    {
        LoadLocalizedChoices();
        foreach (var card in SceneCards) card.RefreshLocalization();
        OnPropertyChanged(nameof(EditButtonText));
        OnPropertyChanged(nameof(EffectChoices));
        OnPropertyChanged(nameof(BlendChoices));
        OnPropertyChanged(nameof(ColorChoices));
        OnPropertyChanged(nameof(SelectedEffectChoice));
        OnPropertyChanged(nameof(SelectedBlendChoice));
        foreach (var row in LayerRows)
        {
            row.Layer.NotifyEffectChanged();
        }
        RefreshRegionChoices();
        StatusText = AppStrings.Text("LightingStudioStatusHint");
    }

    private void LoadLocalizedChoices()
    {
        EffectChoices =
        [
            new(StudioEffectKind.Solid, AppStrings.Text("LightingStudioEffectSolid")),
            new(StudioEffectKind.Breathing, AppStrings.Text("LightingStudioEffectBreathing")),
            new(StudioEffectKind.Spectrum, AppStrings.Text("LightingStudioEffectSpectrum")),
            new(StudioEffectKind.Wave, AppStrings.Text("LightingStudioEffectWave")),
            new(StudioEffectKind.Gradient, AppStrings.Text("LightingStudioEffectGradient")),
            new(StudioEffectKind.Fire, AppStrings.Text("LightingStudioEffectFire")),
            new(StudioEffectKind.Reactive, AppStrings.Text("LightingStudioEffectReactive")),
            new(StudioEffectKind.Ripple, AppStrings.Text("LightingStudioEffectRipple")),
            new(StudioEffectKind.Starlight, AppStrings.Text("LightingStudioEffectStarlight")),
            new(StudioEffectKind.Ambient, AppStrings.Text("LightingStudioEffectAmbient")),
            new(StudioEffectKind.Audio, AppStrings.Text("LightingStudioEffectAudio")),
            new(StudioEffectKind.Wheel, AppStrings.Text("LightingStudioEffectWheel")),
            new(StudioEffectKind.ChromaGenerate, AppStrings.Text("LightingStudioEffectGenerate")),
            new(StudioEffectKind.ChromaFile, AppStrings.Text("LightingStudioEffectChromaFile")),
            new(StudioEffectKind.Tidal, AppStrings.Text("LightingStudioEffectTidal")),
            new(StudioEffectKind.Radiate, AppStrings.Text("LightingStudioEffectRadiate")),
            new(StudioEffectKind.Preset, AppStrings.Text("LightingStudioEffectPreset")),
        ];
        BlendChoices =
        [
            new(StudioBlendMode.Normal, AppStrings.Text("LightingStudioBlendNormal")),
            new(StudioBlendMode.Add, AppStrings.Text("LightingStudioBlendAdd")),
            new(StudioBlendMode.Multiply, AppStrings.Text("LightingStudioBlendMultiply")),
            new(StudioBlendMode.Screen, AppStrings.Text("LightingStudioBlendScreen")),
        ];
        ColorChoices = Palette
            .Select(item => new StudioColorChoice(
                AppStrings.Text(item.Label),
                item.Hex,
                new SolidColorBrush(ParseColor(item.Hex))))
            .ToArray();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? DocumentChanged;

    /// <summary>Layer list mirror; kept in sync with the selected profile.</summary>
    public ObservableCollection<StudioLayerItem> Layers { get; } = new();

    /// <summary>Active profile, used by the canvas for device placement.</summary>
    public StudioProfile? Profile => _project.SelectedProfile;

    public ObservableCollection<StudioDeviceOption> Devices { get; } = new();

    public IReadOnlyList<StudioEffectChoice> EffectChoices { get; private set; } = [];

    public IReadOnlyList<StudioBlendChoice> BlendChoices { get; private set; } = [];

    public IReadOnlyList<StudioColorChoice> ColorChoices { get; private set; } = [];

    public string DocumentName
    {
        get => _project.Name;
        set
        {
            if (_project.Name == value) return;
            _project.Name = value;
            OnPropertyChanged();
        }
    }

    public StudioLayerItem? SelectedLayer
    {
        get => _selectedLayer;
        set
        {
            if (ReferenceEquals(_selectedLayer, value)) return;
            _selectedLayer = value;
            // Keep the region target with the layer; clearing it made a layer with
            // regions show "whole layer" after switching away and back.
            _selectedRegion = value?.Regions.FirstOrDefault();
            RefreshRegionChoices();
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedEffectChoice));
            _ = EnsureMediaSamplerAsync();
            OnPropertyChanged(nameof(SelectedBlendChoice));
            OnPropertyChanged(nameof(SelectedParams));
            OnPropertyChanged(nameof(SpeedMinimum));
            OnPropertyChanged(nameof(SpeedMaximum));
            OnPropertyChanged(nameof(CanRemoveLayer));
        }
    }

    /// <summary>Convenience accessor for the inspector; a selected region wins over its layer.</summary>
    public StudioEffectParams? SelectedParams => _selectedRegion?.Params ?? _selectedLayer?.Params;

    public double SpeedMinimum => SpeedRange?.Minimum ?? 0.1;

    public double SpeedMaximum => SpeedRange?.Maximum ?? 5;

    private StudioParameterRange? SpeedRange =>
        SelectedParams is { } parameters &&
        StudioEffectCatalog.Get(parameters.Effect).Ranges.TryGetValue(StudioParameterField.Speed, out var range)
            ? range
            : null;

    public StudioDeviceOption? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            if (ReferenceEquals(_selectedDevice, value)) return;
            _selectedDevice = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanApply));
            OnPropertyChanged(nameof(PreviewRows));
            OnPropertyChanged(nameof(PreviewColumns));
            OnPropertyChanged(nameof(NoPixelOutputVisibility));
            NotifyDocumentChanged();
            if (value?.HasSameIdentity(_selectedDevice) != true) _ = LoadVisualAsync(value);
        }
    }

    /// <summary>Canvas geometry for the selected device, or null while it loads.</summary>
    public StudioDeviceVisual? DeviceVisual => _deviceVisual;

    public StudioEffectChoice? SelectedEffectChoice
    {
        get => EffectChoices.FirstOrDefault(choice =>
            choice.Kind == (SelectedParams?.Effect ?? StudioEffectKind.Solid));
        set
        {
            if (SelectedParams is not { } parameters || value is null || parameters.Effect == value.Kind) return;
            var previous = parameters.Effect;
            parameters.Effect = value.Kind;
            _selectedLayer?.NotifyEffectChanged();
            ApplyEffectDefaults(parameters, previous);
            _selectedLayer?.NotifyColorChanged();

            OnPropertyChanged();
            OnPropertyChanged(nameof(SpeedMinimum));
            OnPropertyChanged(nameof(SpeedMaximum));
            NotifyDocumentChanged();
            if (IsApplied) TriggerAppliedEffect();
            _ = EnsureMediaSamplerAsync();
        }
    }

    public StudioBlendChoice? SelectedBlendChoice
    {
        get => BlendChoices.FirstOrDefault(choice =>
            choice.Mode == (_selectedLayer?.Blend ?? StudioBlendMode.Normal));
        set
        {
            if (_selectedLayer is null || value is null || _selectedLayer.Blend == value.Mode) return;
            _selectedLayer.Blend = value.Mode;
            OnPropertyChanged();
            NotifyDocumentChanged();
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set
        {
            if (_statusText == value) return;
            _statusText = value;
            OnPropertyChanged();
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (_isBusy == value) return;
            _isBusy = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanApply));
        }
    }

    public bool IsApplied
    {
        get => _isApplied;
        private set
        {
            if (_isApplied == value) return;
            _isApplied = value;
            OnPropertyChanged();
        }
    }

    public bool IsPlaying
    {
        get => _isPlaying;
        private set
        {
            if (_isPlaying == value) return;
            _isPlaying = value;
            OnPropertyChanged();
        }
    }

    public bool CanRemoveLayer => _selectedLayer is not null;

    public int PreviewRows => SelectedPreviewSize().Rows;

    public int PreviewColumns => SelectedPreviewSize().Columns;

    private (int Rows, int Columns) SelectedPreviewSize() =>
        SelectedDevice is { } device
            ? FrameSize(device)
            : (BladeLightingLayout.LogicalRows, BladeLightingLayout.LogicalColumns);

    public StudioRgb[] RenderPreview(TimeSpan elapsed)
    {
        EnsureWorld();
        if (SelectedDevice is { } excluded && !ScopeIncludes(PendingScope, excluded))
            return new StudioRgb[PreviewRows * PreviewColumns];
        var now = Math.Max(0, elapsed.TotalSeconds);
        _previewSeconds = now;
        if (SelectedDevice is { } selected && UsesWorld(selected.EffectiveInstanceId) &&
            _world.RenderDeviceFrames(elapsed).TryGetValue(
                selected.EffectiveInstanceId ?? string.Empty, out var worldFrame))
        {
            return worldFrame.Frame;
        }

        var layers = BuildLayers();
        var rows = PreviewRows;
        var columns = PreviewColumns;
        if (layers.Length == 0) return new StudioRgb[rows * columns];

        var timed = new (StudioLayerRender Layer, double Seconds)[layers.Length];
        for (var index = 0; index < layers.Length; index++)
        {
            var layer = layers[index];
            var state = GetPreviewClock(SelectedDevice?.EffectiveInstanceId, layer.Id)
                .Sample(now, layer.Params, rows, columns);
            timed[index] = (layer with { Opacity = state.Running ? layer.Opacity : 0 }, state.LocalSeconds);
        }

        var input = _inputSampler?.Sample(rows, columns) ?? default;
        if (SelectedDevice is { } device)
            input = input with { Keys = PreviewKeysFor(device, rows, columns).Snapshot() };
        return StudioCompositor.RenderTimedLayers(timed, rows, columns, input);
    }

    public void AddLayer()
    {
        var profile = _project.SelectedProfile;
        if (profile is null) return;
        var index = Layers.Count;
        var color = StudioRgb.FromHex(Palette[index % Palette.Length].Hex);
        var layer = StudioLayerItem.CreateLayer(
            AppStrings.FormatText("LightingStudioLayerName", index + 1),
            StudioEffectKind.Solid);
        layer.Params.Primary = color;
        profile.Items.Insert(0, layer);
        _history?.Capture();
        SyncLayersFromProfile();
        RebuildLayerRows();
        SelectedLayer = layer;
        SelectedLayerRow = LayerRows.FirstOrDefault(row => row.Layer == layer);
        StatusText = AppStrings.FormatText("LightingStudioStatusLayerAdded", layer.Title);
    }

    public void RemoveLayer()
    {
        if (_selectedLayer is null || _project.SelectedProfile is not { } profile) return;
        var next = profile.RemoveItem(_selectedLayer);
        SyncLayersFromProfile();
        // SyncLayersFromProfile suppresses OnLayersChanged, so the removal needs its own undo point.
        _history?.Capture();
        RebuildLayerRows();
        SelectedLayer = next;
        SelectedLayerRow = next is null ? null : LayerRows.FirstOrDefault(row => row.Layer == next);
        StatusText = AppStrings.Text("LightingStudioStatusLayerRemoved");
        NotifyDocumentChanged();
    }

    /// <summary>Moves the selected layer toward the top of the stack.</summary>
    public void MoveLayerUp() => MoveSelected(-1);

    /// <summary>Moves the selected layer toward the bottom of the stack.</summary>
    public void MoveLayerDown() => MoveSelected(1);

    /// <summary>Drops regions so the layer covers every LED of the device.</summary>
    public void ClearRegions()
    {
        if (_selectedLayer is null) return;
        _selectedLayer.Regions.Clear();
        _selectedRegion = null;
        RefreshRegionChoices();
        StatusText = AppStrings.Text("LightingStudioStatusRegionCleared");
        NotifyDocumentChanged();
    }

    /// <summary>Starts one-shot OnPress/OnSelectedKeys effects for the preview clock.</summary>
    public void TriggerPreviewEffect(double nowSeconds)
    {
        if (SelectedParams is not { } parameters || _selectedLayer is not { } layer) return;
        if (parameters.PlaybackStart is not (StudioPlaybackStart.OnPress or StudioPlaybackStart.OnSelectedKeys)) return;
        foreach (var device in Devices)
            GetPreviewClock(device.EffectiveInstanceId, layer.Id).Trigger(Math.Max(0, nowSeconds));
    }

    private void TriggerAppliedEffect()
    {
        if (SelectedParams is not { } parameters ||
            parameters.PlaybackStart is not (StudioPlaybackStart.OnPress or StudioPlaybackStart.OnSelectedKeys))
        {
            return;
        }

        _source?.TriggerPlayback(new StudioTriggerInput(0, 0), _inputClock.Elapsed.TotalSeconds);
        if (_hidRouter is null) return;
        foreach (var target in _hidRouter.GetTargets())
        {
            _hidRouter.TriggerPlayback(
                target.InstanceId,
                new StudioTriggerInput(0, 0),
                _outputClock.Elapsed.TotalSeconds);
        }
    }

    public void SetPrimaryColor(string hex)
    {
        if (SelectedParams is not { } parameters) return;
        parameters.Primary = StudioRgb.FromHex(hex);
        _selectedLayer?.NotifyColorChanged();
        NotifyDocumentChanged();
    }

    public void SetSecondaryColor(string hex)
    {
        if (SelectedParams is not { } parameters) return;
        parameters.Secondary = StudioRgb.FromHex(hex);
        _selectedLayer?.NotifyColorChanged();
        NotifyDocumentChanged();
    }

    /// <summary>Called by the inspector for bindings that do not raise layer notifications.</summary>
    // Regions own their params after creation; later layer edits stay on the layer.
    public void NotifyEdited(bool captureHistory = true)
    {
        _selectedLayer?.NotifyColorChanged();
        if (captureHistory) _history?.Capture();
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        NotifyDocumentChanged();
        _ = EnsureMediaSamplerAsync();
    }

    /// <summary>Commits the current edit as one undo step after the gesture settles.</summary>
    public void CommitHistory()
    {
        _history?.Capture();
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    public async Task ApplyAsync()
    {
        await _outputLifecycleGate.WaitAsync();
        try
        {
            _suspendedOutputScope = null;
            await ApplyOutputCoreAsync(null);
        }
        finally { _outputLifecycleGate.Release(); }
    }

    private async Task ApplyOutputCoreAsync(StudioOutputScope? resumeScope)
    {
        if (_isBusy || (_lighting is null && _hidRouter is null && _argbRouter is null &&
            _headsetRouter is null && _chromaRouter is null))
        {
            return;
        }
        var applied = new List<string>();
        var startOutputPump = _outputTask is { IsCompleted: false };
        var wasApplied = IsApplied;
        IsBusy = true;
        await _outputUpdateGate.WaitAsync();
        try
        {
            var scope = (resumeScope ?? PendingScope).Clone();
            var blade = Devices.FirstOrDefault(device => device.IsOutputAvailable && ScopeIncludes(scope, device) &&
                (device.IsBlade || (device.Descriptor is not null &&
                    DeviceProtocolFamilies.UsesBladeMatrix(device.Descriptor.ProtocolFamily))));
            await EnsureInputAsync();
            if (_appliedBlade is not null && !_appliedBlade.HasSameIdentity(blade))
                await TryReleaseBladeAsync();
            else if (_bladeRestoreId is not null) await TryReleaseBladeAsync();
            await SetRouterTargetsAsync(scope);
            _appliedScope = scope;
            _outputWorldDirty = true;
            EnsureWorld(output: true);
            if (!wasApplied)
            {
                _outputWorld.RestartOutputClock();
                _inputClock.Restart();
            }

            if (_lighting is not null && blade is not null && _appliedBlade is null)
            {
                // The live manifest is the authority for the sparse map. A 2024
                // Blade 16 is 7 x 15 logical -> 6 x 16 device, not the 2025
                // 7 x 16 -> 6 x 17 layout.
                var layout = blade.BladeFrameMode == StudioBladeFrameMode.Direct
                    ? null
                    : await _visualLoader
                        .LoadLayoutAsync(blade.EffectiveProductId, CancellationToken.None)
                        .ConfigureAwait(true);
                var (rows, columns) = layout is { MatrixRows: > 0, MatrixColumns: > 0 }
                    ? (layout.MatrixRows, layout.MatrixColumns)
                    : FrameSize(blade);
                _source = new StudioBladeFrameSourceV2(
                    blade.BladeFrameMode,
                    rows,
                    columns,
                    (inputRows, inputColumns) =>
                        _inputSampler?.Sample(inputRows, inputColumns) ?? default,
                    layout);
                if (blade.EffectiveInstanceId is { } bladeInstanceId)
                {
                    _source.AttachWorld(_outputWorld, bladeInstanceId);
                }
                _appliedBlade = blade;
                _source.Update(BuildLayersFor(blade, output: true));
                RestoreBladeHeldKeys(blade, rows, columns);
                await _lighting.ApplyExternalAsync(
                    _devices().Where(device => StudioLedSelection.SameInstance(
                        device.Id, blade.EffectiveInstanceId)).ToArray(),
                    _source,
                    restorePersistentEffect: _restoreAfterExternal is null);
                _source.TriggerPlayback(new StudioTriggerInput(0, 0), _inputClock.Elapsed.TotalSeconds);
            }
            if (_appliedBlade is { } retainedBlade)
            {
                RefreshBladeWorldSource();
                applied.Add(retainedBlade.Name);
            }

            if (_hidRouter is not null)
            {
                var targets = _hidRouter.GetTargets();
                if (targets.Count > 0)
                {
                    RefreshHidSnapshots(targets);
                    StartOutputPump();
                    foreach (var target in wasApplied ? [] : targets)
                    {
                        _hidRouter.TriggerPlayback(target.InstanceId, new StudioTriggerInput(0, 0), 0);
                    }
                    startOutputPump = true;
                    applied.AddRange(targets.Select(target => target.Definition.DisplayName));
                }
            }

            await ApplyArgbAsync(applied, startOutputPump);
            ApplyChroma(applied, ref startOutputPump);
            if (await ApplyHeadsetAsync(applied) && !startOutputPump)
            {
                StartOutputPump();
                startOutputPump = true;
            }

            IsApplied = applied.Count > 0;
            _appliedNames.Clear();
            foreach (var choice in GetOutputChoices().Where(choice => choice.IsSelected))
                _appliedNames[choice.InstanceId] = choice.Name;
            NotifyOutputScope();
            if (IsApplied)
            {
                SaveAppliedProject();
            }
            else ClearAppliedProject();
            StatusText = applied.Count == 0
                ? AppStrings.Text("LightingStudioStatusNoDevice")
                : AppStrings.FormatText("LightingStudioStatusApplied", string.Join(", ", applied));
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            IsApplied = wasApplied || _appliedBlade is not null ||
                _hidRouter?.OwnedInstanceIds.Count > 0 || _argbRouter?.OwnedInstanceIds.Count > 0 ||
                _headsetRouter?.OwnedInstanceIds.Count > 0 || _chromaRouter?.OwnedInstanceIds.Count > 0;
            StatusText = AppStrings.FormatText("LightingStudioStatusApplyFailed", exception.Message);
        }
        finally
        {
            _outputUpdateGate.Release();
            IsBusy = false;
            NotifyOutputScope();
        }
    }

    public async Task SaveAsync()
    {
        try
        {
            var path = StudioPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, StudioProjectCodec.Serialize(_project));
            StatusText = AppStrings.FormatText("LightingStudioStatusSaved", path);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            StatusText = AppStrings.FormatText("LightingStudioStatusSaveFailed", exception.Message);
        }
    }

    public bool HasSavedProject => File.Exists(StudioPath());

    public async Task LoadAsync()
    {
        var path = StudioPath();
        if (!File.Exists(path))
        {
            StatusText = AppStrings.Text("LightingStudioStatusNoSave");
            return;
        }

        try
        {
            var json = await File.ReadAllTextAsync(path);
            ApplyProject(StudioProjectCodec.Deserialize(json));
            StatusText = AppStrings.FormatText("LightingStudioStatusLoaded", path);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            StatusText = AppStrings.FormatText("LightingStudioStatusLoadFailed", exception.Message);
        }
    }

    /// <summary>Imports a Chroma file and reconciles its devices against this machine.</summary>
    public async Task<StudioImportResult?> ImportChromaAsync(string path)
    {
        try
        {
            var connected = ConnectedMatches();
            var result = await Task.Run(() => StudioChromaFileService.Import(path, connected));
            ApplyProject(result.Project);
            StatusText = result.Issues.Count == 0
                ? AppStrings.FormatText("LightingStudioStatusImported", path)
                : AppStrings.FormatText("LightingStudioStatusImportedIssues", path, result.Issues.Count);
            return result;
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            StatusText = AppStrings.FormatText("LightingStudioStatusLoadFailed", exception.Message);
            return null;
        }
    }

    public void ExportChroma(string path, IReadOnlyCollection<string>? profileIds = null)
    {
        EnsurePlacementIdentity();
        try
        {
            StudioChromaFileService.Export(_project, path, profileIds);
            StatusText = AppStrings.FormatText("LightingStudioStatusExported", path);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            StatusText = AppStrings.FormatText("LightingStudioStatusSaveFailed", exception.Message);
        }
    }

    public void NewDocument()
    {
        var project = StudioProject.CreateDefault();
        LocalizeDefaultProject(project);
        ApplyProject(project);
        StatusText = AppStrings.Text("LightingStudioStatusNew");
    }

    private void ApplyProject(StudioProject project, bool resetHistory = true)
    {
        ArgumentNullException.ThrowIfNull(project);
        DetachProfile(_project.SelectedProfile);
        _project = project;
        _previewKeys.Clear();
        _selectedRegion = null;
        AttachProfile(_project.SelectedProfile);
        SyncLayersFromProfile();
        RebuildLayerRows();
        SelectedLayer = Layers.FirstOrDefault();
        SelectedLayerRow = LayerRows.FirstOrDefault();
        RefreshRegionChoices();
        if (resetHistory)
        {
            _history = new StudioHistory(_project);
            OnPropertyChanged(nameof(CanUndo));
            OnPropertyChanged(nameof(CanRedo));
        }

        OnPropertyChanged(nameof(DocumentName));
        OnPropertyChanged(nameof(Profile));
        OnPropertyChanged(nameof(Profiles));
        OnPropertyChanged(nameof(SelectedProfile));
        NotifyDocumentChanged();
        _ = EnsureMediaSamplerAsync();
    }

    private void AttachProfile(StudioProfile? profile)
    {
        if (profile is null) return;
        foreach (var layer in profile.EnumerateLayers()) layer.PropertyChanged += OnLayerPropertyChanged;
    }

    private void DetachProfile(StudioProfile? profile)
    {
        if (profile is null) return;
        foreach (var layer in profile.EnumerateLayers()) layer.PropertyChanged -= OnLayerPropertyChanged;
    }

    private void SyncLayersFromProfile()
    {
        _syncing = true;
        try
        {
            Layers.Clear();
            if (_project.SelectedProfile is { } profile)
            {
                foreach (var item in profile.Items) Layers.Add(item);
            }
        }
        finally
        {
            _syncing = false;
        }
    }

    private void SyncProfileFromLayers()
    {
        if (_project.SelectedProfile is not { } profile) return;
        profile.Items.Clear();
        foreach (var item in Layers) profile.Items.Add(item);
    }

    private static void LocalizeDefaultProject(StudioProject project)
    {
        project.Name = AppStrings.Text("LightingStudioUntitled");
        if (project.SelectedProfile?.Items.FirstOrDefault() is { } background)
        {
            background.Title = AppStrings.Text("LightingStudioBackgroundLayer");
        }
    }

    private static string StudioPath() =>
        AppDataPaths.Combine("lighting-studio", "studio.json");

    private static Color ParseColor(string hex)
    {
        var color = StudioRgb.FromHex(hex);
        return Color.FromArgb(255, color.R, color.G, color.B);
    }

    private void OnLayersChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (_syncing) return;
        if (args.OldItems is not null)
        {
            foreach (StudioLayerItem layer in args.OldItems) layer.PropertyChanged -= OnLayerPropertyChanged;
        }

        if (args.NewItems is not null)
        {
            foreach (StudioLayerItem layer in args.NewItems) layer.PropertyChanged += OnLayerPropertyChanged;
        }

        _history?.Capture();
        OnPropertyChanged(nameof(CanRemoveLayer));
        NotifyDocumentChanged();
    }

    private void OnLayerPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        // The color and effect forwarders are raised by the view-model's own
        // edit path, which already calls NotifyDocumentChanged once.
        if (args.PropertyName is nameof(StudioLayerItem.PrimaryColor)
            or nameof(StudioLayerItem.SecondaryColor)
            or nameof(StudioLayerItem.Effect))
        {
            return;
        }

        NotifyDocumentChanged();
    }

    private void NotifyDocumentChanged()
    {
        InvalidateWorld();
        EnsureWorld();
        _layerCache.Clear();
        RefreshBladeWorldSource();
        if (IsApplied && _hidRouter is not null) RefreshHidSnapshots();
        if (IsApplied && _argbRouter is not null) RefreshArgbSnapshots(_argbRouter.GetTargets());
        if (IsApplied && _headsetRouter is not null) _headsetRouter.Update(BuildHeadsetLayers);
        if (IsApplied && _chromaRouter is not null) RefreshChromaSnapshots(_chromaRouter.GetTargets());
        _ = EnsureInputAsync();
        NotifyOutputScope();
        DocumentChanged?.Invoke(this, EventArgs.Empty);
    }

    private static bool IsExpected(Exception exception) =>
        exception is IOException or InvalidOperationException or JsonException or
            UnauthorizedAccessException or System.ComponentModel.Win32Exception or ArgumentException or
            NotSupportedException;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
