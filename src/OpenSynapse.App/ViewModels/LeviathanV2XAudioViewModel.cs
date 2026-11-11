using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Core.Sensors;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

public sealed class LeviathanV2XEqBandViewModel : INotifyPropertyChanged
{
    private double _level;

    public LeviathanV2XEqBandViewModel(int frequency)
    {
        Label = frequency >= 1_000 ? $"{frequency / 1_000d:0.#} kHz" : $"{frequency} Hz";
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public string Label { get; }
    public string LevelText => $"{Math.Round(Level)}";

    public double Level
    {
        get => _level;
        set
        {
            var normalized = double.IsFinite(value) ? Math.Clamp(value, 0, 100) : 0;
            if (Math.Abs(_level - normalized) < 0.01)
            {
                return;
            }

            _level = normalized;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Level)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LevelText)));
        }
    }
}

public sealed class LeviathanV2XAudioViewModel : INotifyPropertyChanged
{
    private static readonly (LeviathanV2XEqPreset Preset, string LabelKey)[] Presets =
    [
        (LeviathanV2XEqPreset.Default, "LeviathanPresetDefault"),
        (LeviathanV2XEqPreset.Game, "LeviathanPresetGame"),
        (LeviathanV2XEqPreset.Music, "LeviathanPresetMusic"),
        (LeviathanV2XEqPreset.Movie, "LeviathanPresetMovie"),
        (LeviathanV2XEqPreset.Treble, "LeviathanPresetTreble"),
        (LeviathanV2XEqPreset.Custom, "LeviathanPresetCustom"),
    ];

    private readonly LeviathanV2XAudioService _service;
    private readonly Func<ProfileDocument>? _getProfile;
    private readonly Func<CancellationToken, Task<bool>>? _saveProfile;
    private bool _isBusy;
    private bool _hasState;
    private string _errorText = string.Empty;
    private string _partialReadText = string.Empty;
    private string _operationStatusText = string.Empty;
    private int _selectedEqPresetIndex;
    private double _bassLevel = 1;
    private LeviathanV2XInputSource? _inputSource;
    private LeviathanV2XAudioIoStatus? _audioIo;

    public LeviathanV2XAudioViewModel(
        LeviathanV2XAudioService service,
        Func<ProfileDocument>? getProfile = null,
        Func<CancellationToken, Task<bool>>? saveProfile = null)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _getProfile = getProfile;
        _saveProfile = saveProfile;
        foreach (var frequency in LeviathanV2XProtocol.EqBandFrequencies)
        {
            EqBands.Add(new LeviathanV2XEqBandViewModel(frequency));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<LeviathanV2XEqBandViewModel> EqBands { get; } = new();
    public IReadOnlyList<string> EqPresetOptions => Presets.Select(item => AppStrings.Text(item.LabelKey)).ToArray();
    public int SelectedEqPresetIndex
    {
        get => _selectedEqPresetIndex;
        set => SetField(ref _selectedEqPresetIndex, Math.Clamp(value, 0, Presets.Length - 1));
    }

    public double BassLevel
    {
        get => _bassLevel;
        set
        {
            var normalized = double.IsFinite(value) ? Math.Clamp(value, 1, 7) : 1;
            if (!SetField(ref _bassLevel, normalized))
            {
                return;
            }

            OnPropertyChanged(nameof(BassText));
        }
    }

    public string BassText => $"{Math.Round(BassLevel)} / 7";
    public string InputSourceText =>
        $"{AppStrings.Text("LeviathanInputSourceLabel")}: {FormatInputSource(_inputSource)}";
    public string AudioIoText =>
        $"{AppStrings.Text("LeviathanAudioIoLabel")}: USB {FormatPresent(_audioIo?.Usb)} · AUX {FormatPresent(_audioIo?.AuxIn)} · {AppStrings.Text("LeviathanOptical")} {FormatPresent(_audioIo?.Optical)} · {AppStrings.Text("LeviathanBluetooth")} {FormatPresent(_audioIo?.Bluetooth)}";

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetField(ref _isBusy, value))
            {
                return;
            }

            OnPropertyChanged(nameof(CanEdit));
            OnPropertyChanged(nameof(CanRefresh));
        }
    }

    public bool HasState
    {
        get => _hasState;
        private set
        {
            if (!SetField(ref _hasState, value))
            {
                return;
            }

            OnPropertyChanged(nameof(CanEdit));
        }
    }

    public bool CanRefresh => !IsBusy;
    public bool CanEdit => !IsBusy && HasState;
    public string ErrorText { get => _errorText; private set => SetField(ref _errorText, value); }
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorText);
    public string PartialReadText
    {
        get => _partialReadText;
        private set
        {
            if (SetField(ref _partialReadText, value))
            {
                OnPropertyChanged(nameof(HasPartialRead));
            }
        }
    }
    public bool HasPartialRead => !string.IsNullOrWhiteSpace(PartialReadText);
    public string OperationStatusText
    {
        get => _operationStatusText;
        private set => SetField(ref _operationStatusText, value);
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!BeginOperation())
        {
            return;
        }

        try
        {
            var state = await _service.ReadStateAsync(cancellationToken);
            ApplyState(state);
            await ApplyPersistedProfileAsync(PowerSourceKind.Unknown, cancellationToken);
            var hasErrors = state.Errors.Count > 0;
            PartialReadText = hasErrors && HasState
                ? AppStrings.Text("LeviathanPartialRead")
                : string.Empty;
            ErrorText = hasErrors && !HasState
                ? AppStrings.Text("LeviathanReadFailed")
                : string.Empty;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            HasState = false;
            PartialReadText = string.Empty;
            ErrorText = AppStrings.Text("LeviathanReadFailed");
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ApplyEqPresetAsync(CancellationToken cancellationToken = default)
    {
        if (!await RunWriteAsync(
                () => _service.SetEqPresetAsync(Presets[SelectedEqPresetIndex].Preset, cancellationToken),
                cancellationToken))
        {
            return;
        }

        if (!await SaveProfileAsync(
                settings => settings.EqPreset = Presets[SelectedEqPresetIndex].Preset.ToString(),
                cancellationToken))
        {
            ErrorText = AppStrings.Text("RazerHidSettingProfileSaveFailed");
            return;
        }

        await LoadAsync(cancellationToken);
        OperationStatusText = AppStrings.Text("LeviathanSaved");
    }

    public async Task ApplyBassAsync(CancellationToken cancellationToken = default)
    {
        var bass = (int)Math.Round(BassLevel);
        if (!await RunWriteAsync(
                () => _service.SetBassAsync(bass, cancellationToken),
                cancellationToken))
        {
            return;
        }

        if (!await SaveProfileAsync(settings => settings.Bass = bass, cancellationToken))
        {
            ErrorText = AppStrings.Text("RazerHidSettingProfileSaveFailed");
        }
    }

    public async Task ApplyEqBandsAsync(CancellationToken cancellationToken = default)
    {
        var bands = EqBands.Select(band => (byte)Math.Round(band.Level)).ToArray();
        if (await RunWriteAsync(() => _service.SetEqBandsAsync(bands, cancellationToken), cancellationToken))
        {
            SelectedEqPresetIndex = Array.FindIndex(Presets, item => item.Preset == LeviathanV2XEqPreset.Custom);
            var saved = bands
                .Select((level, index) => (Key: index.ToString(), Level: (int)level))
                .ToDictionary(item => item.Key, item => item.Level, StringComparer.OrdinalIgnoreCase);
            if (!await SaveProfileAsync(settings => settings.EqBands = saved, cancellationToken))
            {
                ErrorText = AppStrings.Text("RazerHidSettingProfileSaveFailed");
            }
        }
    }

    [NotProfileScoped]
    public async Task ApplyPersistedProfileAsync(
        PowerSourceKind powerSource,
        CancellationToken cancellationToken = default)
    {
        if (_getProfile?.Invoke() is not { } document)
        {
            return;
        }

        var descriptor = new DeviceDescriptor(
            ProfileKey,
            "Razer Leviathan V2 X",
            0x1532,
            LeviathanV2XProtocol.ProductId,
            DeviceAccessState.Available,
            DeviceCapabilityState.PendingValidation,
            0,
            1,
            2,
            "leviathan-v2x",
            DeviceCategory.Speaker);
        var settings = ProfileResolver.Resolve(document, descriptor, powerSource).Headset;
        try
        {
            if (!string.IsNullOrWhiteSpace(settings.EqPreset) &&
                Enum.TryParse<LeviathanV2XEqPreset>(settings.EqPreset, true, out var preset))
            {
                await _service.SetEqPresetAsync(preset, cancellationToken);
            }

            if (settings.EqBands is { Count: LeviathanV2XProtocol.EqBandCount } bands)
            {
                var values = Enumerable.Range(0, bands.Count)
                    .Select(index => (byte)Math.Clamp(
                        bands.TryGetValue(index.ToString(), out var value) ? value : 0, 0, 100))
                    .ToArray();
                await _service.SetEqBandsAsync(values, cancellationToken);
            }

            if (settings.Bass is int bass)
            {
                await _service.SetBassAsync(Math.Clamp(bass, 1, 7), cancellationToken);
            }

            ApplyState(await _service.ReadStateAsync(cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            ErrorText = AppStrings.Text("LeviathanWriteFailed");
        }
    }

    private string ProfileKey => $"1532:{LeviathanV2XProtocol.ProductId:X4}";

    private Task<bool> SaveProfileAsync(
        Action<HeadsetProfileSettings> mutate,
        CancellationToken cancellationToken) =>
        PeripheralProfileStore.SaveAsync(
            _getProfile?.Invoke(),
            _saveProfile,
            ProfileKey,
            device => mutate(device.Headset),
            cancellationToken);

    public void RefreshLocalization()
    {
        OnPropertyChanged(string.Empty);
    }

    private void ApplyState(LeviathanV2XAudioState state)
    {
        HasState = state.EqPreset is not null ||
            state.EqBands is not null ||
            state.BassLevel is not null ||
            state.InputSource is not null ||
            state.IoStatus is not null;
        if (state.EqPreset is { } preset)
        {
            var index = Array.FindIndex(Presets, item => item.Preset == preset);
            if (index >= 0)
            {
                SelectedEqPresetIndex = index;
            }
        }

        if (state.EqBands is { Count: LeviathanV2XProtocol.EqBandCount } bands)
        {
            for (var index = 0; index < bands.Count; index++)
            {
                EqBands[index].Level = bands[index];
            }
        }

        if (state.BassLevel is int bass)
        {
            BassLevel = bass;
        }

        if (state.InputSource is { } source)
        {
            _inputSource = source;
            OnPropertyChanged(nameof(InputSourceText));
        }

        if (state.IoStatus is { } io)
        {
            _audioIo = io;
            OnPropertyChanged(nameof(AudioIoText));
        }
    }

    private async Task<bool> RunWriteAsync(
        Func<Task> write,
        CancellationToken cancellationToken)
    {
        if (!BeginOperation())
        {
            return false;
        }

        try
        {
            await write();
            ErrorText = string.Empty;
            OperationStatusText = AppStrings.Text("LeviathanSaved");
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            ErrorText = AppStrings.Text("LeviathanWriteFailed");
            OperationStatusText = string.Empty;
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool BeginOperation()
    {
        if (IsBusy)
        {
            return false;
        }

        ErrorText = string.Empty;
        OperationStatusText = string.Empty;
        IsBusy = true;
        return true;
    }

    private static string FormatInputSource(LeviathanV2XInputSource? source) => source switch
    {
        LeviathanV2XInputSource.Usb => "USB",
        LeviathanV2XInputSource.Bluetooth => AppStrings.Text("LeviathanBluetooth"),
        LeviathanV2XInputSource.Optical => AppStrings.Text("LeviathanOptical"),
        LeviathanV2XInputSource.Aux => "AUX",
        LeviathanV2XInputSource.None => "--",
        _ => "--",
    };

    private static string FormatPresent(bool? present) => present switch
    {
        true => "✓",
        false => "—",
        _ => "--",
    };

    private static bool IsExpectedException(Exception exception) =>
        exception is IOException or InvalidOperationException or ArgumentOutOfRangeException or
            System.ComponentModel.Win32Exception or UnauthorizedAccessException;

    private bool SetField<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        if (propertyName == nameof(ErrorText))
        {
            OnPropertyChanged(nameof(HasError));
        }

        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
