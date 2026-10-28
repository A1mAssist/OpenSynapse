using System.Collections.ObjectModel;
using OpenSynapse.Windows.Devices;

namespace OpenSynapse.App.ViewModels;

public sealed partial class MainViewModel
{
    private RazerGamepadDeviceRowViewModel? _selectedGamepadDevice;
    private RazerGamepadViewModel? _selectedGamepad;

    public ObservableCollection<RazerGamepadDeviceRowViewModel> GamepadDevices { get; } = new();

    public RazerGamepadViewModel? SelectedGamepad
    {
        get => _selectedGamepad;
        private set => SetField(ref _selectedGamepad, value);
    }

    private async Task RefreshGamepadsAsync(CancellationToken cancellationToken)
    {
        RazerGamepadConnection? connection;
        try
        {
            connection = await RazerGamepadService.FindAsync(
                message => _diagnosticLog.TryWrite("gamepad", message), cancellationToken);
        }
        catch (Exception exception) when (IsExpectedRuntimeException(exception))
        {
            _diagnosticLog.TryWrite("gamepad", $"discovery failed: {exception}");
            return;
        }

        if (connection is null)
        {
            if (GamepadDevices.Count == 0 && SelectedGamepad is null) return;
            GamepadDevices.Clear();
            _selectedGamepadDevice = null;
            SelectedGamepad = null;
            return;
        }

        if (GamepadDevices.Count == 1 &&
            StringComparer.OrdinalIgnoreCase.Equals(
                GamepadDevices[0].Connection.DevicePath, connection.DevicePath) &&
            GamepadDevices[0].Connection.ProductId == connection.ProductId)
        {
            return;
        }

        GamepadDevices.Clear();
        GamepadDevices.Add(new RazerGamepadDeviceRowViewModel(connection));
        if (_selectedGamepadDevice is null ||
            !StringComparer.OrdinalIgnoreCase.Equals(
                _selectedGamepadDevice.Connection.DevicePath, connection.DevicePath))
        {
            _selectedGamepadDevice = null;
            SelectedGamepad = null;
        }
    }

    public async Task SelectGamepadAsync(
        RazerGamepadDeviceRowViewModel row,
        CancellationToken cancellationToken = default)
    {
        if (_selectedGamepadDevice is not null &&
            SelectedGamepad is not null &&
            StringComparer.OrdinalIgnoreCase.Equals(
                _selectedGamepadDevice.Connection.DevicePath, row.Connection.DevicePath))
        {
            return;
        }

        _selectedGamepadDevice = row;
        SelectedGamepad = new RazerGamepadViewModel(
            row.Connection, message => _diagnosticLog.TryWrite("gamepad", message));
        await SelectedGamepad.LoadAsync(cancellationToken);
    }
}
