using Microsoft.UI.Xaml;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHeadsetAudioViewModel
{
    private byte? _spatialMode;
    private int _selectedSpatialModeIndex;
    public Visibility SpatialModeVisibility => IsProtocol25 && _connection.ProductId == 0x0548
        ? Visibility.Visible : Visibility.Collapsed;
    public bool CanEditSpatialMode => CanEdit && _spatialMode is not null;
    public IReadOnlyList<string> SpatialModeOptions =>
    [
        AppStrings.Text("LeviathanModeStereo"), AppStrings.Text("LeviathanModeBinaural"),
        AppStrings.Text("LeviathanModeMultichannel"), AppStrings.Text("LeviathanModeRoomFill"),
    ];
    public int SelectedSpatialModeIndex
    {
        get => _selectedSpatialModeIndex;
        set { if (value is >= 0 and <= 3) SetField(ref _selectedSpatialModeIndex, value); }
    }

    private void ApplySpatialMode(byte mode)
    {
        _spatialMode = mode;
        SelectedSpatialModeIndex = mode;
        ConfirmAudioSetting(HeadsetAudioSetting.SpatialMode, (int)mode);
        OnPropertyChanged(nameof(CanEditSpatialMode));
    }

    [ProfileSetting("headset.spatial-mode", DeviceCategory.Headset, powerScoped: true)]
    public async Task ApplySpatialModeAsync(CancellationToken cancellationToken = default)
    {
        if (_spatialMode is not { } previous || _connection.ProductId != 0x0548) return;
        var requested = (byte)SelectedSpatialModeIndex;
        if (!await RunWriteAsync((service, token) => service.SetSpatialModeAsync(requested, token), cancellationToken))
        {
            ApplySpatialMode(previous);
            return;
        }
        ApplySpatialMode(requested);
        await SaveHeadsetSettingAsync(
            settings => settings.SpatialMode = requested,
            settings => settings.SpatialMode = previous,
            () => ApplySpatialMode(previous),
            token => OpenService().SetSpatialModeAsync(previous, token), cancellationToken);
    }
}
