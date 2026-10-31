using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

public sealed class OpenRazerMappingRowViewModel : INotifyPropertyChanged
{
    private readonly IReadOnlyList<OpenRazerMacroOption> _macros;
    private int _groupIndex;
    private int _optionIndex;
    private int _modifierIndex;
    private double _turboRate = 7;
    private double _profileId = 1;
    private double _dpiClutchX = 800;
    private double _dpiClutchY = 800;
    private int _macroPlaybackIndex;
    private double _macroRepeatCount = 1;
    private IReadOnlyList<string> _optionNames = [];

    internal OpenRazerMappingRowViewModel(
        byte buttonId,
        IReadOnlyList<OpenRazerMacroOption> macros)
    {
        _macros = macros;
        ButtonId = buttonId;
        ButtonName = OpenRazerMappingActions.DescribeButton(buttonId);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public byte ButtonId { get; }
    public string ButtonName { get; }
    public IReadOnlyList<string> ModifierNames => OpenRazerMappingActions.ModifierNames;
    public string CurrentActionText { get; internal set; } = "--";
    public IReadOnlyList<string> OptionNames
    {
        get => _optionNames;
        private set
        {
            _optionNames = value;
            OnPropertyChanged();
        }
    }
    public int GroupIndex
    {
        get => _groupIndex;
        set
        {
            if (_groupIndex == value) return;
            _groupIndex = value;
            OptionNames = OpenRazerMappingActions.OptionNames(
                (OpenRazerMappingGroup)value,
                _macros);
            _optionIndex = 0;
            OnPropertyChanged();
            OnPropertyChanged(nameof(OptionIndex));
            OnPropertyChanged(nameof(ModifierVisibility));
            OnPropertyChanged(nameof(TurboRateVisibility));
            OnPropertyChanged(nameof(ProfileIdVisibility));
            OnPropertyChanged(nameof(DpiClutchVisibility));
            OnPropertyChanged(nameof(MacroPlaybackVisibility));
            OnPropertyChanged(nameof(MacroRepeatVisibility));
            OnPropertyChanged(nameof(CanApply));
        }
    }
    public int OptionIndex
    {
        get => _optionIndex;
        set
        {
            if (_optionIndex == value) return;
            _optionIndex = value;
            OnPropertyChanged();
        }
    }
    public int ModifierIndex
    {
        get => _modifierIndex;
        set
        {
            if (_modifierIndex == value) return;
            _modifierIndex = value;
            OnPropertyChanged();
        }
    }
    public double TurboRate
    {
        get => _turboRate;
        set
        {
            var normalized = double.IsFinite(value) ? Math.Clamp(Math.Round(value), 1, 50) : _turboRate;
            if (_turboRate == normalized) return;
            _turboRate = normalized;
            OnPropertyChanged();
        }
    }
    public double ProfileId
    {
        get => _profileId;
        set
        {
            var normalized = double.IsFinite(value) ? Math.Clamp(Math.Round(value), 1, 5) : _profileId;
            if (_profileId == normalized) return;
            _profileId = normalized;
            OnPropertyChanged();
        }
    }
    public double DpiClutchX
    {
        get => _dpiClutchX;
        set => SetDpi(ref _dpiClutchX, value);
    }
    public double DpiClutchY
    {
        get => _dpiClutchY;
        set => SetDpi(ref _dpiClutchY, value);
    }
    public IReadOnlyList<string> MacroPlaybackOptions => OpenRazerMappingActions.MacroPlaybackNames;
    public int MacroPlaybackIndex
    {
        get => _macroPlaybackIndex;
        set
        {
            var clamped = Math.Clamp(value, 0, OpenRazerMappingActions.MacroPlaybackNames.Count - 1);
            if (_macroPlaybackIndex == clamped) return;
            _macroPlaybackIndex = clamped;
            OnPropertyChanged();
            OnPropertyChanged(nameof(MacroRepeatVisibility));
        }
    }
    public double MacroRepeatCount
    {
        get => _macroRepeatCount;
        set
        {
            var normalized = double.IsFinite(value)
                ? Math.Clamp(Math.Round(value), 1, byte.MaxValue)
                : _macroRepeatCount;
            if (_macroRepeatCount == normalized) return;
            _macroRepeatCount = normalized;
            OnPropertyChanged();
        }
    }
    public Visibility ModifierVisibility => _groupIndex == (int)OpenRazerMappingGroup.KeyboardKey ||
        _groupIndex == (int)OpenRazerMappingGroup.TurboKey
        ? Visibility.Visible
        : Visibility.Collapsed;
    public Visibility TurboRateVisibility => _groupIndex == (int)OpenRazerMappingGroup.TurboKey ||
        _groupIndex == (int)OpenRazerMappingGroup.TurboButton
        ? Visibility.Visible
        : Visibility.Collapsed;
    public Visibility ProfileIdVisibility => _groupIndex == (int)OpenRazerMappingGroup.ProfileSpecific
        ? Visibility.Visible
        : Visibility.Collapsed;
    public Visibility DpiClutchVisibility => _groupIndex == (int)OpenRazerMappingGroup.DpiClutch
        ? Visibility.Visible
        : Visibility.Collapsed;
    public Visibility MacroPlaybackVisibility =>
        _groupIndex == (int)OpenRazerMappingGroup.Macro && _macros.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    public Visibility MacroRepeatVisibility =>
        MacroPlaybackVisibility == Visibility.Visible &&
        _macroPlaybackIndex == (int)OpenRazerMacroPlayback.Repeat
            ? Visibility.Visible
            : Visibility.Collapsed;
    public bool CanApply => _groupIndex > 0 &&
        (_groupIndex != (int)OpenRazerMappingGroup.Macro || _macros.Count > 0);
    public OpenRazerMappingAction? CreateAction() =>
        OpenRazerMappingActions.CreateAction(
            (OpenRazerMappingGroup)_groupIndex,
            _optionIndex,
            OpenRazerMappingActions.ModifierMasks[Math.Clamp(
                _modifierIndex, 0, OpenRazerMappingActions.ModifierMasks.Count - 1)],
            _macros,
            checked((int)TurboRate),
            checked((int)ProfileId),
            checked((int)DpiClutchX),
            checked((int)DpiClutchY),
            (OpenRazerMacroPlayback)_macroPlaybackIndex,
            checked((int)MacroRepeatCount));

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private void SetDpi(ref double field, double value)
    {
        var normalized = double.IsFinite(value)
            ? Math.Clamp(Math.Round(value / 50) * 50, 100, 30000)
            : field;
        if (field == normalized) return;
        field = normalized;
        OnPropertyChanged();
    }
}

public sealed class OpenRazerMappingViewModel : INotifyPropertyChanged
{
    private readonly OpenRazerDeviceService _service;
    private readonly OpenRazerDeviceConnection _connection;
    private readonly Action<string>? _log;
    private bool _isBusy;
    private string _statusText = string.Empty;
    private string _errorText = string.Empty;
    private bool _loaded;
    private int _layerIndex;
    private IReadOnlyList<OpenRazerMacroOption> _macros = [];

    public OpenRazerMappingViewModel(
        OpenRazerDeviceService service,
        OpenRazerDeviceConnection connection,
        Action<string>? log = null)
    {
        _service = service;
        _connection = connection;
        _log = log;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<OpenRazerMappingRowViewModel> Rows { get; } = [];
    public bool IsLoaded => _loaded;
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (_isBusy == value) return;
            _isBusy = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanRead));
            OnPropertyChanged(nameof(CanApply));
        }
    }
    public bool CanRead => !IsBusy;
    public bool CanApply => !IsBusy && _loaded && Rows.Any(row => row.CanApply);
    public string StatusText
    {
        get => _statusText;
        private set { if (_statusText == value) return; _statusText = value; OnPropertyChanged(); }
    }
    public bool HasError => _errorText.Length > 0;
    public string ErrorText
    {
        get => _errorText;
        private set
        {
            if (_errorText == value) return;
            _errorText = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasError));
        }
    }
    public int LayerIndex
    {
        get => _layerIndex;
        set
        {
            if (_layerIndex == value) return;
            _layerIndex = value;
            OnPropertyChanged();
            if (_loaded)
            {
                Rows.Clear();
                _loaded = false;
                StatusText = string.Empty;
                OnPropertyChanged(nameof(IsLoaded));
                OnPropertyChanged(nameof(CanApply));
            }
        }
    }

    private byte Mode => (byte)LayerIndex;

    public Task ReadAsync(CancellationToken cancellationToken = default) =>
        ReadAsync([], cancellationToken);

    public async Task ReadAsync(
        IReadOnlyList<OpenRazerMacroOption> macros,
        CancellationToken cancellationToken = default)
    {
        if (IsBusy) return;
        IsBusy = true;
        ErrorText = string.Empty;
        _macros = macros;
        try
        {
            var ids = await _service.GetMappingButtonIdsAsync(_connection, cancellationToken)
                .ConfigureAwait(true);
            Rows.Clear();
            foreach (var id in ids)
            {
                var row = new OpenRazerMappingRowViewModel(id, _macros);
                row.PropertyChanged += (_, args) =>
                {
                    if (args.PropertyName == nameof(OpenRazerMappingRowViewModel.GroupIndex))
                    {
                        OnPropertyChanged(nameof(CanApply));
                    }
                };
                try
                {
                    var assignment = await _service.GetButtonAssignmentAsync(
                        _connection, id, Mode, cancellationToken).ConfigureAwait(true);
                    row.CurrentActionText =
                        OpenRazerMappingActions.Describe(
                            assignment.FunctionId,
                            assignment.Data,
                            _macros);
                }
                catch (Exception exception) when (IsExpected(exception))
                {
                    row.CurrentActionText = AppStrings.Text("OpenRazerMappingUnreadable");
                    _log?.Invoke($"mapping button {id} read failed: {exception.Message}");
                }
                Rows.Add(row);
            }
            _loaded = true;
            StatusText = Rows.Count == 0
                ? AppStrings.Text("OpenRazerMappingNoButtons")
                : _macros.Count == 0
                    ? AppStrings.FormatText("OpenRazerMappingReadCount", Rows.Count)
                    : AppStrings.FormatText(
                        "OpenRazerMappingReadCountWithMacros", Rows.Count, _macros.Count);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            _loaded = false;
            Rows.Clear();
            StatusText = string.Empty;
            ErrorText = exception.Message;
            _log?.Invoke($"mapping read failed: {exception}");
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(IsLoaded));
            OnPropertyChanged(nameof(CanApply));
        }
    }

    public async Task ApplyAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy || !_loaded) return;
        IsBusy = true;
        ErrorText = string.Empty;
        var applied = 0;
        try
        {
            foreach (var row in Rows.Where(row => row.CanApply).ToArray())
            {
                var action = row.CreateAction();
                if (action is null)
                {
                    continue;
                }
                await _service.SetButtonAssignmentAsync(
                    _connection, row.ButtonId, Mode, action.FunctionId, action.Data,
                    cancellationToken).ConfigureAwait(true);
                row.CurrentActionText = OpenRazerMappingActions.Describe(
                    action.FunctionId,
                    action.Data,
                    _macros);
                row.GroupIndex = 0;
                applied++;
            }
            StatusText = applied == 0
                ? AppStrings.Text("OpenRazerMappingNothingChanged")
                : AppStrings.FormatText("OpenRazerMappingSavedCount", applied);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            ErrorText = exception.Message;
            _log?.Invoke($"mapping apply failed: {exception}");
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(CanApply));
        }
    }

    internal void SetLoadError(string message)
    {
        _loaded = false;
        Rows.Clear();
        StatusText = string.Empty;
        ErrorText = string.IsNullOrWhiteSpace(message)
            ? AppStrings.Text("OpenRazerMappingReadFailed")
            : message;
        OnPropertyChanged(nameof(IsLoaded));
        OnPropertyChanged(nameof(CanApply));
    }

    private static bool IsExpected(Exception exception) =>
        exception is InvalidOperationException or InvalidDataException or NotSupportedException or
            IOException or TimeoutException or UnauthorizedAccessException or
            System.ComponentModel.Win32Exception;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
