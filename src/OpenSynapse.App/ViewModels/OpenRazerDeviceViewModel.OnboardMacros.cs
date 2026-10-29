using OpenSynapse.Windows.Macros;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

public sealed partial class OpenRazerDeviceViewModel
{
    private OnboardMacroListViewModel? _onboard;

    public OnboardMacroListViewModel Onboard =>
        _onboard ??= new OnboardMacroListViewModel(
            RazerOnboardMacroDevices.Products.Contains(Connection.Definition.ProductId));

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
