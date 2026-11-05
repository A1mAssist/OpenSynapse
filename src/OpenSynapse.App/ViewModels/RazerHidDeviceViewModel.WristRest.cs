using Microsoft.UI.Xaml;
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

    public Task ApplyWristRestAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(
            RazerHidBackendCapability.MatrixFrameWrite,
            () => IsWristRestBusy,
            value => IsWristRestBusy = value,
            async () =>
            {
                var color = WristRestEnabled ? ToRazerHidColor(WristRestColor) : default;
                await _service.SetWristRestColorAsync(Connection, color, cancellationToken);
            },
            cancellationToken);
}
