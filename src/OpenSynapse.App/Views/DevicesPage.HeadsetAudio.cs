using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OpenSynapse.App.ViewModels;
using Windows.System;

namespace OpenSynapse.App.Views;

public sealed partial class DevicesPage : UserControl
{
    private async void HeadsetAudioSelectorClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: RazerHeadsetAudioDeviceRowViewModel row })
        {
            return;
        }

        SelectDevice("headsetaudio");
        await ViewModel.Accessories.SelectHeadsetAudioAsync(row, Lifetime);
        UpdateDeviceSelector("headsetaudio");
    }

    private async void HeadsetAudioRefreshClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.Accessories.SelectedHeadsetAudio?.LoadAsync(Lifetime) ?? Task.CompletedTask);

    private async void HeadsetAudioInstallThxClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.Accessories.SelectedHeadsetAudio?.InstallThxComponentsAsync(Lifetime) ?? Task.CompletedTask);

    private async void HeadsetAudioOpenThxGameProfilesClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Accessories.SelectedHeadsetAudio is { ThxGameProfilesUri: { Length: > 0 } uri })
        {
            await Launcher.LaunchUriAsync(new Uri(uri));
        }
    }

    private async void HeadsetAudioOpenThxPatcherClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Accessories.SelectedHeadsetAudio is { ThxPatcherUri: { Length: > 0 } uri })
        {
            await Launcher.LaunchUriAsync(new Uri(uri));
        }
    }

    private async void HeadsetAudioDownloadThxClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.Accessories.SelectedHeadsetAudio?.InstallThxNativeAsync(Lifetime) ?? Task.CompletedTask);

    private async void HeadsetAudioApplyEqPresetClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.Accessories.SelectedHeadsetAudio?.ApplyEqPresetAsync(Lifetime) ?? Task.CompletedTask);

    private async void HeadsetAudioApplyEqBandsClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.Accessories.SelectedHeadsetAudio?.ApplyEqBandsAsync(Lifetime) ?? Task.CompletedTask);

    private async void HeadsetAudioApplyMicEqBandsClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.Accessories.SelectedHeadsetAudio?.ApplyMicEqBandsAsync(Lifetime) ?? Task.CompletedTask);

    private async void HeadsetAudioApplyBassClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.Accessories.SelectedHeadsetAudio?.ApplyBassAsync(Lifetime) ?? Task.CompletedTask);

    private async void HeadsetAudioApplyMicClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.Accessories.SelectedHeadsetAudio?.ApplyMicrophoneAsync(Lifetime) ?? Task.CompletedTask);

    private async void HeadsetAudioApplySourceClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.Accessories.SelectedHeadsetAudio?.ApplySourceAsync(Lifetime) ?? Task.CompletedTask);

    private async void HeadsetAudioApplyNoiseClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.Accessories.SelectedHeadsetAudio?.ApplyNoiseSuppressionAsync(Lifetime) ?? Task.CompletedTask);

    private async void HeadsetAudioApplyAncClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.Accessories.SelectedHeadsetAudio?.ApplyAncAsync(Lifetime) ?? Task.CompletedTask);

    private async void HeadsetAudioApplyAdvancedClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.Accessories.SelectedHeadsetAudio?.ApplyAdvancedAsync(Lifetime) ?? Task.CompletedTask);

    private async void HeadsetAudioApplyMicProcessingClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.Accessories.SelectedHeadsetAudio?.ApplyMicProcessingAsync(Lifetime) ?? Task.CompletedTask);

    private async void HeadsetAudioApplyHapticsClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.Accessories.SelectedHeadsetAudio?.ApplyHapticsAsync(Lifetime) ?? Task.CompletedTask);

    private async void HeadsetAudioApplyCompressorClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.Accessories.SelectedHeadsetAudio?.ApplyMicCompressorAsync(Lifetime) ?? Task.CompletedTask);

    private async void HeadsetAudioApplyMicMonitoringClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.Accessories.SelectedHeadsetAudio?.ApplyMicMonitoringAsync(Lifetime) ?? Task.CompletedTask);

    private async void HeadsetAudioLoadParametricEqClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.Accessories.SelectedHeadsetAudio?.LoadParametricEqAsync(Lifetime) ?? Task.CompletedTask);

    private async void HeadsetAudioApplyParametricEqClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.Accessories.SelectedHeadsetAudio?.ApplyParametricEqAsync(Lifetime) ?? Task.CompletedTask);

    private async void HeadsetAudioApplyMatrixBrightnessClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.Accessories.SelectedHeadsetAudio?.ApplyMatrixBrightnessAsync(Lifetime) ?? Task.CompletedTask);

    private async void HeadsetAudioApplyMatrixLightingClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.Accessories.SelectedHeadsetAudio?.ApplyMatrixLightingAsync(Lifetime) ?? Task.CompletedTask);

    private async void HeadsetAudioApplyLightingClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.Accessories.SelectedHeadsetAudio?.Lighting?.ApplyAsync(Lifetime) ?? Task.CompletedTask);

    private async void HeadsetAudioApplyThxSpatialClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.Accessories.SelectedHeadsetAudio?.ApplyThxSpatialAsync(Lifetime) ?? Task.CompletedTask);

    private async void HeadsetAudioApplyThxPresetClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.Accessories.SelectedHeadsetAudio?.ApplyThxPresetAsync(Lifetime) ?? Task.CompletedTask);

    private async void HeadsetAudioApplyThxBandsClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.Accessories.SelectedHeadsetAudio?.ApplyThxBandsAsync(Lifetime) ?? Task.CompletedTask);

    private async void HeadsetAudioApplyThxNormalizationClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.Accessories.SelectedHeadsetAudio?.ApplyThxNormalizationAsync(Lifetime) ?? Task.CompletedTask);

    private async void HeadsetAudioApplyThxVoiceClarityClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.Accessories.SelectedHeadsetAudio?.ApplyThxVoiceClarityAsync(Lifetime) ?? Task.CompletedTask);

    private async void HeadsetAudioApplyThxSidetoneClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.Accessories.SelectedHeadsetAudio?.ApplyThxSidetoneAsync(Lifetime) ?? Task.CompletedTask);
}
