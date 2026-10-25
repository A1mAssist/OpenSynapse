using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OpenSynapse.App.ViewModels;

namespace OpenSynapse.App;

public sealed partial class MainWindow
{
    private async void HeadsetAudioSelectorClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: RazerHeadsetAudioDeviceRowViewModel row })
        {
            return;
        }

        SelectDevice("headsetaudio");
        await _viewModel.SelectHeadsetAudioAsync(row, _lifetime.Token);
        UpdateDeviceSelector("headsetaudio");
    }

    private async void HeadsetAudioRefreshClick(object sender, RoutedEventArgs e) =>
        await (_viewModel.SelectedHeadsetAudio?.LoadAsync(_lifetime.Token) ?? Task.CompletedTask);

    private async void HeadsetAudioApplyEqPresetClick(object sender, RoutedEventArgs e) =>
        await (_viewModel.SelectedHeadsetAudio?.ApplyEqPresetAsync(_lifetime.Token) ?? Task.CompletedTask);

    private async void HeadsetAudioApplyEqBandsClick(object sender, RoutedEventArgs e) =>
        await (_viewModel.SelectedHeadsetAudio?.ApplyEqBandsAsync(_lifetime.Token) ?? Task.CompletedTask);

    private async void HeadsetAudioApplyBassClick(object sender, RoutedEventArgs e) =>
        await (_viewModel.SelectedHeadsetAudio?.ApplyBassAsync(_lifetime.Token) ?? Task.CompletedTask);

    private async void HeadsetAudioApplyMicClick(object sender, RoutedEventArgs e) =>
        await (_viewModel.SelectedHeadsetAudio?.ApplyMicrophoneAsync(_lifetime.Token) ?? Task.CompletedTask);

    private async void HeadsetAudioApplySourceClick(object sender, RoutedEventArgs e) =>
        await (_viewModel.SelectedHeadsetAudio?.ApplySourceAsync(_lifetime.Token) ?? Task.CompletedTask);

    private async void HeadsetAudioApplyNoiseClick(object sender, RoutedEventArgs e) =>
        await (_viewModel.SelectedHeadsetAudio?.ApplyNoiseSuppressionAsync(_lifetime.Token) ?? Task.CompletedTask);

    private async void HeadsetAudioApplyAncClick(object sender, RoutedEventArgs e) =>
        await (_viewModel.SelectedHeadsetAudio?.ApplyAncAsync(_lifetime.Token) ?? Task.CompletedTask);

    private async void HeadsetAudioApplyBrightnessClick(object sender, RoutedEventArgs e) =>
        await (_viewModel.SelectedHeadsetAudio?.ApplyBrightnessAsync(_lifetime.Token) ?? Task.CompletedTask);

    private async void HeadsetAudioApplyLightingClick(object sender, RoutedEventArgs e) =>
        await (_viewModel.SelectedHeadsetAudio?.Lighting?.ApplyAsync(_lifetime.Token) ?? Task.CompletedTask);
}
