using System.Collections.ObjectModel;
using System.ComponentModel;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using OpenSynapse.Windows.Devices;

namespace OpenSynapse.App.ViewModels;

public sealed partial class SurfaceCalibrationViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    private readonly IRazerSurfaceCalibrationClient _client;
    private readonly RazerSurfaceCalibrationIdentity _identity;
    private readonly RazerSurfaceCalibrationStore _store;
    private RazerSurfaceCalibrationSession _session;
    private readonly IRazerSurfaceCalibrationInputSource _input;
    private readonly Func<bool>? _canWriteResolver;
    private readonly Action<string>? _log;
    private readonly DispatcherQueue? _dispatcher = DispatcherQueue.GetForCurrentThread();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly RazerCalibrationConnection _connection;
    private readonly RazerCalibrationProductData? _product;
    private readonly RazerSurfaceCalibrationConfiguration? _defaults;
    private RazerSurfaceCalibrationConfiguration _draft = new();
    private RazerSurfaceCalibrationRecord? _savedRecord;
    private RazerSurfaceCalibrationRecord? _pendingSave;
    private Task _operation = Task.CompletedTask;
    private bool _isBusy;
    private bool _readAttempted;
    private bool _isConnected = true;
    private bool _canWrite = true;
    private bool _disposed;
    private string _status = AppStrings.Text("SurfaceCalibrationNotRead");
    private string _storeError = string.Empty;
    private string _surfaceName = string.Empty;
    private string? _selectedSurfaceId;
    private double _tracking = double.NaN, _lift = double.NaN, _landing = double.NaN, _lod = double.NaN;

    public SurfaceCalibrationViewModel(IRazerSurfaceCalibrationClient client,
        RazerSurfaceCalibrationIdentity identity, Func<bool>? canWrite = null,
        IRazerSurfaceCalibrationInputSource? input = null, RazerSurfaceCalibrationStore? store = null,
        RazerCalibrationConnection connection = RazerCalibrationConnection.Wired, Action<string>? log = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _identity = identity ?? throw new ArgumentNullException(nameof(identity));
        _store = store ?? RazerSurfaceCalibrationStore.Shared;
        _canWriteResolver = canWrite;
        _log = log;
        _input = input ?? new RazerSurfaceCalibrationInputSource(identity.InstanceId);
        _connection = connection;
        _product = client.Profile.Calibration;
        _defaults = RazerSurfaceCalibrationAlgorithms.CreateDefaultConfiguration(client.Profile);
        _session = CreateSession();
        RazerSurfaceCalibrationOperations.AvailabilityChanged += OperationsAvailabilityChanged;
        var modes = new List<SurfaceCalibrationModeOption>();
        void Add(RazerSurfaceCalibrationMode mode) => modes.Add(new(mode, AppStrings.Text($"SurfaceCalibrationMode{mode}")));
        if (Has(RazerCalibrationCapabilities.EnableDisable)) Add(RazerSurfaceCalibrationMode.Disabled);
        if (Has(RazerCalibrationCapabilities.Adaptive)) Add(RazerSurfaceCalibrationMode.Adaptive);
        if (Has(RazerCalibrationCapabilities.Asymmetric) && IsOrca) Add(RazerSurfaceCalibrationMode.Adjustable);
        if (IsLiftOnly ? _product?.LiftOffDistance is not null && Has(RazerCalibrationCapabilities.LiftOff)
            : Has(RazerCalibrationCapabilities.Preset)) Add(RazerSurfaceCalibrationMode.Preset);
        if (Has(RazerCalibrationCapabilities.Manual)) Add(RazerSurfaceCalibrationMode.Manual);
        if (IsLiftOnly)
            modes = modes.Select(item => item.Mode == RazerSurfaceCalibrationMode.Preset
                ? item with { Name = AppStrings.Text("SurfaceCalibrationEnabled") } : item).ToList();
        Modes = modes;
        Presets = _product?.Presets.Select(item => new SurfaceCalibrationPresetOption(item.Id, PresetName(item.Id))).ToArray() ?? [];
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public IReadOnlyList<SurfaceCalibrationModeOption> Modes { get; }
    public IReadOnlyList<SurfaceCalibrationPresetOption> Presets { get; }
    public ObservableCollection<SurfaceCalibrationSavedOption> SavedSurfaces { get; } = [];
    public RazerSurfaceCalibrationIdentity Identity => _identity;
    public string StatusText => _status;
    public string StoreErrorText => _storeError;
    public bool HasStoreError => _storeError.Length != 0;
    public bool IsBusy => _isBusy;
    public bool IsSessionActive => _preparingSession || _session.State is RazerSurfaceCalibrationSessionState.Preparing or
        RazerSurfaceCalibrationSessionState.WaitingToStart or RazerSurfaceCalibrationSessionState.Starting or
        RazerSurfaceCalibrationSessionState.Sampling or RazerSurfaceCalibrationSessionState.Ending or
        RazerSurfaceCalibrationSessionState.Reading or RazerSurfaceCalibrationSessionState.Applying or
        RazerSurfaceCalibrationSessionState.Saving or RazerSurfaceCalibrationSessionState.Cancelling;
    public bool CanRead => !_disposed && _isConnected && ConnectionSupported && !_isBusy && !IsSessionActive &&
        !HasPendingSave && !RazerSurfaceCalibrationOperations.IsBusy(_identity);
    public bool CanEdit => CanRead && _canWrite && (_canWriteResolver?.Invoke() ?? true) && _readAttempted && !HasPendingSave;
    public bool CanApply => CanEdit && IsValidDraft(requireRaw: true);
    public bool CanPrepare => CanEdit && Has(RazerCalibrationCapabilities.Manual);
    public bool CanPrepareMini => CanEdit && Has(RazerCalibrationCapabilities.Mini) && SelectedPreset is not null && Valid(LodRange, _lod);
    public bool CanStart => !_isBusy && _session.State == RazerSurfaceCalibrationSessionState.WaitingToStart;
    public bool CanFinish => !_isBusy && _session.State == RazerSurfaceCalibrationSessionState.Sampling;
    public bool CanCancel => IsSessionActive && _session.State != RazerSurfaceCalibrationSessionState.Cancelling;
    public bool HasPendingSave => _pendingSave is not null || _session.State == RazerSurfaceCalibrationSessionState.AppliedUnsaved;
    public bool CanRetrySave => !_disposed && !_isBusy && HasPendingSave;
    public bool CanRestoreSaved => CanEdit && _savedRecord is not null;
    public bool CanRestoreDefaults => CanEdit && _defaults is not null;

    public Visibility Visibility => Microsoft.UI.Xaml.Visibility.Visible;
    public Visibility ControlsVisibility => Visible(ConnectionSupported);
    public Visibility ConnectionHintVisibility => Visible(!ConnectionSupported);
    public Visibility AdjustmentHintVisibility => Visible(ConnectionSupported && IsLiftOnly && _product?.LiftOffDistance is null);
    public string ConnectionHint => AppStrings.Text(_product is null ? "SurfaceCalibrationUnknownDevice" :
        _product.BleSupport == RazerCalibrationSupport.Unsupported ? "SurfaceCalibrationUseUsb" : "SurfaceCalibrationConnectionUnknown");
    public Visibility PresetVisibility => Visible(Mode == RazerSurfaceCalibrationMode.Preset && Presets.Count != 0);
    public Visibility TrackingVisibility => Visible(Mode == RazerSurfaceCalibrationMode.Adaptive && !IsAsymmetric);
    public Visibility LiftVisibility => Visible(IsLiftOnly && LiftRange is not null && Mode == RazerSurfaceCalibrationMode.Preset ||
        Mode == RazerSurfaceCalibrationMode.Adjustable || IsAsymmetric && Mode is RazerSurfaceCalibrationMode.Adaptive or RazerSurfaceCalibrationMode.Preset or RazerSurfaceCalibrationMode.Manual);
    public Visibility LandingVisibility => Visible(!IsLiftOnly && LiftVisibility == Microsoft.UI.Xaml.Visibility.Visible);
    public Visibility LodVisibility => Visible(!IsLiftOnly && LodRange is not null && !IsAsymmetric && Mode is
        RazerSurfaceCalibrationMode.Preset or RazerSurfaceCalibrationMode.Manual);
    public int LiftColumnSpan => IsLiftOnly ? 2 : 1;
    public Visibility AsymmetricVisibility => Visible(Has(RazerCalibrationCapabilities.Asymmetric) && !IsOrca &&
        Mode is RazerSurfaceCalibrationMode.Adaptive or RazerSurfaceCalibrationMode.Preset or RazerSurfaceCalibrationMode.Manual);
    public Visibility ManualVisibility => Visible(Has(RazerCalibrationCapabilities.Manual));
    public Visibility MiniVisibility => Visible(Has(RazerCalibrationCapabilities.Mini) && Mode == RazerSurfaceCalibrationMode.Preset);
    public Visibility SavedVisibility => Visible(SavedSurfaces.Count != 0);
    public Visibility SessionVisibility => Visible(IsSessionActive);
    public Visibility StatusVisibility => Visible(!IsSessionActive);
    public Visibility RetrySaveVisibility => Visible(HasPendingSave);
    public Visibility DefaultsVisibility => Visible(_defaults is not null);
    public string SessionText => AppStrings.Text($"SurfaceCalibrationSession{_session.State}");
    public string SpeedText => _session.Speed is { } speed ? AppStrings.Text($"SurfaceCalibration{speed}") : string.Empty;
    public Visibility SpeedVisibility => Visible(_session.State == RazerSurfaceCalibrationSessionState.Sampling && _session.Speed is not null);

    public SurfaceCalibrationModeOption? SelectedMode
    {
        get => Modes.FirstOrDefault(item => item.Mode == Mode);
        set
        {
            if (value is null || !CanEdit || value.Mode == Mode) return;
            _draft = _draft with { Mode = value.Mode, IsAsymmetric = IsOrca
                ? value.Mode == RazerSurfaceCalibrationMode.Adjustable : _draft.IsAsymmetric };
            _selectedSurfaceId = null;
            FillDraftRanges();
            Changed();
        }
    }

    public SurfaceCalibrationPresetOption? SelectedPreset
    {
        get => Presets.FirstOrDefault(item => item.Id == _draft.PresetId);
        set
        {
            if (value is null || !CanEdit || value.Id == _draft.PresetId) return;
            _draft = _draft with { PresetId = value.Id, RawData = null };
            _selectedSurfaceId = null;
            Changed();
        }
    }

    public SurfaceCalibrationSavedOption? SelectedSavedSurface
    {
        get => SavedSurfaces.FirstOrDefault(item => item.Id == _selectedSurfaceId);
        set
        {
            if (value is null || !CanEdit) return;
            _selectedSurfaceId = value.Id;
            SetDraft(value.Configuration);
            SurfaceName = value.Name;
        }
    }

    public bool IsAsymmetric
    {
        get => _draft.IsAsymmetric;
        set
        {
            if (!CanEdit || value == _draft.IsAsymmetric) return;
            _draft = _draft with { IsAsymmetric = value };
            FillDraftRanges();
            Changed();
        }
    }

    public string SurfaceName { get => _surfaceName; set { if (_surfaceName == value) return; _surfaceName = value; Changed(); } }
    public double TrackingDistance { get => _tracking; set { if (_tracking.Equals(value)) return; _tracking = value; Changed(); } }
    public double LiftOffDistance { get => _lift; set { if (_lift.Equals(value)) return; _lift = value; Changed(); } }
    public double LandingDistance { get => _landing; set { if (_landing.Equals(value)) return; _landing = value; Changed(); } }
    public double Lod { get => _lod; set { if (_lod.Equals(value)) return; _lod = value; Changed(); } }
    public double TrackingMinimum => TrackingRange?.Minimum ?? 0;
    public double TrackingMaximum => TrackingRange?.Maximum ?? 0;
    public double TrackingStep => TrackingRange?.Step ?? 1;
    public double LiftMinimum => LiftRange?.Minimum ?? 0;
    public double LiftMaximum => LiftRange?.Maximum ?? 0;
    public double LiftStep => LiftRange?.Step ?? 1;
    public double LandingMinimum => LandingRange?.Minimum ?? 0;
    public double LandingMaximum => LandingRange?.Maximum ?? 0;
    public double LandingStep => LandingRange?.Step ?? 1;
    public double LodMinimum => LodRange?.Minimum ?? 0;
    public double LodMaximum => LodRange?.Maximum ?? 0;
    public double LodStep => LodRange?.Step ?? 1;
    public string TrackingLabel => RangeLabel("SurfaceCalibrationTracking", TrackingRange);
    public string LiftLabel => RangeLabel("SurfaceCalibrationLift", LiftRange);
    public string LandingLabel => RangeLabel("SurfaceCalibrationLanding", LandingRange);
    public string LodLabel => RangeLabel("SurfaceCalibrationLod", LodRange);

    private RazerSurfaceCalibrationMode Mode => _draft.Mode == RazerSurfaceCalibrationMode.Mini
        ? RazerSurfaceCalibrationMode.Preset : _draft.Mode;
    private bool IsOrca => _client.Profile.Kind is RazerProximityCalibrationKind.Orca5 or RazerProximityCalibrationKind.Orca5Plus;
    private bool IsLiftOnly => _client.Profile.Kind == RazerProximityCalibrationKind.Pixart3349;
    private bool ConnectionSupported => _product is not null &&
        (_connection != RazerCalibrationConnection.Ble || _product.BleSupport == RazerCalibrationSupport.Supported);
    private RazerCalibrationRange? TrackingRange => _product?.TrackingDistance;
    private bool SmartSurfaceMode => _client.Profile.Kind == RazerProximityCalibrationKind.SmartPixart &&
        Mode is RazerSurfaceCalibrationMode.Preset or RazerSurfaceCalibrationMode.Manual;
    private RazerCalibrationRange? LiftRange => SmartSurfaceMode ? _product?.SurfaceLiftOffDistance : _product?.LiftOffDistance;
    private RazerCalibrationRange? LandingRange => SmartSurfaceMode ? _product?.SurfaceLandingDistance : _product?.LandingDistance;
    private RazerCalibrationRange? LodRange => _product?.Lod;
    private bool Has(RazerCalibrationCapabilities capability) => _product?.Capabilities.HasFlag(capability) == true;
    private static Visibility Visible(bool value) => value ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
    private void Changed() { if (!_disposed) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null)); }
    private void OperationsAvailabilityChanged(RazerSurfaceCalibrationIdentity identity)
    {
        if (_disposed || !StringComparer.OrdinalIgnoreCase.Equals(identity.InstanceId, _identity.InstanceId) &&
            (string.IsNullOrWhiteSpace(identity.ControlDevicePath) || string.IsNullOrWhiteSpace(_identity.ControlDevicePath) ||
             !StringComparer.OrdinalIgnoreCase.Equals(identity.ControlDevicePath, _identity.ControlDevicePath))) return;
        if (_dispatcher is not null && !_dispatcher.HasThreadAccess) _dispatcher.TryEnqueue(Changed);
        else Changed();
    }
    private static bool Valid(RazerCalibrationRange? range, double value) => range is not null &&
        double.IsFinite(value) && value == Math.Truncate(value) && value >= int.MinValue && value <= int.MaxValue && range.Contains((int)value);
    private static string RangeLabel(string key, RazerCalibrationRange? range) => range is null ? AppStrings.Text(key) :
        AppStrings.FormatText("SurfaceCalibrationRange", AppStrings.Text(key), range.Minimum, range.Maximum,
            AppStrings.Text(range.Unit == RazerCalibrationDistanceUnit.Millimeters ? "SurfaceCalibrationMillimeters" : "SurfaceCalibrationLevels"));
    private static string PresetName(string id) => id.Replace('_', ' ').Replace('-', ' ');

    private void FillDraftRanges()
    {
        if (!Valid(TrackingRange, _tracking)) _tracking = TrackingRange?.DefaultValue ?? double.NaN;
        if (!Valid(LiftRange, _lift)) _lift = LiftRange?.DefaultValue ?? double.NaN;
        if (!Valid(LandingRange, _landing)) _landing = LandingRange?.DefaultValue ?? double.NaN;
        if (!Valid(LodRange, _lod)) _lod = LodRange?.DefaultValue ?? double.NaN;
    }

    private void SetDraft(RazerSurfaceCalibrationConfiguration configuration)
    {
        _draft = configuration;
        _tracking = Valid(TrackingRange, configuration.TrackingDistance) ? configuration.TrackingDistance : double.NaN;
        _lift = Valid(LiftRange, configuration.LiftOffDistance) ? configuration.LiftOffDistance : double.NaN;
        _landing = Valid(LandingRange, configuration.LandingDistance) ? configuration.LandingDistance : double.NaN;
        _lod = Valid(LodRange, configuration.Lod) ? configuration.Lod : double.NaN;
        Changed();
    }

    private RazerSurfaceCalibrationConfiguration DraftConfiguration() => _draft with
    {
        TrackingDistance = double.IsFinite(_tracking) ? (int)_tracking : 0,
        LiftOffDistance = double.IsFinite(_lift) ? (int)_lift : 0,
        LandingDistance = double.IsFinite(_landing) ? (int)_landing : 0,
        Lod = double.IsFinite(_lod) ? (int)_lod : 0,
    };

    private bool IsValidDraft(bool requireRaw)
    {
        if (SelectedMode is null) return false;
        if (Mode == RazerSurfaceCalibrationMode.Disabled) return true;
        if (TrackingVisibility == Microsoft.UI.Xaml.Visibility.Visible && !Valid(TrackingRange, _tracking)) return false;
        if (LiftVisibility == Microsoft.UI.Xaml.Visibility.Visible && !Valid(LiftRange, _lift)) return false;
        if (LandingVisibility == Microsoft.UI.Xaml.Visibility.Visible && (!Valid(LandingRange, _landing) || _landing >= _lift)) return false;
        if (LodVisibility == Microsoft.UI.Xaml.Visibility.Visible && !Valid(LodRange, _lod)) return false;
        if (Mode == RazerSurfaceCalibrationMode.Preset && Presets.Count != 0 && SelectedPreset is null) return false;
        return !requireRaw || _draft.RawData is not null || Mode != RazerSurfaceCalibrationMode.Manual &&
            _client.Profile.Kind != RazerProximityCalibrationKind.Philips;
    }
}

public sealed record SurfaceCalibrationModeOption(RazerSurfaceCalibrationMode Mode, string Name);
public sealed record SurfaceCalibrationPresetOption(string Id, string Name);
public sealed record SurfaceCalibrationSavedOption(string Id, string Name, RazerSurfaceCalibrationConfiguration Configuration);
