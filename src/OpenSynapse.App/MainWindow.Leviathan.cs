using Microsoft.UI.Xaml;

namespace OpenSynapse.App;

public sealed partial class MainWindow
{
    private async void LeviathanRefreshClick(object sender, RoutedEventArgs e) =>
        await (_viewModel.SelectedOpenRazerDevice?.Soundbar?.LoadAsync(_lifetime.Token)
            ?? Task.CompletedTask);

    private async void LeviathanApplyEqPresetClick(object sender, RoutedEventArgs e) =>
        await (_viewModel.SelectedOpenRazerDevice?.Soundbar?.ApplyEqPresetAsync(_lifetime.Token)
            ?? Task.CompletedTask);

    private async void LeviathanApplyBassClick(object sender, RoutedEventArgs e) =>
        await (_viewModel.SelectedOpenRazerDevice?.Soundbar?.ApplyBassAsync(_lifetime.Token)
            ?? Task.CompletedTask);

    private async void LeviathanApplyEqBandsClick(object sender, RoutedEventArgs e) =>
        await (_viewModel.SelectedOpenRazerDevice?.Soundbar?.ApplyEqBandsAsync(_lifetime.Token)
            ?? Task.CompletedTask);
}
