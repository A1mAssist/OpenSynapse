using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
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
    private bool _isBusy;
    private string _errorText = string.Empty;
    private string _operationStatusText = string.Empty;
    private int _selectedEqPresetIndex;
    private double _bassLevel = 1;
    private LeviathanV2XInputSource? _inputSource;
    private LeviathanV2XAudioIoStatus? _audioIo;

    public LeviathanV2XAudioViewModel(LeviathanV2XAudioService service)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
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
        }
    }

    public bool CanEdit => !IsBusy;
    public string ErrorText { get => _errorText; private set => SetField(ref _errorText, value); }
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorText);
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
            ErrorText = state.Errors.Count == 0
                ? string.Empty
                : string.Join("; ", state.Errors);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            ErrorText = exception.Message;
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

        await LoadAsync(cancellationToken);
        OperationStatusText = AppStrings.Text("LeviathanSaved");
    }

    public async Task ApplyBassAsync(CancellationToken cancellationToken = default) =>
        await RunWriteAsync(
            () => _service.SetBassAsync((int)Math.Round(BassLevel), cancellationToken),
            cancellationToken);

    public async Task ApplyEqBandsAsync(CancellationToken cancellationToken = default)
    {
        var bands = EqBands.Select(band => (byte)Math.Round(band.Level)).ToArray();
        if (await RunWriteAsync(() => _service.SetEqBandsAsync(bands, cancellationToken), cancellationToken))
        {
            SelectedEqPresetIndex = Array.FindIndex(Presets, item => item.Preset == LeviathanV2XEqPreset.Custom);
        }
    }

    public void RefreshLocalization()
    {
        OnPropertyChanged(string.Empty);
    }

    private void ApplyState(LeviathanV2XAudioState state)
    {
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
            ErrorText = exception.Message;
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
