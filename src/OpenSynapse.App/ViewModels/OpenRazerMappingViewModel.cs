using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

public sealed class OpenRazerMappingRowViewModel : INotifyPropertyChanged
{
    private int _groupIndex;
    private int _optionIndex;
    private int _modifierIndex;
    private IReadOnlyList<string> _optionNames = [];

    internal OpenRazerMappingRowViewModel(byte buttonId)
    {
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
            OptionNames = OpenRazerMappingActions.OptionNames((OpenRazerMappingGroup)value);
            _optionIndex = 0;
            OnPropertyChanged();
            OnPropertyChanged(nameof(OptionIndex));
            OnPropertyChanged(nameof(ModifierVisibility));
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
    public Visibility ModifierVisibility => _groupIndex == (int)OpenRazerMappingGroup.KeyboardKey
        ? Visibility.Visible
        : Visibility.Collapsed;
    public bool CanApply => _groupIndex > 0;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
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
                OnPropertyChanged(nameof(CanApply));
            }
        }
    }

    private byte Mode => (byte)LayerIndex;

    public async Task ReadAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy) return;
        IsBusy = true;
        ErrorText = string.Empty;
        try
        {
            var ids = await _service.GetMappingButtonIdsAsync(_connection, cancellationToken)
                .ConfigureAwait(true);
            Rows.Clear();
            foreach (var id in ids)
            {
                var row = new OpenRazerMappingRowViewModel(id);
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
                        OpenRazerMappingActions.Describe(assignment.FunctionId, assignment.Data);
                }
                catch (Exception exception) when (IsExpected(exception))
                {
                    row.CurrentActionText = "unreadable";
                    _log?.Invoke($"mapping button {id} read failed: {exception.Message}");
                }
                Rows.Add(row);
            }
            _loaded = true;
            StatusText = Rows.Count == 0 ? "No mappable buttons" : $"Read {Rows.Count} buttons";
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            _loaded = false;
            Rows.Clear();
            ErrorText = exception.Message;
            _log?.Invoke($"mapping read failed: {exception}");
        }
        finally
        {
            IsBusy = false;
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
                var action = OpenRazerMappingActions.CreateAction(
                    (OpenRazerMappingGroup)row.GroupIndex,
                    row.OptionIndex,
                    OpenRazerMappingActions.ModifierMasks[row.ModifierIndex]);
                if (action is null)
                {
                    continue;
                }
                await _service.SetButtonAssignmentAsync(
                    _connection, row.ButtonId, Mode, action.FunctionId, action.Data,
                    cancellationToken).ConfigureAwait(true);
                row.CurrentActionText = OpenRazerMappingActions.Describe(action.FunctionId, action.Data);
                row.GroupIndex = 0;
                applied++;
            }
            StatusText = applied == 0 ? "Nothing changed" : $"Saved {applied} assignments";
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

    private static bool IsExpected(Exception exception) =>
        exception is InvalidOperationException or InvalidDataException or NotSupportedException or
            IOException or TimeoutException or UnauthorizedAccessException or
            System.ComponentModel.Win32Exception;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
