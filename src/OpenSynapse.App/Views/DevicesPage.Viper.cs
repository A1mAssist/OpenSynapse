using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OpenSynapse.App.ViewModels;

namespace OpenSynapse.App.Views;

public sealed partial class DevicesPage : UserControl
{
    private async void ApplyPollingRateClick(object sender, RoutedEventArgs e) =>
        await ViewModel.Viper.ApplyPollingRateAsync(Lifetime);

    private async void ApplyDpiClick(object sender, RoutedEventArgs e) =>
        await ViewModel.Viper.ApplyDpiAsync(Lifetime);

    private async void ApplyIdleClick(object sender, RoutedEventArgs e) =>
        await ViewModel.Viper.ApplyIdleAsync(Lifetime);

    private async void ApplyLowBatteryThresholdClick(object sender, RoutedEventArgs e) =>
        await ViewModel.Viper.ApplyLowBatteryThresholdAsync(Lifetime);

    private async void ApplyBatteryChemistryClick(object sender, RoutedEventArgs e) =>
        await ViewModel.Viper.ApplyBatteryChemistryAsync(Lifetime);

    private async void ApplyDpiStagesClick(object sender, RoutedEventArgs e) =>
        await ViewModel.Viper.ApplyDpiStagesAsync(Lifetime);

}
