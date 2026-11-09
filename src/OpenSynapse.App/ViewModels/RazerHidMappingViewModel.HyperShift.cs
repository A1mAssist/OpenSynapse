using Microsoft.UI.Xaml;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHidMappingViewModel
{
    public Visibility HyperShiftSoftwareUnavailableVisibility =>
        LayerIndex == 1 && !_canObserveHyperShiftLayer
            ? Visibility.Visible
            : Visibility.Collapsed;

    public string HyperShiftSoftwareUnavailableText =>
        AppStrings.Text("RazerMappingHyperShiftSoftwareUnavailable");
}
