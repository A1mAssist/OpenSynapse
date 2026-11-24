using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using OpenSynapse.Core.Lighting;

namespace OpenSynapse.App.ViewModels;

internal static class PresetEditorText
{
    public static string Get(string key, string fallback) => AppStrings.TryGet(key) ?? fallback;

    public static string Format(string key, string fallback, params object?[] args)
    {
        var text = Get(key, fallback);
        try
        {
            return string.Format(CultureInfo.CurrentCulture, text, args);
        }
        catch (FormatException)
        {
            return text;
        }
    }

    public static string Humanize(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;
        var text = new StringBuilder(value.Length + 4);
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (index > 0 && char.IsUpper(character) &&
                (char.IsLower(value[index - 1]) || char.IsDigit(value[index - 1])))
            {
                text.Append(' ');
            }

            text.Append(index == 0 ? char.ToUpperInvariant(character) : character);
        }

        return text.ToString();
    }

    public static string ModuleName(string moduleId, string fallback) =>
        string.IsNullOrWhiteSpace(moduleId)
            ? fallback
            : AppStrings.TryGet("LightingStudioModule_" + moduleId) ?? fallback;
}

internal static class PresetEditorIssueText
{
    private const string UnknownModuleSuffix = " is not a render module";

    public static string Format(StudioPresetValidationIssue issue)
    {
        var line = $"{Field(issue.Field)}: {Message(issue.Message)}";
        return issue.LayerIndex >= 0
            ? PresetEditorText.Format("PresetEditorIssueLayer", "Layer {0} · {1}",
                issue.LayerIndex + 1, line)
            : line;
    }

    private static string Field(string name) =>
        PresetEditorText.Get("PresetEditorField_" + name, PresetEditorText.Humanize(name));

    private static string Message(string message) => message switch
    {
        "is required" => PresetEditorText.Get("PresetEditorIssueRequired", message),
        "must not be empty" => PresetEditorText.Get("PresetEditorIssueEmpty", message),
        "must use lowercase letters, digits and '-'" =>
            PresetEditorText.Get("PresetEditorIssueIdInvalid", message),
        "must contain 1 to 4 layers" =>
            PresetEditorText.Get("PresetEditorIssueLayersCount", message),
        "must contain 1 to 10 color stops" =>
            PresetEditorText.Get("PresetEditorIssuePaletteCount", message),
        "stop positions must be 0 to 100" =>
            PresetEditorText.Get("PresetEditorIssuePaletteRange", message),
        "must be between 0 and 1" =>
            PresetEditorText.Get("PresetEditorIssueBetween01", message),
        "must be between 0.1 and 5" =>
            PresetEditorText.Get("PresetEditorIssueBetweenSpeed", message),
        "is not a supported blend mode" =>
            PresetEditorText.Get("PresetEditorIssueBlend", message),
        _ when message.StartsWith('\'') && message.EndsWith(UnknownModuleSuffix,
            StringComparison.Ordinal) =>
            PresetEditorText.Format("PresetEditorIssueUnknownModule",
                "'{0}' is not a render module",
                message[1..^UnknownModuleSuffix.Length].TrimEnd('\'')),
        _ => message,
    };
}

internal sealed class PresetEditorLayerItem : INotifyPropertyChanged
{
    public PresetEditorLayerItem(StudioPresetLayerDraft layer)
    {
        Layer = layer;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public StudioPresetLayerDraft Layer { get; }

    public string ModuleName
    {
        get
        {
            if (RenderModuleRegistry.TryGet(Layer.ModuleId, out var module))
                return PresetEditorText.ModuleName(Layer.ModuleId, module.Info.Name);
            return string.IsNullOrWhiteSpace(Layer.ModuleId)
                ? PresetEditorText.Get("PresetEditorNoModule", "No module")
                : Layer.ModuleId;
        }
    }

    public string BlendName => Layer.Blend switch
    {
        StudioBlendMode.Add => PresetEditorText.Get("LightingStudioBlendAdd", "Add"),
        StudioBlendMode.Multiply => PresetEditorText.Get("LightingStudioBlendMultiply", "Multiply"),
        StudioBlendMode.Screen => PresetEditorText.Get("LightingStudioBlendScreen", "Screen"),
        StudioBlendMode.Max => PresetEditorText.Get("LightingStudioBlendMax", "Max"),
        _ => PresetEditorText.Get("LightingStudioBlendNormal", "Normal"),
    };

    public string DetailText =>
        $"{BlendName} · {Math.Round(Layer.Opacity * 100):0}% · {Layer.Speed:0.##}×";

    public void Refresh()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ModuleName)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BlendName)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DetailText)));
    }
}

internal sealed class PresetEditorIssueItem
{
    public PresetEditorIssueItem(StudioPresetValidationIssue issue)
    {
        Issue = issue;
        Text = PresetEditorIssueText.Format(issue);
    }

    public StudioPresetValidationIssue Issue { get; }

    public string Text { get; }

    public override string ToString() => Text;
}

internal sealed class PresetEditorLibraryIssueItem
{
    public PresetEditorLibraryIssueItem(StudioPresetLibraryValidation result)
    {
        Result = result;
        Text = $"{result.Entry.Id} — {string.Join("; ", result.Issues.Select(PresetEditorIssueText.Format))}";
    }

    public StudioPresetLibraryValidation Result { get; }

    public string Text { get; }

    public override string ToString() => Text;
}

/// <summary>
/// Holds the mutable preset draft, the 1-4 layer wrappers, undo snapshots and
/// the validation state shared by the editor window and its preview.
/// </summary>
internal sealed class PresetEditorViewModel : INotifyPropertyChanged
{
    public const int MaxLayers = 4;
    private const int MaxUndo = 50;
    private const int CoalesceMilliseconds = 500;

    private readonly List<StudioPresetDraft> _undo = new();
    private readonly List<StudioPresetDraft> _redo = new();
    private PresetEditorLayerItem? _selectedLayer;
    private string? _cleanJson;
    private string? _coalesceKey;
    private long _coalesceStamp;
    private string? _path;

    public PresetEditorViewModel(StudioPresetConfig? initial, string? initialPath)
    {
        _path = initialPath;
        Draft = initial is null ? CreateNewDraft() : StudioPresetDraft.FromConfig(initial);
        _cleanJson = SafeJson();
        RebuildLayers();
        _selectedLayer = Layers.FirstOrDefault();
        Validate(false);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? Changed;

    public event EventHandler? DocumentReplaced;

    public event EventHandler? StateChanged;

    public StudioPresetDraft Draft { get; private set; }

    public ObservableCollection<PresetEditorLayerItem> Layers { get; } = new();

    public ObservableCollection<PresetEditorIssueItem> Issues { get; } = new();

    public PresetEditorLayerItem? SelectedLayer
    {
        get => _selectedLayer;
        set
        {
            if (ReferenceEquals(_selectedLayer, value)) return;
            _selectedLayer = value;
            Raise(nameof(SelectedLayer));
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public string? Path
    {
        get => _path;
        private set
        {
            if (_path == value) return;
            _path = value;
            Raise(nameof(Path));
            Raise(nameof(IsBuiltIn));
        }
    }

    public bool IsBuiltIn => StudioPresetLibrary.IsBuiltInId(Draft.Id);

    public bool IsDirty => _cleanJson is null || !string.Equals(SafeJson(), _cleanJson, StringComparison.Ordinal);

    public bool CanAddLayer => Draft.Layers.Count < MaxLayers;

    public bool CanRemoveLayer => Draft.Layers.Count > 1 && SelectedLayer is not null;

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public bool HasIssues => Issues.Count > 0;

    public string LayerLimitText => PresetEditorText.Get("PresetEditorLayerLimit", "A preset can contain at most 4 layers.");

    public string LayerSummaryText => PresetEditorText.Format(
        "PresetEditorLayerSummary", "{0}",
        $"{Draft.Layers.Count} / {MaxLayers}");

    public string IssueSummaryText => Issues.Count == 0
        ? PresetEditorText.Get("PresetEditorValidationOk", "No issues")
        : PresetEditorText.Format("PresetEditorValidationIssues", "{0} issues", Issues.Count);

    public void SelectLayer(PresetEditorLayerItem? item)
    {
        if (item is not null && !Layers.Contains(item)) return;
        SelectedLayer = item;
    }

    public void SetName(string? value)
    {
        value ??= string.Empty;
        if (Draft.Name == value) return;
        BeginEdit("name");
        Draft.Name = value;
        ApplyEdit();
    }

    public void SetId(string? value)
    {
        value ??= string.Empty;
        if (Draft.Id == value) return;
        BeginEdit("id");
        Draft.Id = value;
        ApplyEdit();
    }

    public void SetCategory(string? value)
    {
        value ??= string.Empty;
        if (Draft.Category == value) return;
        BeginEdit("category");
        Draft.Category = value;
        ApplyEdit();
    }

    public void SetNote(string? value)
    {
        if (Draft.Note == value) return;
        BeginEdit("note");
        Draft.Note = value;
        ApplyEdit();
    }

    public void SetKind(StudioPresetKind kind)
    {
        if (Draft.Kind == kind) return;
        BeginEdit();
        Draft.Kind = kind;
        ApplyEdit();
    }

    public void BeginEdit(string? key = null)
    {
        var now = Environment.TickCount64;
        if (key is not null && key == _coalesceKey &&
            now - _coalesceStamp < CoalesceMilliseconds)
        {
            return;
        }

        _undo.Add(Draft.Clone());
        if (_undo.Count > MaxUndo) _undo.RemoveAt(0);
        _redo.Clear();
        _coalesceKey = key;
        _coalesceStamp = now;
        Raise(nameof(CanUndo));
        Raise(nameof(CanRedo));
    }

    public void ApplyEdit(bool structural = false)
    {
        if (structural) RebuildLayers(keepSelection: true);
        foreach (var item in Layers) item.Refresh();
        Validate(false);
        RaiseDocumentState();
    }

    public void Undo()
    {
        if (_undo.Count == 0) return;
        var selected = SelectedLayer;
        var selectedIndex = selected is null ? 0 : Math.Max(0, Layers.IndexOf(selected));
        _redo.Add(Draft.Clone());
        if (_redo.Count > MaxUndo) _redo.RemoveAt(0);
        Draft = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _coalesceKey = null;
        RebuildLayers();
        SelectedLayer = Layers.ElementAtOrDefault(Math.Min(selectedIndex, Layers.Count - 1));
        Validate(false);
        RaiseDocumentState();
        DocumentReplaced?.Invoke(this, EventArgs.Empty);
    }

    public void Redo()
    {
        if (_redo.Count == 0) return;
        var selected = SelectedLayer;
        var selectedIndex = selected is null ? 0 : Math.Max(0, Layers.IndexOf(selected));
        _undo.Add(Draft.Clone());
        if (_undo.Count > MaxUndo) _undo.RemoveAt(0);
        Draft = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        _coalesceKey = null;
        RebuildLayers();
        SelectedLayer = Layers.ElementAtOrDefault(Math.Min(selectedIndex, Layers.Count - 1));
        Validate(false);
        RaiseDocumentState();
        DocumentReplaced?.Invoke(this, EventArgs.Empty);
    }

    public bool AddLayer()
    {
        if (!CanAddLayer) return false;
        var index = SelectedLayer is null ? Draft.Layers.Count : Draft.Layers.IndexOf(SelectedLayer.Layer) + 1;
        index = Math.Clamp(index, 0, Draft.Layers.Count);
        BeginEdit();
        Draft.Layers.Insert(index, CreateDefaultLayer());
        RebuildLayers();
        SelectedLayer = Layers.ElementAtOrDefault(index);
        ApplyEdit();
        return true;
    }

    public bool RemoveSelectedLayer()
    {
        if (!CanRemoveLayer || SelectedLayer is null) return false;
        var index = Draft.Layers.IndexOf(SelectedLayer.Layer);
        BeginEdit();
        Draft.Layers.RemoveAt(index);
        RebuildLayers();
        SelectedLayer = Layers.ElementAtOrDefault(Math.Min(index, Layers.Count - 1));
        ApplyEdit();
        return true;
    }

    public bool DuplicateSelectedLayer()
    {
        if (!CanAddLayer || SelectedLayer is null) return false;
        var source = SelectedLayer.Layer;
        var index = Draft.Layers.IndexOf(source) + 1;
        var copy = new StudioPresetLayerDraft
        {
            ModuleId = source.ModuleId,
            Blend = source.Blend,
            Opacity = source.Opacity,
            Speed = source.Speed,
            Params = (JsonObject)source.Params.DeepClone(),
        };
        copy.Palette.AddRange(source.Palette);
        BeginEdit();
        Draft.Layers.Insert(index, copy);
        RebuildLayers();
        SelectedLayer = Layers.ElementAtOrDefault(index);
        ApplyEdit();
        return true;
    }

    public bool MoveSelectedLayer(int delta)
    {
        if (SelectedLayer is null) return false;
        var sourceIndex = Draft.Layers.IndexOf(SelectedLayer.Layer);
        var targetIndex = Math.Clamp(sourceIndex + delta, 0, Draft.Layers.Count - 1);
        if (sourceIndex == targetIndex) return false;
        BeginEdit();
        var layer = Draft.Layers[sourceIndex];
        Draft.Layers.RemoveAt(sourceIndex);
        Draft.Layers.Insert(targetIndex, layer);
        RebuildLayers();
        SelectedLayer = Layers.ElementAtOrDefault(targetIndex);
        ApplyEdit();
        return true;
    }

    public void ChangeModule(PresetEditorLayerItem item, string moduleId)
    {
        if (item.Layer.ModuleId == moduleId) return;
        BeginEdit();
        item.Layer.ModuleId = moduleId;
        item.Layer.Params = CreateModuleParameters(moduleId);
        if (item.Layer.Palette.Count == 0) item.Layer.Palette.AddRange(DefaultPalette(moduleId));
        foreach (var layerItem in Layers) layerItem.Refresh();
        ApplyEdit();
    }

    public void SetLayerBlend(PresetEditorLayerItem item, StudioBlendMode value)
    {
        if (item.Layer.Blend == value) return;
        BeginEdit("blend:" + LayerKey(item));
        item.Layer.Blend = value;
        item.Refresh();
        ApplyEdit();
    }

    public void SetLayerOpacity(PresetEditorLayerItem item, double value)
    {
        var next = Math.Clamp(value, 0, 1);
        if (Math.Abs(item.Layer.Opacity - next) < 0.0001) return;
        BeginEdit("opacity:" + LayerKey(item));
        item.Layer.Opacity = next;
        item.Refresh();
        ApplyEdit();
    }

    public void SetLayerSpeed(PresetEditorLayerItem item, double value)
    {
        var next = Math.Clamp(value, 0.1, 5);
        if (Math.Abs(item.Layer.Speed - next) < 0.0001) return;
        BeginEdit("speed:" + LayerKey(item));
        item.Layer.Speed = next;
        item.Refresh();
        ApplyEdit();
    }

    public void AddPaletteStop(PresetEditorLayerItem item)
    {
        if (item.Layer.Palette.Count >= 10) return;
        BeginEdit();
        var color = item.Layer.Palette.Count == 0
            ? StudioRgb.White
            : item.Layer.Palette[^1].Color;
        item.Layer.Palette.Add(new StudioColorStop(100, color));
        ApplyEdit();
    }

    public void RemovePaletteStop(PresetEditorLayerItem item, int index)
    {
        if (item.Layer.Palette.Count <= 1 || index < 0 || index >= item.Layer.Palette.Count) return;
        BeginEdit();
        item.Layer.Palette.RemoveAt(index);
        ApplyEdit();
    }

    public void SetPaletteColor(PresetEditorLayerItem item, int index, StudioRgb color)
    {
        if (index < 0 || index >= item.Layer.Palette.Count) return;
        var current = item.Layer.Palette[index];
        if (current.Color == color) return;
        BeginEdit("palette-color:" + LayerKey(item) + ":" + index);
        item.Layer.Palette[index] = current with { Color = color };
        ApplyEdit();
    }

    public void SetPaletteStop(PresetEditorLayerItem item, int index, double stop)
    {
        if (index < 0 || index >= item.Layer.Palette.Count || !double.IsFinite(stop)) return;
        var next = Math.Clamp((int)Math.Round(stop), 0, 100);
        var current = item.Layer.Palette[index];
        if (current.Stop == next) return;
        BeginEdit("palette-stop:" + LayerKey(item) + ":" + index);
        item.Layer.Palette[index] = current with { Stop = next };
        ApplyEdit();
    }

    public void ResetPalette(PresetEditorLayerItem item)
    {
        BeginEdit();
        item.Layer.Palette.Clear();
        item.Layer.Palette.AddRange(DefaultPalette(item.Layer.ModuleId));
        ApplyEdit();
    }

    public void ReplaceDocument(StudioPresetDraft draft, string? path, bool clean)
    {
        Draft = draft;
        Path = path;
        _cleanJson = clean ? SafeJson() : null;
        _undo.Clear();
        _redo.Clear();
        _coalesceKey = null;
        RebuildLayers();
        SelectedLayer = Layers.FirstOrDefault();
        Validate(false);
        RaiseDocumentState();
        DocumentReplaced?.Invoke(this, EventArgs.Empty);
    }

    public void MarkSaved(string path)
    {
        Path = path;
        _cleanJson = SafeJson();
        _coalesceKey = null;
        StudioPresetAutosave.Clear();
        RaiseDocumentState();
    }

    public void MarkRecovered()
    {
        _cleanJson = null;
        _coalesceKey = null;
        RaiseDocumentState();
    }

    public void Validate(bool renderSmoke)
    {
        IReadOnlyList<StudioPresetValidationIssue> issues;
        try
        {
            issues = StudioPresetValidator.Validate(Draft, renderSmoke);
        }
        catch (Exception exception) when (exception is InvalidDataException or FormatException or
            ArgumentException or InvalidOperationException or JsonException)
        {
            issues = [new StudioPresetValidationIssue(-1, "validation", exception.Message)];
        }

        Issues.Clear();
        foreach (var issue in issues) Issues.Add(new PresetEditorIssueItem(issue));
        Raise(nameof(HasIssues));
        Raise(nameof(IssueSummaryText));
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public static JsonObject CreateModuleParameters(string moduleId)
    {
        if (FindExampleLayer(moduleId) is { } example)
        {
            return JsonNode.Parse(example.Parameters.GetRawText()) as JsonObject ?? new JsonObject();
        }

        try
        {
            var fields = RenderModuleRegistry.Describe(
                moduleId, JsonSerializer.SerializeToElement(new JsonObject()));
            var values = new JsonObject();
            foreach (var field in fields)
            {
                values[field.Name] = JsonNode.Parse(field.Default.GetRawText());
            }

            return values;
        }
        catch (Exception exception) when (exception is InvalidDataException or FormatException or
            ArgumentException or JsonException)
        {
            return new JsonObject();
        }
    }

    public static IReadOnlyList<StudioColorStop> DefaultPalette(string moduleId)
    {
        if (FindExampleLayer(moduleId) is { Palette.Stops.Count: > 0 } example)
        {
            return example.Palette.Stops.ToArray();
        }

        return
        [
            new StudioColorStop(0, StudioRgb.FromHex("#44D62C")),
            new StudioColorStop(100, StudioRgb.FromHex("#00D4D4")),
        ];
    }

    private static StudioPresetLayer? FindExampleLayer(string moduleId)
    {
        try
        {
            foreach (var preset in StudioPresetCatalog.Default.All)
            {
                foreach (var layer in preset.Layers)
                {
                    if (layer.ModuleId == moduleId) return layer;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or
            FormatException or InvalidOperationException or UnauthorizedAccessException)
        {
            // The editor still starts with module defaults when a packaged preset is damaged.
        }

        return null;
    }

    public static StudioPresetDraft CreateNewDraft()
    {
        var draft = new StudioPresetDraft
        {
            Id = "custom-" + Guid.NewGuid().ToString("N")[..8],
            Name = PresetEditorText.Get("PresetEditorNewName", "New preset"),
            Kind = StudioPresetKind.Ambient,
            Category = "ambient",
        };

        draft.Layers.Add(CreateDefaultLayer());
        return draft;
    }

    private static StudioPresetLayerDraft CreateDefaultLayer()
    {
        const string fallbackModule = "ambient.field";
        var example = FindExampleLayer(fallbackModule);
        var layer = new StudioPresetLayerDraft
        {
            ModuleId = example?.ModuleId ?? fallbackModule,
            Blend = example?.Blend ?? StudioBlendMode.Normal,
            Opacity = example?.Opacity ?? 1,
            Speed = example?.Speed ?? 1,
            Params = example is null
                ? CreateModuleParameters(fallbackModule)
                : JsonNode.Parse(example.Parameters.GetRawText()) as JsonObject ?? new JsonObject(),
        };
        layer.Palette.AddRange(example is null
            ? DefaultPalette(fallbackModule)
            : example.Palette.Stops);
        return layer;
    }

    private void RebuildLayers(bool keepSelection = false)
    {
        var selectedLayer = keepSelection ? SelectedLayer?.Layer : null;
        Layers.Clear();
        foreach (var layer in Draft.Layers) Layers.Add(new PresetEditorLayerItem(layer));
        if (keepSelection && selectedLayer is not null)
        {
            SelectedLayer = Layers.FirstOrDefault(item => ReferenceEquals(item.Layer, selectedLayer));
        }
        else if (SelectedLayer is not null && !Layers.Contains(SelectedLayer))
        {
            SelectedLayer = null;
        }
    }

    private string LayerKey(PresetEditorLayerItem item)
    {
        var index = Draft.Layers.IndexOf(item.Layer);
        return index >= 0 ? index.ToString(CultureInfo.InvariantCulture) : item.Layer.ModuleId;
    }

    private string SafeJson()
    {
        try
        {
            return StudioPresetCompiler.Serialize(Draft);
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException or
            InvalidOperationException or JsonException)
        {
            return exception.Message;
        }
    }

    private void RaiseDocumentState()
    {
        Raise(nameof(IsDirty));
        Raise(nameof(IsBuiltIn));
        Raise(nameof(CanAddLayer));
        Raise(nameof(CanRemoveLayer));
        Raise(nameof(CanUndo));
        Raise(nameof(CanRedo));
        Raise(nameof(IssueSummaryText));
        Raise(nameof(LayerSummaryText));
        StateChanged?.Invoke(this, EventArgs.Empty);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
