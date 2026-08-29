using Microsoft.UI.Xaml;

namespace OpenSynapse.App;

public sealed partial class MainWindow
{
    private async void ApplyPollingRateClick(object sender, RoutedEventArgs e) =>
        await _viewModel.ApplyViperPollingRateAsync(_lifetime.Token);

    private async void ApplyDpiClick(object sender, RoutedEventArgs e) =>
        await _viewModel.ApplyViperDpiAsync(_lifetime.Token);

    private async void ApplyIdleClick(object sender, RoutedEventArgs e) =>
        await _viewModel.ApplyViperIdleAsync(_lifetime.Token);

    private async void ApplyBatteryChemistryClick(object sender, RoutedEventArgs e) =>
        await _viewModel.ApplyViperBatteryChemistryAsync(_lifetime.Token);

    private async void ApplyDpiStagesClick(object sender, RoutedEventArgs e) =>
        await _viewModel.ApplyViperDpiStagesAsync(_lifetime.Token);

    private async void ReadViperButtonMappingsClick(object sender, RoutedEventArgs e) =>
        await _viewModel.ReadViperButtonMappingsAsync(_lifetime.Token);

    private async void ApplyAllViperButtonMappingsClick(object sender, RoutedEventArgs e) =>
        await _viewModel.ApplyAllViperButtonMappingsAsync(_lifetime.Token);
}
