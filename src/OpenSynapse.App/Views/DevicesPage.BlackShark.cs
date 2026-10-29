using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace OpenSynapse.App.Views;

public sealed partial class DevicesPage : UserControl
{
    private async void BlackSharkApplySidetoneClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.SelectedBlackShark?.SetSidetoneAsync(Lifetime) ?? Task.CompletedTask);

    private async void BlackSharkApplyThxClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.SelectedBlackShark?.SetThxAsync(Lifetime) ?? Task.CompletedTask);

    private async void BlackSharkApplyAncClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.SelectedBlackShark?.SetAncAsync(Lifetime) ?? Task.CompletedTask);

    private async void BlackSharkApplyGameChatClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.SelectedBlackShark?.SetGameChatAsync(Lifetime) ?? Task.CompletedTask);

    private async void BlackSharkRefreshClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.SelectedBlackShark?.LoadAsync(Lifetime) ?? Task.CompletedTask);
}
