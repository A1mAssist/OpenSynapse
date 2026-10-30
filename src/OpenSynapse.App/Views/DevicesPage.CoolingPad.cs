using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OpenSynapse.App.ViewModels;

namespace OpenSynapse.App.Views;

public sealed partial class DevicesPage : UserControl
{
    private async void CoolingPadRefreshClick(object sender, RoutedEventArgs e)
    {
        await ViewModel.CoolingPad.RefreshAsync(Lifetime);
        if (!ViewModel.CoolingPad.IsConnected)
        {
            SelectDevice("none");
        }
    }

    private async void CoolingPadApplyClick(object sender, RoutedEventArgs e) =>
        await ViewModel.CoolingPad.ApplyAsync(Lifetime);

    private async void CoolingPadApplyLightingClick(object sender, RoutedEventArgs e) =>
        await ViewModel.CoolingPad.ApplyLightingAsync(Lifetime);

    private async void CoolingPadApplyFixedModeClick(object sender, RoutedEventArgs e) =>
        await ViewModel.CoolingPad.ApplyFixedModeAsync(Lifetime);

    private async void CoolingPadApplyCurveClick(object sender, RoutedEventArgs e) =>
        await ViewModel.CoolingPad.ApplyCurveAsync(Lifetime);
}
