using System.Collections.ObjectModel;
using OpenSynapse.Windows.Devices;

namespace OpenSynapse.App.ViewModels;

public sealed partial class MainViewModel
{
    private BlackSharkDeviceRowViewModel? _selectedBlackSharkDevice;
    private BlackSharkHeadsetViewModel? _selectedBlackShark;

    public ObservableCollection<BlackSharkDeviceRowViewModel> BlackSharkDevices { get; } = new();

    public BlackSharkHeadsetViewModel? SelectedBlackShark
    {
        get => _selectedBlackShark;
        private set => SetField(ref _selectedBlackShark, value);
    }

    private async Task RefreshBlackSharkAsync(CancellationToken cancellationToken)
    {
        BlackSharkHeadsetConnection? connection;
        try
        {
            connection = await BlackSharkHeadsetService.FindAsync(cancellationToken);
        }
        catch (Exception exception) when (IsExpectedRuntimeException(exception))
        {
            _diagnosticLog.TryWrite("blackshark", $"discovery failed: {exception}");
            return;
        }

        if (connection is null)
        {
            if (BlackSharkDevices.Count == 0 && SelectedBlackShark is null) return;
            BlackSharkDevices.Clear();
            _selectedBlackSharkDevice = null;
            SelectedBlackShark = null;
            return;
        }

        if (BlackSharkDevices.Count == 1 &&
            StringComparer.OrdinalIgnoreCase.Equals(
                BlackSharkDevices[0].Connection.DevicePath, connection.DevicePath) &&
            BlackSharkDevices[0].Connection.ProductId == connection.ProductId)
        {
            return;
        }

        BlackSharkDevices.Clear();
        BlackSharkDevices.Add(new BlackSharkDeviceRowViewModel(connection));
        if (_selectedBlackSharkDevice is not null &&
            StringComparer.OrdinalIgnoreCase.Equals(
                _selectedBlackSharkDevice.Connection.DevicePath, connection.DevicePath))
        {
            _selectedBlackSharkDevice = BlackSharkDevices[0];
        }
        if (_selectedBlackSharkDevice is null ||
            !StringComparer.OrdinalIgnoreCase.Equals(
                _selectedBlackSharkDevice.Connection.DevicePath, connection.DevicePath))
        {
            _selectedBlackSharkDevice = null;
            SelectedBlackShark = null;
        }
    }

    public async Task SelectBlackSharkAsync(
        BlackSharkDeviceRowViewModel row,
        CancellationToken cancellationToken = default)
    {
        if (_selectedBlackSharkDevice is not null &&
            SelectedBlackShark is not null &&
            StringComparer.OrdinalIgnoreCase.Equals(
                _selectedBlackSharkDevice.Connection.DevicePath, row.Connection.DevicePath))
        {
            return;
        }

        _selectedBlackSharkDevice = row;
        SelectedBlackShark = new BlackSharkHeadsetViewModel(row.Connection);
        await SelectedBlackShark.LoadAsync(cancellationToken);
    }
}
