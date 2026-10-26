using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

public sealed record OpenRazerMappingAction(string Name, byte FunctionId, byte[] Data);

public sealed class OpenRazerMappingRowViewModel : INotifyPropertyChanged
{
    private int _targetIndex;

    internal OpenRazerMappingRowViewModel(byte buttonId, IReadOnlyList<string> actionNames)
    {
        ButtonId = buttonId;
        ButtonName = OpenRazerMappingViewModel.DescribeButton(buttonId);
        ActionNames = actionNames;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public byte ButtonId { get; }
    public string ButtonName { get; }
    public IReadOnlyList<string> ActionNames { get; }
    public string CurrentActionText { get; internal set; } = "--";
    public int TargetIndex
    {
        get => _targetIndex;
        set
        {
            if (_targetIndex == value) return;
            _targetIndex = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TargetIndex)));
        }
    }
}

public sealed class OpenRazerMappingViewModel : INotifyPropertyChanged
{
    private static readonly OpenRazerMappingAction[] Catalog =
    [
        new("Off", OpenRazerMappingFunctions.Off, []),
        new("Left", OpenRazerMappingFunctions.ButtonCode, [0x01]),
        new("Right", OpenRazerMappingFunctions.ButtonCode, [0x02]),
        new("Middle", OpenRazerMappingFunctions.ButtonCode, [0x03]),
        new("Back", OpenRazerMappingFunctions.ButtonCode, [0x04]),
        new("Forward", OpenRazerMappingFunctions.ButtonCode, [0x05]),
        new("Button 6", OpenRazerMappingFunctions.ButtonCode, [0x06]),
        new("Button 7", OpenRazerMappingFunctions.ButtonCode, [0x07]),
        new("Button 8", OpenRazerMappingFunctions.ButtonCode, [0x08]),
        new("Scroll up", OpenRazerMappingFunctions.ButtonCode, [0x09]),
        new("Scroll down", OpenRazerMappingFunctions.ButtonCode, [0x0A]),
        new("DPI up", OpenRazerMappingFunctions.ButtonCode, [0x0B]),
        new("DPI down", OpenRazerMappingFunctions.ButtonCode, [0x0C]),
        new("DPI cycle up", OpenRazerMappingFunctions.ButtonCode, [0x60]),
        new("DPI cycle down", OpenRazerMappingFunctions.ButtonCode, [0x61]),
        new("Sniper", OpenRazerMappingFunctions.ButtonCode, [0x0F]),
        new("Profile switch", OpenRazerMappingFunctions.ButtonCode, [0x0E]),
    ];

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
    private static readonly string[] KeepLabel = ["(keep)"];
    public IReadOnlyList<string> ActionNames { get; } =
        KeepLabel.Concat(Catalog.Select(action => action.Name)).ToArray();
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
    public bool CanApply => !IsBusy && _loaded && Rows.Any(row => row.TargetIndex > 0);
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
                var row = new OpenRazerMappingRowViewModel(id, ActionNames);
                try
                {
                    var assignment = await _service.GetButtonAssignmentAsync(
                        _connection, id, Mode, cancellationToken).ConfigureAwait(true);
                row.CurrentActionText = Describe(assignment.FunctionId, assignment.Data);
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
            foreach (var row in Rows.Where(row => row.TargetIndex > 0).ToArray())
            {
                var action = Catalog[row.TargetIndex - 1];
                await _service.SetButtonAssignmentAsync(
                    _connection, row.ButtonId, Mode, action.FunctionId, action.Data,
                    cancellationToken).ConfigureAwait(true);
                row.CurrentActionText = Describe(action.FunctionId, action.Data);
                row.TargetIndex = 0;
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

    internal static string DescribeButton(byte buttonId) => buttonId switch
    {
        0x01 => "Left",
        0x02 => "Right",
        0x03 => "Middle",
        0x04 => "Back",
        0x05 => "Forward",
        >= 0x06 and <= 0x08 => $"Button {buttonId}",
        0x09 => "Scroll up",
        0x0A => "Scroll down",
        0x0B => "DPI up",
        0x0C => "DPI down",
        0x0D => "User select",
        0x0E => "Profile switch",
        0x0F => "Sniper",
        >= 0x10 and <= 0x1F => $"Macro {buttonId - 0x0F}",
        0x60 => "DPI cycle up",
        0x61 => "DPI cycle down",
        _ => $"Button {buttonId}",
    };

    private static string Describe(byte functionId, IReadOnlyList<byte> data) =>
        functionId switch
        {
            OpenRazerMappingFunctions.Off => "Off",
            OpenRazerMappingFunctions.ButtonCode when data.Count > 0 => DescribeButton(data[0]),
            OpenRazerMappingFunctions.ButtonCode => "Button",
            _ => $"Function {functionId}" +
                 (data.Count == 0
                     ? string.Empty
                     : $" [{string.Join(' ', data.Select(value => value.ToString("X2")))}]"),
        };

    private static bool IsExpected(Exception exception) =>
        exception is InvalidOperationException or InvalidDataException or NotSupportedException or
            IOException or TimeoutException or UnauthorizedAccessException or
            System.ComponentModel.Win32Exception;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
