using OpenSynapse.Windows.Macros;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerKeyboardViewModel
{
    private OnboardMacroListViewModel? _onboard;

    public OnboardMacroListViewModel Onboard =>
        _onboard ??= new OnboardMacroListViewModel(
            RazerOnboardMacroDevices.Keyboards.Contains(_connection.ProductId));

    public Task LoadOnboardMacrosAsync(CancellationToken cancellationToken = default) =>
        Onboard.LoadAsync(
            () => new RazerOnboardMacroService(
                new RazerFeatureTransport(),
                _connection.DevicePath,
                TimeSpan.FromMilliseconds(20),
                _diagnostic),
            cancellationToken);
}
