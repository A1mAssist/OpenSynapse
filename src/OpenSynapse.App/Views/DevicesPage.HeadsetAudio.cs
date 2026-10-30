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
        await ViewModel.SelectHeadsetAudioAsync(row, Lifetime);
        UpdateDeviceSelector("headsetaudio");
    }

    private async void HeadsetAudioRefreshClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.SelectedHeadsetAudio?.LoadAsync(Lifetime) ?? Task.CompletedTask);

    private async void HeadsetAudioInstallThxClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedHeadsetAudio is { ThxInstallerUri: { Length: > 0 } uri })
        {
            await Launcher.LaunchUriAsync(new Uri(uri));
        }
    }

    private async void HeadsetAudioOpenThxGameProfilesClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedHeadsetAudio is { ThxGameProfilesUri: { Length: > 0 } uri })
        {
            await Launcher.LaunchUriAsync(new Uri(uri));
        }
    }

    private async void HeadsetAudioOpenThxPatcherClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedHeadsetAudio is { ThxPatcherUri: { Length: > 0 } uri })
        {
            await Launcher.LaunchUriAsync(new Uri(uri));
        }
    }

    private async void HeadsetAudioDownloadThxClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.SelectedHeadsetAudio?.InstallThxNativeAsync(Lifetime) ?? Task.CompletedTask);

    private async void HeadsetAudioApplyEqPresetClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.SelectedHeadsetAudio?.ApplyEqPresetAsync(Lifetime) ?? Task.CompletedTask);

    private async void HeadsetAudioApplyEqBandsClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.SelectedHeadsetAudio?.ApplyEqBandsAsync(Lifetime) ?? Task.CompletedTask);

    private async void HeadsetAudioApplyMicEqBandsClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.SelectedHeadsetAudio?.ApplyMicEqBandsAsync(Lifetime) ?? Task.CompletedTask);

    private async void HeadsetAudioApplyBassClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.SelectedHeadsetAudio?.ApplyBassAsync(Lifetime) ?? Task.CompletedTask);

    private async void HeadsetAudioApplyMicClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.SelectedHeadsetAudio?.ApplyMicrophoneAsync(Lifetime) ?? Task.CompletedTask);

    private async void HeadsetAudioApplySourceClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.SelectedHeadsetAudio?.ApplySourceAsync(Lifetime) ?? Task.CompletedTask);

    private async void HeadsetAudioApplyNoiseClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.SelectedHeadsetAudio?.ApplyNoiseSuppressionAsync(Lifetime) ?? Task.CompletedTask);

    private async void HeadsetAudioApplyAncClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.SelectedHeadsetAudio?.ApplyAncAsync(Lifetime) ?? Task.CompletedTask);

    private async void HeadsetAudioApplyAdvancedClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.SelectedHeadsetAudio?.ApplyAdvancedAsync(Lifetime) ?? Task.CompletedTask);

    private async void HeadsetAudioApplyMicProcessingClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.SelectedHeadsetAudio?.ApplyMicProcessingAsync(Lifetime) ?? Task.CompletedTask);

    private async void HeadsetAudioApplyHapticsClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.SelectedHeadsetAudio?.ApplyHapticsAsync(Lifetime) ?? Task.CompletedTask);

    private async void HeadsetAudioApplyCompressorClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.SelectedHeadsetAudio?.ApplyMicCompressorAsync(Lifetime) ?? Task.CompletedTask);

    private async void HeadsetAudioApplyMicMonitoringClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.SelectedHeadsetAudio?.ApplyMicMonitoringAsync(Lifetime) ?? Task.CompletedTask);

    private async void HeadsetAudioLoadParametricEqClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.SelectedHeadsetAudio?.LoadParametricEqAsync(Lifetime) ?? Task.CompletedTask);

    private async void HeadsetAudioApplyParametricEqClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.SelectedHeadsetAudio?.ApplyParametricEqAsync(Lifetime) ?? Task.CompletedTask);

    private async void HeadsetAudioApplyMatrixBrightnessClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.SelectedHeadsetAudio?.ApplyMatrixBrightnessAsync(Lifetime) ?? Task.CompletedTask);

    private async void HeadsetAudioApplyMatrixLightingClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.SelectedHeadsetAudio?.ApplyMatrixLightingAsync(Lifetime) ?? Task.CompletedTask);

    private async void HeadsetAudioApplyLightingClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.SelectedHeadsetAudio?.Lighting?.ApplyAsync(Lifetime) ?? Task.CompletedTask);

    private async void HeadsetAudioApplyThxSpatialClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.SelectedHeadsetAudio?.ApplyThxSpatialAsync(Lifetime) ?? Task.CompletedTask);

    private async void HeadsetAudioApplyThxPresetClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.SelectedHeadsetAudio?.ApplyThxPresetAsync(Lifetime) ?? Task.CompletedTask);

    private async void HeadsetAudioApplyThxBandsClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.SelectedHeadsetAudio?.ApplyThxBandsAsync(Lifetime) ?? Task.CompletedTask);

    private async void HeadsetAudioApplyThxNormalizationClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.SelectedHeadsetAudio?.ApplyThxNormalizationAsync(Lifetime) ?? Task.CompletedTask);

    private async void HeadsetAudioApplyThxVoiceClarityClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.SelectedHeadsetAudio?.ApplyThxVoiceClarityAsync(Lifetime) ?? Task.CompletedTask);

    private async void HeadsetAudioApplyThxSidetoneClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.SelectedHeadsetAudio?.ApplyThxSidetoneAsync(Lifetime) ?? Task.CompletedTask);
}
