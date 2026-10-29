using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OpenSynapse.App.ViewModels;

namespace OpenSynapse.App.Views;

public sealed partial class DevicesPage : UserControl
{
    private async void HueSelectorClick(object sender, RoutedEventArgs e)
    {
        SelectDevice("hue");
        await ViewModel.Hue.EnsureLoadedAsync(Lifetime);
        UpdateDeviceSelector("hue");
    }

    private async void HueScanClick(object sender, RoutedEventArgs e) =>
        await ViewModel.Hue.ScanAsync(Lifetime);

    private async void HuePairClick(object sender, RoutedEventArgs e) =>
        await ViewModel.Hue.PairAsync(Lifetime);

    private async void HueRefreshClick(object sender, RoutedEventArgs e) =>
        await ViewModel.Hue.LoadLightsAsync(Lifetime);

    private async void HueApplyColorClick(object sender, RoutedEventArgs e) =>
        await ViewModel.Hue.ApplyColorAsync(Lifetime);

    private async void HueApplyLightClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: PhilipsHueLightRowViewModel row })
        {
            await ViewModel.Hue.ApplyLightAsync(row, Lifetime);
        }
    }
}
