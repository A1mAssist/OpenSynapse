using System.Collections.ObjectModel;
using OpenSynapse.Windows.Devices;

namespace OpenSynapse.App.ViewModels;

public sealed partial class UsbAccessoriesWorkspace
{
    private RazerGamepadDeviceRowViewModel? _selectedGamepadDevice;
    private RazerGamepadViewModel? _selectedGamepad;

    public ObservableCollection<RazerGamepadDeviceRowViewModel> GamepadDevices { get; } = new();

    public RazerGamepadViewModel? SelectedGamepad
    {
        get => _selectedGamepad;
        private set => SetField(ref _selectedGamepad, value);
    }

    internal async Task RefreshGamepadsAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<RazerGamepadConnection> connections;
        try
        {
            connections = await RazerGamepadService.FindAllAsync(
                message => _diagnosticLog.TryWrite("gamepad", message), cancellationToken);
        }
        catch (Exception exception) when (IsExpectedRuntimeException(exception))
        {
            _diagnosticLog.TryWrite("gamepad", $"discovery failed: {exception}");
            return;
        }

        if (connections.Count == 0)
        {
            if (GamepadDevices.Count == 0 && SelectedGamepad is null) return;
            GamepadDevices.Clear();
            _selectedGamepadDevice = null;
            SelectedGamepad = null;
            return;
        }

        if (GamepadDevices.Count == connections.Count &&
            connections.Zip(GamepadDevices).All(pair =>
                pair.First.ProductId == pair.Second.Connection.ProductId &&
                StringComparer.OrdinalIgnoreCase.Equals(
                    pair.First.DevicePath, pair.Second.Connection.DevicePath)))
        {
            return;
        }

        var previousPath = _selectedGamepadDevice?.Connection.DevicePath;
        GamepadDevices.Clear();
        foreach (var connection in connections)
        {
            GamepadDevices.Add(new RazerGamepadDeviceRowViewModel(connection));
        }

        _selectedGamepadDevice = null;
        SelectedGamepad = null;
        var row = GamepadDevices.FirstOrDefault(candidate => previousPath is not null &&
            StringComparer.OrdinalIgnoreCase.Equals(candidate.Connection.DevicePath, previousPath))
            ?? GamepadDevices[0];
        await SelectGamepadAsync(row, cancellationToken);
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
            row.Connection,
            message => _diagnosticLog.TryWrite("gamepad", message),
            _getProfile,
            _saveProfile);
        await SelectedGamepad.LoadAsync(cancellationToken);
        row.SetCapabilityText(SelectedGamepad.CapabilityText);
    }
}
