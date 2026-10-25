using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OpenSynapse.App.ViewModels;

namespace OpenSynapse.App;

public sealed partial class MainWindow
{
    private async void HuePairClick(object sender, RoutedEventArgs e) =>
        await _viewModel.Hue.PairAsync(_lifetime.Token);

    private async void HueRefreshClick(object sender, RoutedEventArgs e) =>
        await _viewModel.Hue.LoadLightsAsync(_lifetime.Token);

    private async void HueApplyColorClick(object sender, RoutedEventArgs e) =>
        await _viewModel.Hue.ApplyColorAsync(_lifetime.Token);

    private async void HueApplyLightClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: PhilipsHueLightRowViewModel row })
        {
            await _viewModel.Hue.ApplyLightAsync(row, _lifetime.Token);
        }
    }
}
