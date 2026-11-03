using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace OpenSynapse.App.Views;

public sealed partial class DevicesPage : UserControl
{
    private async void LeviathanRefreshClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.RazerHid.SelectedDevice?.Soundbar?.LoadAsync(Lifetime)
            ?? Task.CompletedTask);

    private async void LeviathanApplyEqPresetClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.RazerHid.SelectedDevice?.Soundbar?.ApplyEqPresetAsync(Lifetime)
            ?? Task.CompletedTask);

    private async void LeviathanApplyBassClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.RazerHid.SelectedDevice?.Soundbar?.ApplyBassAsync(Lifetime)
            ?? Task.CompletedTask);

    private async void LeviathanApplyEqBandsClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.RazerHid.SelectedDevice?.Soundbar?.ApplyEqBandsAsync(Lifetime)
            ?? Task.CompletedTask);
}
