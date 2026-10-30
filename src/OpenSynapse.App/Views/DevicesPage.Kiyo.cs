using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OpenSynapse.App.ViewModels;

namespace OpenSynapse.App.Views;

public sealed partial class DevicesPage : UserControl
{
    private async void KiyoRefreshClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.Kiyo.RefreshAsync(Lifetime));

    private async void KiyoApplyWhiteBalanceClick(object sender, RoutedEventArgs e) =>
        await ViewModel.Kiyo.ApplyWhiteBalanceAsync(Lifetime);

    private async void KiyoOpenSettingsClick(object sender, RoutedEventArgs e) =>
        await ViewModel.Kiyo.OpenSettingsAsync(Lifetime);
}
