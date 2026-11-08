using Microsoft.UI.Xaml;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using Windows.UI;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHidDeviceViewModel
{
    private Color _wristRestColor = DefaultPrimaryColor;
    private bool _wristRestEnabled = true;
    private bool _isWristRestBusy;

    public bool HasWristRestLighting => RazerHidWristRestProtocol.Supports(Connection.Definition);
    public Visibility WristRestVisibility => VisibleWhen(HasWristRestLighting);
    public Color WristRestColor
    {
        get => _wristRestColor;
        set => SetField(ref _wristRestColor, value);
    }
    public bool WristRestEnabled
    {
        get => _wristRestEnabled;
        set => SetField(ref _wristRestEnabled, value);
    }
    public bool IsWristRestBusy
    {
        get => _isWristRestBusy;
        private set => SetBusy(ref _isWristRestBusy, value, nameof(CanApplyWristRest));
    }
    public bool CanApplyWristRest =>
        CanWrite && HasWristRestLighting &&
        Has(RazerHidBackendCapability.MatrixFrameWrite) && !IsWristRestBusy;

    [ProfileSetting("keyboard.wrist-rest", DeviceCategory.Keyboard)]
    public Task ApplyWristRestAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(
            RazerHidBackendCapability.MatrixFrameWrite,
            () => IsWristRestBusy,
            value => IsWristRestBusy = value,
            async () =>
            {
                var previousEnabled = WristRestEnabled;
                var previousColor = WristRestColor;
                var previousProfile = ReadProfile(settings => settings.Keyboard.WristRestLightingEnabled);
                var color = WristRestEnabled ? ToRazerHidColor(WristRestColor) : default;
                await _service.SetWristRestColorAsync(Connection, color, cancellationToken);
                await SaveProfileAsync(
                    settings => settings.Keyboard.WristRestLightingEnabled = WristRestEnabled,
                    settings => settings.Keyboard.WristRestLightingEnabled = previousProfile,
                    () =>
                    {
                        WristRestEnabled = previousEnabled;
                        WristRestColor = previousColor;
                    },
                    token => _service.SetWristRestColorAsync(
                        Connection,
                        previousEnabled ? ToRazerHidColor(previousColor) : default,
                        token),
                    cancellationToken);
            },
            cancellationToken);
}
