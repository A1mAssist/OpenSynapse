using Microsoft.UI.Xaml;

namespace OpenSynapse.App;

public sealed partial class MainWindow
{
    private async void BlackSharkApplySidetoneClick(object sender, RoutedEventArgs e) =>
        await (_viewModel.SelectedBlackShark?.SetSidetoneAsync(_lifetime.Token) ?? Task.CompletedTask);

    private async void BlackSharkApplyThxClick(object sender, RoutedEventArgs e) =>
        await (_viewModel.SelectedBlackShark?.SetThxAsync(_lifetime.Token) ?? Task.CompletedTask);

    private async void BlackSharkApplyAncClick(object sender, RoutedEventArgs e) =>
        await (_viewModel.SelectedBlackShark?.SetAncAsync(_lifetime.Token) ?? Task.CompletedTask);

    private async void BlackSharkApplyGameChatClick(object sender, RoutedEventArgs e) =>
        await (_viewModel.SelectedBlackShark?.SetGameChatAsync(_lifetime.Token) ?? Task.CompletedTask);

    private async void BlackSharkRefreshClick(object sender, RoutedEventArgs e) =>
        await (_viewModel.SelectedBlackShark?.LoadAsync(_lifetime.Token) ?? Task.CompletedTask);
}
