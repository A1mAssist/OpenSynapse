using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OpenSynapse.App.ViewModels;
using Windows.System;

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

    private async void HeadsetAudioInstallThxClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedHeadsetAudio is { ThxInstallerUri: { Length: > 0 } uri })
        {
            await Launcher.LaunchUriAsync(new Uri(uri));
        }
    }

    private async void HeadsetAudioOpenThxGameProfilesClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedHeadsetAudio is { ThxGameProfilesUri: { Length: > 0 } uri })
        {
            await Launcher.LaunchUriAsync(new Uri(uri));
        }
    }

    private async void HeadsetAudioOpenThxPatcherClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedHeadsetAudio is { ThxPatcherUri: { Length: > 0 } uri })
        {
            await Launcher.LaunchUriAsync(new Uri(uri));
        }
    }

    private async void HeadsetAudioDownloadThxClick(object sender, RoutedEventArgs e) =>
        await (_viewModel.SelectedHeadsetAudio?.InstallThxNativeAsync(_lifetime.Token) ?? Task.CompletedTask);

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

    private async void HeadsetAudioApplyMatrixBrightnessClick(object sender, RoutedEventArgs e) =>
        await (_viewModel.SelectedHeadsetAudio?.ApplyMatrixBrightnessAsync(_lifetime.Token) ?? Task.CompletedTask);

    private async void HeadsetAudioApplyMatrixLightingClick(object sender, RoutedEventArgs e) =>
        await (_viewModel.SelectedHeadsetAudio?.ApplyMatrixLightingAsync(_lifetime.Token) ?? Task.CompletedTask);

    private async void HeadsetAudioApplyLightingClick(object sender, RoutedEventArgs e) =>
        await (_viewModel.SelectedHeadsetAudio?.Lighting?.ApplyAsync(_lifetime.Token) ?? Task.CompletedTask);

    private async void HeadsetAudioApplyThxSpatialClick(object sender, RoutedEventArgs e) =>
        await (_viewModel.SelectedHeadsetAudio?.ApplyThxSpatialAsync(_lifetime.Token) ?? Task.CompletedTask);

    private async void HeadsetAudioApplyThxPresetClick(object sender, RoutedEventArgs e) =>
        await (_viewModel.SelectedHeadsetAudio?.ApplyThxPresetAsync(_lifetime.Token) ?? Task.CompletedTask);

    private async void HeadsetAudioApplyThxBandsClick(object sender, RoutedEventArgs e) =>
        await (_viewModel.SelectedHeadsetAudio?.ApplyThxBandsAsync(_lifetime.Token) ?? Task.CompletedTask);

    private async void HeadsetAudioApplyThxNormalizationClick(object sender, RoutedEventArgs e) =>
        await (_viewModel.SelectedHeadsetAudio?.ApplyThxNormalizationAsync(_lifetime.Token) ?? Task.CompletedTask);

    private async void HeadsetAudioApplyThxVoiceClarityClick(object sender, RoutedEventArgs e) =>
        await (_viewModel.SelectedHeadsetAudio?.ApplyThxVoiceClarityAsync(_lifetime.Token) ?? Task.CompletedTask);

    private async void HeadsetAudioApplyThxSidetoneClick(object sender, RoutedEventArgs e) =>
        await (_viewModel.SelectedHeadsetAudio?.ApplyThxSidetoneAsync(_lifetime.Token) ?? Task.CompletedTask);
}
