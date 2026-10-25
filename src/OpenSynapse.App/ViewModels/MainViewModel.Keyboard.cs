using System.Collections.ObjectModel;
using OpenSynapse.Windows.Devices;

namespace OpenSynapse.App.ViewModels;

public sealed partial class MainViewModel
{
    private RazerKeyboardDeviceRowViewModel? _selectedKeyboardDevice;
    private RazerKeyboardViewModel? _selectedKeyboard;

    public ObservableCollection<RazerKeyboardDeviceRowViewModel> KeyboardDevices { get; } = new();

    public RazerKeyboardViewModel? SelectedKeyboard
    {
        get => _selectedKeyboard;
        private set => SetField(ref _selectedKeyboard, value);
    }

    private async Task RefreshKeyboardsAsync(CancellationToken cancellationToken)
    {
        RazerKeyboardConnection? connection;
        try
        {
            connection = await RazerKeyboardService.FindAsync(
                message => _diagnosticLog.TryWrite("keyboard", message), cancellationToken);
        }
        catch (Exception exception) when (IsExpectedRuntimeException(exception))
        {
            _diagnosticLog.TryWrite("keyboard", $"discovery failed: {exception}");
            return;
        }

        if (connection is null)
        {
            if (KeyboardDevices.Count == 0 && SelectedKeyboard is null) return;
            KeyboardDevices.Clear();
            _selectedKeyboardDevice = null;
            SelectedKeyboard = null;
            return;
        }

        if (KeyboardDevices.Count == 1 &&
            StringComparer.OrdinalIgnoreCase.Equals(
                KeyboardDevices[0].Connection.DevicePath, connection.DevicePath) &&
            KeyboardDevices[0].Connection.ProductId == connection.ProductId)
        {
            return;
        }

        KeyboardDevices.Clear();
        KeyboardDevices.Add(new RazerKeyboardDeviceRowViewModel(connection));
        if (_selectedKeyboardDevice is null ||
            !StringComparer.OrdinalIgnoreCase.Equals(
                _selectedKeyboardDevice.Connection.DevicePath, connection.DevicePath))
        {
            _selectedKeyboardDevice = null;
            SelectedKeyboard = null;
        }
    }

    public async Task SelectKeyboardAsync(
        RazerKeyboardDeviceRowViewModel row,
        CancellationToken cancellationToken = default)
    {
        if (_selectedKeyboardDevice is not null &&
            SelectedKeyboard is not null &&
            StringComparer.OrdinalIgnoreCase.Equals(
                _selectedKeyboardDevice.Connection.DevicePath, row.Connection.DevicePath))
        {
            return;
        }

        _selectedKeyboardDevice = row;
        SelectedKeyboard = new RazerKeyboardViewModel(
            row.Connection, message => _diagnosticLog.TryWrite("keyboard", message));
        await SelectedKeyboard.LoadAsync(cancellationToken);
    }
}
