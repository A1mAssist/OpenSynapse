using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using OpenSynapse.Core.Audio;

namespace OpenSynapse.App.ViewModels;

public sealed record ThxEndpointOption(ThxAudioEndpoint Endpoint, string Label);

public sealed record ThxPresetOption(string Id, string Label);

public sealed class ThxBandViewModel : INotifyPropertyChanged
{
    private double _gainDb;

    public ThxBandViewModel(int frequencyHertz, double gainDb)
    {
        FrequencyHertz = frequencyHertz;
        _gainDb = gainDb;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public int FrequencyHertz { get; }
    public string FrequencyText => FrequencyHertz >= 1000
        ? $"{FrequencyHertz / 1000d:0.#} kHz"
        : $"{FrequencyHertz} Hz";
    public double GainDb
    {
        get => _gainDb;
        set
        {
            if (Math.Abs(_gainDb - value) < 0.001)
            {
                return;
            }
            _gainDb = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(GainText));
        }
    }
    public string GainText => $"{_gainDb:+0.0;-0.0;0.0} dB";

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class ThxAudioViewModel : INotifyPropertyChanged
{
    private readonly IThxAudioController? _controller;
    private readonly bool _isSupportedHost;
    private readonly Dictionary<ThxAudioEndpoint, Dictionary<string, double[]>> _presetCurves = [];
    private readonly Dictionary<ThxAudioEndpoint, Dictionary<string, double[]>> _factoryCurves = [];
    private ThxAudioAvailability _availability = ThxAudioAvailability.Disabled;
    private ThxAudioState? _state;
    private bool _busy;
    private bool _loaded;
    private bool _applying;
    private string _statusText = AppStrings.Text("ThxStatusDisabled");
    private int _selectedEndpointIndex;
    private int _selectedPresetIndex = -1;
    private bool _spatialEnabled;
    private bool _equalizerEnabled;
    private bool _voiceClarityEnabled;
    private double _voiceClarityLevel;
    private bool _normalizationEnabled;
    private double _normalizationLevel;
    private string? _errorCode;

    public ThxAudioViewModel(IThxAudioController? controller, bool isSupportedHost)
    {
        _controller = controller;
        _isSupportedHost = isSupportedHost;
        Endpoints =
        [
            new(ThxAudioEndpoint.Speakers, AppStrings.Text("ThxSpeakers")),
            new(ThxAudioEndpoint.Headphones, AppStrings.Text("ThxHeadphones")),
        ];
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public IReadOnlyList<ThxEndpointOption> Endpoints { get; }
    public ObservableCollection<ThxPresetOption> Presets { get; } = [];
    public ObservableCollection<ThxBandViewModel> Bands { get; } = [];
    public bool IsBusy
    {
        get => _busy;
        private set
        {
            if (SetField(ref _busy, value))
            {
                OnPropertyChanged(nameof(CanRead));
                OnPropertyChanged(nameof(CanEdit));
                OnPropertyChanged(nameof(CanApplyEqualizer));
                OnPropertyChanged(nameof(CanResetEqualizer));
                OnPropertyChanged(nameof(CanDownloadComponent));
            }
        }
    }
    public bool CanRead => !_busy && _controller is not null;
    public bool CanEdit => !_busy && _state?.CanEdit == true;
    public Visibility DeviceCardVisibility => _isSupportedHost ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ControlsVisibility => _availability is ThxAudioAvailability.Available or ThxAudioAvailability.AvailableReadOnly
        ? Visibility.Visible : Visibility.Collapsed;
    public Visibility UnavailableVisibility => ControlsVisibility == Visibility.Visible
        ? Visibility.Collapsed : Visibility.Visible;
    public Visibility SoundNormalizationVisibility => SelectedEndpoint.Endpoint == ThxAudioEndpoint.Speakers
        ? Visibility.Visible : Visibility.Collapsed;
    public Visibility EqualizerEditVisibility => IsEditablePreset ? Visibility.Visible : Visibility.Collapsed;
    public string StatusText { get => _statusText; private set => SetField(ref _statusText, value); }
    public bool HasError => _availability is not (ThxAudioAvailability.Available or ThxAudioAvailability.AvailableReadOnly);
    public bool HasEqualizerChanges
    {
        get
        {
            if (Bands.Count == 0 ||
                _state?.SelectedPresetId is not { } presetId ||
                !_presetCurves.TryGetValue(SelectedEndpoint.Endpoint, out var curves) ||
                !curves.TryGetValue(presetId, out var confirmed) ||
                confirmed.Length != Bands.Count)
            {
                return false;
            }

            for (var index = 0; index < Bands.Count; index++)
            {
                if (Math.Abs(Bands[index].GainDb - confirmed[index]) > 0.001)
                {
                    return true;
                }
            }
            return false;
        }
    }
    public bool CanApplyEqualizer => CanEdit && IsEditablePreset && HasEqualizerChanges;
    public bool CanResetEqualizer => CanEdit && IsEditablePreset &&
        _state?.SelectedPresetId is { } presetId &&
        _factoryCurves.TryGetValue(SelectedEndpoint.Endpoint, out var curves) &&
        curves.TryGetValue(presetId, out var gains) &&
        gains.Length == Bands.Count;
    public string EqualizerHint => IsEditablePreset
        ? AppStrings.Text("ThxEqualizerHint")
        : AppStrings.Text("ThxEqualizerPresetHint");
    public string UnavailableHint => _errorCode switch
    {
        "ThxModelUnknown" or "ThxModelUnsupported" => AppStrings.Text("ThxHintModelUnsupported"),
        "ThxControllerActive" => AppStrings.Text("ThxHintControllerActive"),
        "ThxServiceMissing" => AppStrings.Text("ThxHintServiceMissing"),
        "ThxNativeMissing" => AppStrings.Text("ThxHintNativeMissing"),
        "ThxHelperMissing" => AppStrings.Text("ThxHintHelperMissing"),
        _ when IsNativeMissingError => AppStrings.Text("ThxHintNativeMissing"),
        _ => AppStrings.Text("ThxHintGeneric"),
    };
    public Visibility DownloadVisibility => IsNativeMissingError
        ? Visibility.Visible : Visibility.Collapsed;
    public Visibility InstallVisibility => _errorCode == "ThxServiceMissing"
        ? Visibility.Visible : Visibility.Collapsed;
    public bool CanDownloadComponent => !_busy && _controller is not null && IsNativeMissingError;
    private bool IsNativeMissingError => _errorCode == "ThxNativeMissing" ||
        _errorCode?.StartsWith("ThxDownload", StringComparison.Ordinal) == true;

    public ThxEndpointOption SelectedEndpoint => Endpoints[Math.Clamp(_selectedEndpointIndex, 0, Endpoints.Count - 1)];

    public int SelectedEndpointIndex
    {
        get => _selectedEndpointIndex;
        set
        {
            if (value < 0 || value >= Endpoints.Count || _selectedEndpointIndex == value)
            {
                return;
            }
            _selectedEndpointIndex = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedEndpoint));
            OnPropertyChanged(nameof(SoundNormalizationVisibility));
            OnPropertyChanged(nameof(HasEqualizerChanges));
            OnPropertyChanged(nameof(CanApplyEqualizer));
            OnPropertyChanged(nameof(CanResetEqualizer));
            if (!_applying)
            {
                _ = RefreshAsync();
            }
        }
    }

    public int SelectedPresetIndex
    {
        get => _selectedPresetIndex;
        set
        {
            if (_applying || value < 0 || value >= Presets.Count || _selectedPresetIndex == value)
            {
                return;
            }
            _selectedPresetIndex = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsEditablePreset));
            OnPropertyChanged(nameof(EqualizerEditVisibility));
            OnPropertyChanged(nameof(EqualizerHint));
            OnPropertyChanged(nameof(HasEqualizerChanges));
            OnPropertyChanged(nameof(CanApplyEqualizer));
            OnPropertyChanged(nameof(CanResetEqualizer));
            LoadPresetDraft();
            if (!_applying)
            {
                _ = SelectPresetAsync();
            }
        }
    }

    public bool IsEditablePreset => Presets.Count > 0 &&
        Presets[Math.Clamp(_selectedPresetIndex, 0, Presets.Count - 1)].Id is
            "game" or "movie" or "music" or "voice" or "custom";

    public bool SpatialEnabled
    {
        get => _spatialEnabled;
        set
        {
            if (SetField(ref _spatialEnabled, value) && !_applying)
            {
                _ = SetSpatialAsync(value);
            }
        }
    }

    public bool EqualizerEnabled
    {
        get => _equalizerEnabled;
        set
        {
            if (SetField(ref _equalizerEnabled, value) && !_applying)
            {
                _ = SetEqualizerAsync(value);
            }
        }
    }

    public bool VoiceClarityEnabled
    {
        get => _voiceClarityEnabled;
        set
        {
            if (SetField(ref _voiceClarityEnabled, value) && !_applying)
            {
                _ = SetEnhancementAsync(ThxAudioEnhancementKind.VoiceClarity, value, null);
            }
        }
    }

    public double VoiceClarityLevel
    {
        get => _voiceClarityLevel;
        set
        {
            if (SetField(ref _voiceClarityLevel, Math.Clamp(value, 0, 100)))
            {
                OnPropertyChanged(nameof(VoiceClarityLevelText));
            }
        }
    }

    public string VoiceClarityLevelText => $"{_voiceClarityLevel:0}%";

    public bool SoundNormalizationEnabled
    {
        get => _normalizationEnabled;
        set
        {
            if (SetField(ref _normalizationEnabled, value) && !_applying)
            {
                _ = SetEnhancementAsync(ThxAudioEnhancementKind.SoundNormalization, value, null);
            }
        }
    }

    public double SoundNormalizationLevel
    {
        get => _normalizationLevel;
        set
        {
            if (SetField(ref _normalizationLevel, Math.Clamp(value, 0, 100)))
            {
                OnPropertyChanged(nameof(SoundNormalizationLevelText));
            }
        }
    }

    public string SoundNormalizationLevelText => $"{_normalizationLevel:0}%";

    public Task EnsureLoadedAsync() => _loaded ? Task.CompletedTask : RefreshAsync();

    public async Task RefreshAsync()
    {
        if (_controller is null)
        {
            ApplyStatus(new ThxAudioStatus(ThxAudioAvailability.ComponentsUnavailable, "ThxHelperMissing", null));
            return;
        }

        StatusText = AppStrings.Text("ThxStatusReading");
        await ExecuteAsync(() => _controller.ReadAsync(SelectedEndpoint.Endpoint));
        _loaded = true;
    }

    public async Task InstallNativeComponentAsync()
    {
        if (_controller is not { } controller || !CanDownloadComponent)
        {
            return;
        }

        StatusText = AppStrings.Text("ThxStatusDownloading");
        await ExecuteAsync(
            () => controller.InstallNativeComponentAsync(SelectedEndpoint.Endpoint),
            "ThxDownloadFailed");
        _loaded = true;
    }

    public async Task ApplyEqualizerAsync()
    {
        if (_controller is not { } controller || !CanApplyEqualizer ||
            _state?.SelectedPresetId is not { } presetId)
        {
            return;
        }

        var endpoint = SelectedEndpoint.Endpoint;
        var gains = Bands.Select(band => band.GainDb).ToArray();
        await ExecuteAsync(() => controller.SelectPresetAsync(endpoint, presetId, gains));
    }

    public void ResetEqualizerToFactory()
    {
        if (!CanResetEqualizer || _state?.SelectedPresetId is not { } id ||
            !_factoryCurves.TryGetValue(SelectedEndpoint.Endpoint, out var curves) ||
            !curves.TryGetValue(id, out var gains))
        {
            return;
        }
        ApplyBandValues(gains);
    }

    public void FlattenEqualizer()
    {
        if (CanEdit)
        {
            ApplyBandValues(Enumerable.Repeat(0d, Bands.Count).ToArray());
        }
    }

    public async Task ApplyVoiceClarityAsync()
    {
        if (VoiceClarityEnabled)
        {
            await SetEnhancementAsync(
                ThxAudioEnhancementKind.VoiceClarity,
                true,
                (int)Math.Round(VoiceClarityLevel));
        }
    }

    public async Task ApplySoundNormalizationAsync()
    {
        if (SoundNormalizationEnabled)
        {
            await SetEnhancementAsync(
                ThxAudioEnhancementKind.SoundNormalization,
                true,
                (int)Math.Round(SoundNormalizationLevel));
        }
    }

    private async Task SelectPresetAsync()
    {
        if (_controller is not { } controller || !CanEdit || SelectedPresetIndex < 0 ||
            _state?.SelectedPresetId is not { } currentId)
        {
            return;
        }

        var selected = Presets[SelectedPresetIndex];
        if (selected.Id == currentId)
        {
            return;
        }

        var endpoint = SelectedEndpoint.Endpoint;
        if (!_presetCurves.TryGetValue(endpoint, out var curves) ||
            !curves.TryGetValue(selected.Id, out var gains))
        {
            return;
        }

        await ExecuteAsync(() => controller.SelectPresetAsync(endpoint, selected.Id, gains));
    }

    private async Task SetSpatialAsync(bool enabled)
    {
        if (_controller is not { } controller || !CanEdit)
        {
            return;
        }
        var endpoint = SelectedEndpoint.Endpoint;
        await ExecuteAsync(() => controller.SetSpatialAsync(endpoint, enabled));
    }

    private async Task SetEqualizerAsync(bool enabled)
    {
        if (_controller is not { } controller || !CanEdit)
        {
            return;
        }
        var endpoint = SelectedEndpoint.Endpoint;
        await ExecuteAsync(() => controller.SetEqualizerAsync(endpoint, enabled));
    }

    private async Task SetEnhancementAsync(ThxAudioEnhancementKind kind, bool enabled, int? level)
    {
        if (_controller is not { } controller || !CanEdit)
        {
            return;
        }
        var endpoint = SelectedEndpoint.Endpoint;
        await ExecuteAsync(() => controller.SetEnhancementAsync(
            endpoint, kind, enabled, level));
    }

    private async Task ExecuteAsync(
        Func<Task<ThxAudioStatus>> operation,
        string failureCode = "ThxHelperFailed")
    {
        IsBusy = true;
        try
        {
            ApplyStatus(await operation());
        }
        catch (Exception exception)
        {
            ApplyStatus(new ThxAudioStatus(
                ThxAudioAvailability.ComponentsUnavailable,
                $"{failureCode}:{exception.Message}",
                null));
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyStatus(ThxAudioStatus status)
    {
        _availability = status.Availability;
        _errorCode = status.Error;
        OnPropertyChanged(nameof(ControlsVisibility));
        OnPropertyChanged(nameof(UnavailableVisibility));
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(UnavailableHint));
        OnPropertyChanged(nameof(DownloadVisibility));
        OnPropertyChanged(nameof(InstallVisibility));
        OnPropertyChanged(nameof(CanDownloadComponent));
        if (status.State is not { } state || !status.IsAvailable)
        {
            _state = null;
            OnPropertyChanged(nameof(CanEdit));
            OnPropertyChanged(nameof(HasEqualizerChanges));
            OnPropertyChanged(nameof(CanApplyEqualizer));
            OnPropertyChanged(nameof(CanResetEqualizer));
            StatusText = LocalizeError(status);
            return;
        }

        _state = state;
        _applying = true;
        try
        {
            SpatialEnabled = state.SpatialEnabled;
            EqualizerEnabled = state.EqualizerEnabled;
            VoiceClarityEnabled = state.VoiceClarity.Enabled;
            VoiceClarityLevel = state.VoiceClarity.LevelPercent;
            SoundNormalizationEnabled = state.SoundNormalization?.Enabled ?? false;
            SoundNormalizationLevel = state.SoundNormalization?.LevelPercent ?? 0;

            Presets.Clear();
            foreach (var preset in state.Presets)
            {
                Presets.Add(new ThxPresetOption(preset.Id, LocalizePreset(preset)));
            }
            var endpoint = SelectedEndpoint.Endpoint;
            if (!_presetCurves.TryGetValue(endpoint, out var curves))
            {
                curves = new Dictionary<string, double[]>(StringComparer.Ordinal);
                _presetCurves[endpoint] = curves;
            }
            if (!_factoryCurves.TryGetValue(endpoint, out var factory))
            {
                factory = new Dictionary<string, double[]>(StringComparer.Ordinal);
                _factoryCurves[endpoint] = factory;
            }
            foreach (var (id, gains) in state.FactoryCurves)
            {
                factory[id] = gains.ToArray();
                curves.TryAdd(id, gains.ToArray());
            }
            if (state.SelectedPresetId is { } selectedId)
            {
                curves[selectedId] = state.EqualizerBands.Select(band => band.GainDb).ToArray();
            }
            var selectedIndex = state.SelectedPresetId is null
                ? -1
                : state.Presets.ToList().FindIndex(preset => preset.Id == state.SelectedPresetId);
            _selectedPresetIndex = selectedIndex;
            OnPropertyChanged(nameof(SelectedPresetIndex));
            OnPropertyChanged(nameof(IsEditablePreset));
            OnPropertyChanged(nameof(EqualizerEditVisibility));
            OnPropertyChanged(nameof(EqualizerHint));

            Bands.Clear();
            foreach (var band in state.EqualizerBands)
            {
                var item = new ThxBandViewModel(band.FrequencyHertz, band.GainDb);
                item.PropertyChanged += BandPropertyChanged;
                Bands.Add(item);
            }
        }
        finally
        {
            _applying = false;
        }

        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(HasEqualizerChanges));
        OnPropertyChanged(nameof(CanApplyEqualizer));
        OnPropertyChanged(nameof(CanResetEqualizer));
        StatusText = AppStrings.Text("ThxStatusReady");
    }

    private void BandPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(ThxBandViewModel.GainDb))
        {
            OnPropertyChanged(nameof(HasEqualizerChanges));
            OnPropertyChanged(nameof(CanApplyEqualizer));
        }
    }

    private void LoadPresetDraft()
    {
        if (SelectedPresetIndex < 0 ||
            !_presetCurves.TryGetValue(SelectedEndpoint.Endpoint, out var curves) ||
            !curves.TryGetValue(Presets[SelectedPresetIndex].Id, out var gains) ||
            gains.Length != Bands.Count)
        {
            return;
        }
        ApplyBandValues(gains);
    }

    private void ApplyBandValues(IReadOnlyList<double> gains)
    {
        for (var index = 0; index < Bands.Count && index < gains.Count; index++)
        {
            Bands[index].GainDb = gains[index];
        }
    }

    private static string LocalizePreset(ThxAudioPreset preset) => preset.Id switch
    {
        "game" => AppStrings.Text("ThxPresetGame"),
        "movie" => AppStrings.Text("ThxPresetMovie"),
        "music" => AppStrings.Text("ThxPresetMusic"),
        "voice" => AppStrings.Text("ThxPresetVoice"),
        "custom" => AppStrings.Text("ThxPresetCustom"),
        "default" => AppStrings.Text("ThxPresetDefault"),
        _ => preset.Name,
    };

    private static string LocalizeError(ThxAudioStatus status) => status.Error switch
    {
        "ThxModelUnknown" => AppStrings.Text("ThxStatusModelUnknown"),
        "ThxModelUnsupported" => AppStrings.Text("ThxStatusModelUnsupported"),
        "ThxUsbMissing" => AppStrings.Text("ThxStatusUsbMissing"),
        "ThxServiceMissing" => AppStrings.Text("ThxStatusServiceMissing"),
        "ThxNativeMissing" => AppStrings.Text("ThxStatusNativeMissing"),
        "ThxControllerActive" => AppStrings.Text("ThxStatusControllerActive"),
        "ThxHelperMissing" => AppStrings.Text("ThxStatusHelperMissing"),
        "ThxHelperExited" => AppStrings.Text("ThxStatusHelperExited"),
        "ThxHelperInvalidResponse" or "ThxHelperFailed" => AppStrings.Text("ThxStatusHelperFailed"),
        "ThxDeviceChanged" => AppStrings.Text("ThxStatusDeviceChanged"),
        "ThxServiceNotReady" => AppStrings.Text("ThxStatusServiceNotReady"),
        "ThxOperationRejected" => AppStrings.Text("ThxStatusOperationRejected"),
        "ThxNativeFailure" => AppStrings.Text("ThxStatusNativeFailure"),
        "ThxInvalidRequest" => AppStrings.Text("ThxStatusInvalidRequest"),
        "ThxDownloadFailed" => AppStrings.Text("ThxStatusDownloadFailed"),
        "ThxDownloadVerificationFailed" => AppStrings.Text("ThxStatusDownloadVerificationFailed"),
        "ThxDownloadWriteFailed" => AppStrings.Text("ThxStatusDownloadWriteFailed"),
        null when status.Availability == ThxAudioAvailability.Disabled => AppStrings.Text("ThxStatusDisabled"),
        null => AppStrings.Text("ThxStatusUnavailable"),
        _ when status.Error.StartsWith("ThxDownloadFailed:", StringComparison.Ordinal) =>
            AppStrings.Text("ThxStatusDownloadFailed"),
        _ when status.Error.StartsWith("ThxHelperFailed:", StringComparison.Ordinal) =>
            AppStrings.FormatText("ThxStatusHelperFailedDetail", status.Error["ThxHelperFailed:".Length..]),
        _ => status.Error,
    };

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
