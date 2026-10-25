using System.Collections.ObjectModel;
using OpenSynapse.Windows.Devices;

namespace OpenSynapse.App.ViewModels;

public sealed partial class MainViewModel
{
    private RazerHeadsetAudioDeviceRowViewModel? _selectedHeadsetAudioDevice;
    private RazerHeadsetAudioViewModel? _selectedHeadsetAudio;

    public ObservableCollection<RazerHeadsetAudioDeviceRowViewModel> HeadsetAudioDevices { get; } = new();

    public RazerHeadsetAudioViewModel? SelectedHeadsetAudio
    {
        get => _selectedHeadsetAudio;
        private set => SetField(ref _selectedHeadsetAudio, value);
    }

    private async Task RefreshHeadsetAudioAsync(CancellationToken cancellationToken)
    {
        RazerHeadsetAudioConnection? connection;
        try
        {
            connection = await RazerHeadsetAudioService.FindAsync(
                message => _diagnosticLog.TryWrite("headset-audio", message), cancellationToken);
        }
        catch (Exception exception) when (IsExpectedRuntimeException(exception))
        {
            _diagnosticLog.TryWrite("headset-audio", $"discovery failed: {exception}");
            return;
        }

        if (connection is null)
        {
            if (HeadsetAudioDevices.Count == 0 && SelectedHeadsetAudio is null) return;
            HeadsetAudioDevices.Clear();
            _selectedHeadsetAudioDevice = null;
            SelectedHeadsetAudio = null;
            return;
        }

        if (HeadsetAudioDevices.Count == 1 &&
            StringComparer.OrdinalIgnoreCase.Equals(
                HeadsetAudioDevices[0].Connection.DevicePath, connection.DevicePath) &&
            HeadsetAudioDevices[0].Connection.ProductId == connection.ProductId)
        {
            return;
        }

        HeadsetAudioDevices.Clear();
        HeadsetAudioDevices.Add(new RazerHeadsetAudioDeviceRowViewModel(connection));
        if (_selectedHeadsetAudioDevice is null ||
            !StringComparer.OrdinalIgnoreCase.Equals(
                _selectedHeadsetAudioDevice.Connection.DevicePath, connection.DevicePath))
        {
            _selectedHeadsetAudioDevice = null;
            SelectedHeadsetAudio = null;
        }
    }

    public async Task SelectHeadsetAudioAsync(
        RazerHeadsetAudioDeviceRowViewModel row,
        CancellationToken cancellationToken = default)
    {
        if (_selectedHeadsetAudioDevice is not null &&
            SelectedHeadsetAudio is not null &&
            StringComparer.OrdinalIgnoreCase.Equals(
                _selectedHeadsetAudioDevice.Connection.DevicePath, row.Connection.DevicePath))
        {
            return;
        }

        _selectedHeadsetAudioDevice = row;
        SelectedHeadsetAudio = new RazerHeadsetAudioViewModel(
            row.Connection, message => _diagnosticLog.TryWrite("headset-audio", message));
        await SelectedHeadsetAudio.LoadAsync(cancellationToken);
    }
}
