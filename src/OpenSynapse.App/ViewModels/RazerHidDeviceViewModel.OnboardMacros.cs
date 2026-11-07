using OpenSynapse.Windows.Macros;
using OpenSynapse.Windows.Protocols;
using OpenSynapse.Windows.Devices;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHidDeviceViewModel
{
    private OnboardMacroListViewModel? _onboard;

    public OnboardMacroListViewModel Onboard =>
        _onboard ??= new OnboardMacroListViewModel(
            RazerOnboardMacroDevices.Products.Contains(Connection.Definition.ProductId),
            canWrite: Has(RazerHidBackendCapability.MacroStorageDataWrite));

    public Task LoadOnboardMacrosAsync(CancellationToken cancellationToken = default) =>
        Onboard.LoadAsync(CreateService, cancellationToken);

    public Task WriteOnboardMacroAsync(CancellationToken cancellationToken = default) =>
        Onboard.WriteSelectedAsync(CreateService, cancellationToken);

    private RazerOnboardMacroService CreateService() =>
        new(
            new RazerFeatureTransport(),
            Connection.DevicePath ??
                throw new InvalidOperationException("The device has no HID control path."),
            TimeSpan.FromMilliseconds(20));
}
