using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace OpenSynapse.App.Views;

public sealed partial class DevicesPage : UserControl
{
    private async void Mixer053EMute1Click(object sender, RoutedEventArgs e) =>
        await (ViewModel.SelectedHeadsetAudio?.Mixer053E?.ToggleMute1Async(Lifetime) ?? Task.CompletedTask);

    private async void Mixer053EMute2Click(object sender, RoutedEventArgs e) =>
        await (ViewModel.SelectedHeadsetAudio?.Mixer053E?.ToggleMute2Async(Lifetime) ?? Task.CompletedTask);

    private async void Mixer053EMute3Click(object sender, RoutedEventArgs e) =>
        await (ViewModel.SelectedHeadsetAudio?.Mixer053E?.ToggleMute3Async(Lifetime) ?? Task.CompletedTask);

    private async void Mixer053EMute4Click(object sender, RoutedEventArgs e) =>
        await (ViewModel.SelectedHeadsetAudio?.Mixer053E?.ToggleMute4Async(Lifetime) ?? Task.CompletedTask);

    private async void Mixer053EMicMuteClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.SelectedHeadsetAudio?.Mixer053E?.ToggleMicMuteAsync(Lifetime) ?? Task.CompletedTask);

    private async void Mixer053EApplyBrightnessClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.SelectedHeadsetAudio?.Mixer053E?.ApplyBrightnessAsync(Lifetime) ?? Task.CompletedTask);
}