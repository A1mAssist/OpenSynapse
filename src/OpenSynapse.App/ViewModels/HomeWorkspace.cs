using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using OpenSynapse.Core.Storage;

namespace OpenSynapse.App.ViewModels;

public sealed record HomeQuickAction(string Id, string Label, string Glyph);
public sealed record HomeDeviceOption(string Name, string Identity, object? Source, string Glyph);

public sealed class HomeCardEditorRow : INotifyPropertyChanged
{
    private bool _isVisible;
    private string _label;

    public HomeCardEditorRow(string id, string label, bool isVisible)
    {
        Id = id;
        _label = label;
        _isVisible = isVisible;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public string Id { get; }
    public string Label
    {
        get => _label;
        set
        {
            if (_label == value) return;
            _label = value;
            PropertyChanged?.Invoke(this, new(nameof(Label)));
        }
    }
    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            if (_isVisible == value) return;
            _isVisible = value;
            PropertyChanged?.Invoke(this, new(nameof(IsVisible)));
        }
    }

    public void RefreshLabel(string label) => Label = label;
}

public sealed partial class HomeWorkspace : INotifyPropertyChanged
{
    public static readonly string[] CardIds = ["status", "devices", "quick-actions", "profiles", "telemetry"];
    private readonly HomeLayoutStore _store;
    private HomeLayoutSettings _layout = HomeLayoutSettings.CreateDefault();
    private bool _isEditing;
    private string _statusText = string.Empty;
    private IReadOnlyList<HomeDeviceOption> _devices = [];
    private string _applicationSwitchingStateText = string.Empty;
    private string _performanceModeText = string.Empty;
    private string _performancePowerText = string.Empty;
    private bool _canCyclePerformanceMode;

    public HomeWorkspace(HomeLayoutStore? store = null) => _store = store ?? new HomeLayoutStore();

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Func<string, string?, CancellationToken, Task>? QuickActionRequested;
    public ObservableCollection<HomeCardEditorRow> EditorCards { get; } = [];
    public IReadOnlyList<string> CardOrder => _layout.CardOrder;
    public IReadOnlyList<HomeQuickAction> QuickActions { get; private set; } =
    [
        new(HomeLayoutSettings.OpenStudio, Text("HomeOpenStudio", "Open Lighting Studio"), "\uE790"),
    ];
    public string StatusText { get => _statusText; private set => SetField(ref _statusText, value); }
    public bool IsEditing { get => _isEditing; private set => SetField(ref _isEditing, value); }
    public Visibility EditorVisibility => IsEditing ? Visibility.Visible : Visibility.Collapsed;
    public Visibility BrowseVisibility => IsEditing ? Visibility.Collapsed : Visibility.Visible;
    public bool HasStatus => !string.IsNullOrWhiteSpace(StatusText);
    public IReadOnlyList<HomeDeviceOption> Devices => _devices;
    public string ApplicationSwitchingStateText { get => _applicationSwitchingStateText; private set => SetField(ref _applicationSwitchingStateText, value); }
    public bool IsCardVisible(string id) => !_layout.HiddenCards.Contains(id);
    public string PerformanceModeText { get => _performanceModeText; private set => SetField(ref _performanceModeText, value); }
    public string PerformancePowerText { get => _performancePowerText; private set => SetField(ref _performancePowerText, value); }
    public bool CanCyclePerformanceMode { get => _canCyclePerformanceMode; private set => SetField(ref _canCyclePerformanceMode, value); }
    public string PerformanceCardTitle => Text("HomePerformanceCard", "Performance");
    public string CyclePerformanceLabel => Text("HomeCyclePerformance", "Cycle performance mode");
    public string OpenStudioLabel => Text("HomeOpenStudio", "Open Lighting Studio");
    public Visibility PerformanceUnavailableVisibility => CanCyclePerformanceMode ? Visibility.Collapsed : Visibility.Visible;
    public Visibility PerformanceAvailableVisibility => CanCyclePerformanceMode ? Visibility.Visible : Visibility.Collapsed;

    public void SetPerformanceState(string modeText, string powerText, bool canCycle)
    {
        PerformanceModeText = modeText;
        PerformancePowerText = powerText;
        CanCyclePerformanceMode = canCycle;
        if (!canCycle && string.IsNullOrWhiteSpace(PerformanceModeText))
            PerformanceModeText = Text("HomePerformanceUnavailable", "No controllable laptop detected");
    }

    public void SetDevices(IReadOnlyList<HomeDeviceOption> devices)
    {
        _devices = devices;
        OnPropertyChanged(nameof(Devices));
    }

    public void UpdateApplicationSwitching(bool enabled) =>
        ApplicationSwitchingStateText = Text(
            enabled ? "HomeApplicationSwitchingEnabled" : "HomeApplicationSwitchingDisabled",
            enabled ? "Application switching is on" : "Application switching is off");

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            ApplyLayout(await _store.LoadAsync(cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            StatusText = AppStrings.FormatText("HomeLayoutLoadFailed", exception.Message);
        }
    }

    public void BeginEdit()
    {
        StatusText = string.Empty;
        EditorCards.Clear();
        foreach (var id in _layout.CardOrder) EditorCards.Add(new(id, CardLabel(id), IsCardVisible(id)));
        IsEditing = true;
    }

    public void CancelEdit()
    {
        StatusText = string.Empty;
        IsEditing = false;
    }

    public void Move(HomeCardEditorRow row, int offset)
    {
        var index = EditorCards.IndexOf(row);
        var destination = index + offset;
        if (index < 0 || destination < 0 || destination >= EditorCards.Count) return;
        EditorCards.Move(index, destination);
    }

    public void Pin(HomeCardEditorRow row)
    {
        var index = EditorCards.IndexOf(row);
        if (index > 0) EditorCards.Move(index, 0);
    }

    public async Task SaveEditAsync(CancellationToken cancellationToken = default)
    {
        if (!IsEditing) return;
        try
        {
            var layout = HomeLayoutSettings.Sanitize(new()
            {
                CardOrder = EditorCards.Select(row => row.Id).ToList(),
                HiddenCards = EditorCards.Where(row => !row.IsVisible).Select(row => row.Id).ToHashSet(StringComparer.OrdinalIgnoreCase),
            });
            await _store.SaveAsync(layout, cancellationToken);
            ApplyLayout(layout);
            IsEditing = false;
            StatusText = Text("HomeLayoutSaved", "Layout saved.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            StatusText = Text("HomeLayoutNotSaved", "Layout was not saved.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            StatusText = Text("HomeLayoutSaveFailed", "Could not save the layout.") + " " + exception.Message;
        }
    }

    public async Task ResetAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var layout = HomeLayoutSettings.CreateDefault();
            await _store.SaveAsync(layout, cancellationToken);
            ApplyLayout(layout);
            if (IsEditing) BeginEdit();
            StatusText = Text("HomeLayoutReset", "Layout restored to default.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            StatusText = Text("HomeLayoutSaveFailed", "Could not save the layout.") + " " + exception.Message;
        }
    }

    public Task InvokeQuickActionAsync(string id, string? argument = null, CancellationToken cancellationToken = default) =>
        HomeLayoutSettings.IsQuickAction(id) && QuickActionRequested is { } handler
            ? handler(id, argument, cancellationToken)
            : Task.CompletedTask;

    public void RefreshLocalization()
    {
        QuickActions =
        [
            new(HomeLayoutSettings.OpenStudio, Text("HomeOpenStudio", "Open Lighting Studio"), "\uE790"),
        ];
        OnPropertyChanged(nameof(QuickActions));
        OnPropertyChanged(nameof(PerformanceCardTitle));
        OnPropertyChanged(nameof(CyclePerformanceLabel));
        OnPropertyChanged(nameof(OpenStudioLabel));
        if (IsEditing) foreach (var row in EditorCards) row.RefreshLabel(CardLabel(row.Id));
        if (!CanCyclePerformanceMode)
            PerformanceModeText = Text("HomePerformanceUnavailable", "No controllable laptop detected");
    }

    private void ApplyLayout(HomeLayoutSettings layout)
    {
        _layout = layout;
        OnPropertyChanged(nameof(CardOrder));
        foreach (var id in CardIds) OnPropertyChanged($"Is{id}Visible");
    }

    private static string CardLabel(string id) => id switch
    {
        "status" => Text("HomeStatusCard", "Status"),
        "devices" => Text("HomeDevicesCard", "Devices"),
        "quick-actions" => Text("HomePerformanceCard", "Performance"),
        "profiles" => Text("HomeProfilesCard", "Profiles"),
        "telemetry" => Text("HomeTelemetryCard", "System telemetry"),
        _ => throw new ArgumentOutOfRangeException(nameof(id)),
    };

    private static string Text(string key, string fallback) => AppStrings.TryGet(key) ?? fallback;

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        if (propertyName == nameof(IsEditing))
        {
            OnPropertyChanged(nameof(EditorVisibility));
            OnPropertyChanged(nameof(BrowseVisibility));
        }
        else if (propertyName == nameof(StatusText)) OnPropertyChanged(nameof(HasStatus));
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new(propertyName));
}
