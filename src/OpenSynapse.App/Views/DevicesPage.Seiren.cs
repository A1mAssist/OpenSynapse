using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OpenSynapse.App.ViewModels;

namespace OpenSynapse.App.Views;

public sealed partial class DevicesPage : UserControl
{
    private async void SeirenEmoteApplyClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.SelectedHeadsetAudio?.SeirenEmote?.ApplyAsync(Lifetime) ?? Task.CompletedTask);

    private async void SeirenEmoteClearClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.SelectedHeadsetAudio?.SeirenEmote?.ClearAsync(Lifetime) ?? Task.CompletedTask);
}
