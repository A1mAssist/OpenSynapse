using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OpenSynapse.App.ViewModels;
using Windows.System;

namespace OpenSynapse.App.Views.DevicePanels;

public sealed partial class HeadsetAudioPanel : UserControl
{
    public HeadsetAudioPanel()
    {
        InitializeComponent();
    }

    internal CancellationToken Lifetime { get; private set; } = CancellationToken.None;

    private UsbAccessoriesWorkspace? Accessories { get; set; }

    private RazerHeadsetAudioViewModel? ViewModel => DataContext as RazerHeadsetAudioViewModel;

    internal void Attach(UsbAccessoriesWorkspace accessories, CancellationToken lifetime)
    {
        Accessories = accessories;
        Lifetime = lifetime;
    }

    private async void HeadsetAudioRefreshClick(object sender, RoutedEventArgs e) =>
        await (Accessories?.RefreshSelectedHeadsetAudioAsync(Lifetime) ?? Task.CompletedTask);

    private async void HeadsetAudioInstallThxClick(object sender, RoutedEventArgs e) =>
        await (ViewModel?.InstallThxComponentsAsync(Lifetime) ?? Task.CompletedTask);

    private async void HeadsetAudioOpenThxGameProfilesClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { ThxGameProfilesUri: { Length: > 0 } uri })
        {
            await Launcher.LaunchUriAsync(new Uri(uri));
        }
    }

    private async void HeadsetAudioOpenThxInstallerClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { ThxInstallerUri: { Length: > 0 } uri })
        {
            await Launcher.LaunchUriAsync(new Uri(uri));
        }
    }

    private async void HeadsetAudioOpenThxPatcherClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { ThxPatcherUri: { Length: > 0 } uri })
        {
            await Launcher.LaunchUriAsync(new Uri(uri));
        }
    }

    private async void HeadsetAudioApplyEqPresetClick(object sender, RoutedEventArgs e) =>
        await (ViewModel?.ApplyEqPresetAsync(Lifetime) ?? Task.CompletedTask);

    private async void HeadsetAudioApplyEqBandsClick(object sender, RoutedEventArgs e) =>
        await (ViewModel?.ApplyEqBandsAsync(Lifetime) ?? Task.CompletedTask);

    private async void HeadsetAudioApplyMicEqBandsClick(object sender, RoutedEventArgs e) =>
        await (ViewModel?.ApplyMicEqBandsAsync(Lifetime) ?? Task.CompletedTask);

    private async void HeadsetAudioApplyBassClick(object sender, RoutedEventArgs e) =>
        await (ViewModel?.ApplyBassAsync(Lifetime) ?? Task.CompletedTask);

    private async void HeadsetAudioApplyMicClick(object sender, RoutedEventArgs e) =>
        await (ViewModel?.ApplyMicrophoneAsync(Lifetime) ?? Task.CompletedTask);

    private async void HeadsetAudioApplySourceClick(object sender, RoutedEventArgs e) =>
        await (ViewModel?.ApplySourceAsync(Lifetime) ?? Task.CompletedTask);

    private async void HeadsetAudioApplyNoiseClick(object sender, RoutedEventArgs e) =>
        await (ViewModel?.ApplyNoiseSuppressionAsync(Lifetime) ?? Task.CompletedTask);

    private async void HeadsetAudioApplyAncClick(object sender, RoutedEventArgs e) =>
        await (ViewModel?.ApplyAncAsync(Lifetime) ?? Task.CompletedTask);

    private async void HeadsetAudioApplyAdvancedClick(object sender, RoutedEventArgs e) =>
        await (ViewModel?.ApplyAdvancedAsync(Lifetime) ?? Task.CompletedTask);

    private async void HeadsetAudioApplyMicProcessingClick(object sender, RoutedEventArgs e) =>
        await (ViewModel?.ApplyMicProcessingAsync(Lifetime) ?? Task.CompletedTask);

    private async void HeadsetAudioApplyHapticsClick(object sender, RoutedEventArgs e) =>
        await (ViewModel?.ApplyHapticsAsync(Lifetime) ?? Task.CompletedTask);

    private async void HeadsetAudioApplyCompressorClick(object sender, RoutedEventArgs e) =>
        await (ViewModel?.ApplyMicCompressorAsync(Lifetime) ?? Task.CompletedTask);

    private async void HeadsetAudioApplyMicMonitoringClick(object sender, RoutedEventArgs e) =>
        await (ViewModel?.ApplyMicMonitoringAsync(Lifetime) ?? Task.CompletedTask);

    private async void HeadsetAudioLoadParametricEqClick(object sender, RoutedEventArgs e) =>
        await (ViewModel?.LoadParametricEqAsync(Lifetime) ?? Task.CompletedTask);

    private async void HeadsetAudioApplyParametricEqClick(object sender, RoutedEventArgs e) =>
        await (ViewModel?.ApplyParametricEqAsync(Lifetime) ?? Task.CompletedTask);

    private async void HeadsetAudioApplyMatrixBrightnessClick(object sender, RoutedEventArgs e) =>
        await (ViewModel?.ApplyMatrixBrightnessAsync(Lifetime) ?? Task.CompletedTask);

    private async void HeadsetAudioApplyMatrixLightingClick(object sender, RoutedEventArgs e) =>
        await (ViewModel?.ApplyMatrixLightingAsync(Lifetime) ?? Task.CompletedTask);

    private async void HeadsetAudioApplyAutoSleepClick(object sender, RoutedEventArgs e) =>
        await (ViewModel?.ApplyAutoSleepAsync(Lifetime) ?? Task.CompletedTask);

    private async void HeadsetAudioApplyLightingClick(object sender, RoutedEventArgs e) =>
        await (ViewModel?.Lighting?.ApplyAsync(Lifetime) ?? Task.CompletedTask);

    private async void HeadsetAudioApplyThxSpatialClick(object sender, RoutedEventArgs e) =>
        await (ViewModel?.ApplyThxSpatialAsync(Lifetime) ?? Task.CompletedTask);

    private async void HeadsetAudioApplyThxPresetClick(object sender, RoutedEventArgs e) =>
        await (ViewModel?.ApplyThxPresetAsync(Lifetime) ?? Task.CompletedTask);

    private async void HeadsetAudioApplyThxBandsClick(object sender, RoutedEventArgs e) =>
        await (ViewModel?.ApplyThxBandsAsync(Lifetime) ?? Task.CompletedTask);

    private async void HeadsetAudioApplyThxS3BaselineClick(object sender, RoutedEventArgs e) =>
        await (ViewModel?.ApplyS3BaselineAsync(Lifetime) ?? Task.CompletedTask);

    private async void HeadsetAudioApplyThxNormalizationClick(object sender, RoutedEventArgs e) =>
        await (ViewModel?.ApplyThxNormalizationAsync(Lifetime) ?? Task.CompletedTask);

    private async void HeadsetAudioApplyThxVoiceClarityClick(object sender, RoutedEventArgs e) =>
        await (ViewModel?.ApplyThxVoiceClarityAsync(Lifetime) ?? Task.CompletedTask);

    private async void HeadsetAudioApplyThxBassBoostClick(object sender, RoutedEventArgs e) =>
        await (ViewModel?.ApplyThxBassBoostAsync(Lifetime) ?? Task.CompletedTask);

    private async void HeadsetAudioApplyThxSidetoneClick(object sender, RoutedEventArgs e) =>
        await (ViewModel?.ApplyThxSidetoneAsync(Lifetime) ?? Task.CompletedTask);

    private async void HeadsetAudioApplyThxCaptureNormalizationClick(object sender, RoutedEventArgs e) =>
        await (ViewModel?.ApplyThxCaptureNormalizationAsync(Lifetime) ?? Task.CompletedTask);

    private async void HeadsetAudioApplyThxCaptureVocalClarityClick(object sender, RoutedEventArgs e) =>
        await (ViewModel?.ApplyThxCaptureVocalClarityAsync(Lifetime) ?? Task.CompletedTask);

    private async void HeadsetAudioApplyThxCaptureNoiseReductionClick(object sender, RoutedEventArgs e) =>
        await (ViewModel?.ApplyThxCaptureNoiseReductionAsync(Lifetime) ?? Task.CompletedTask);

    private async void HeadsetAudioApplyThxCaptureVoiceGateClick(object sender, RoutedEventArgs e) =>
        await (ViewModel?.ApplyThxCaptureVoiceGateAsync(Lifetime) ?? Task.CompletedTask);

    private async void Mixer053EMute1Click(object sender, RoutedEventArgs e) =>
        await (ViewModel?.Mixer053E?.ToggleMute1Async(Lifetime) ?? Task.CompletedTask);

    private async void Mixer053EMute2Click(object sender, RoutedEventArgs e) =>
        await (ViewModel?.Mixer053E?.ToggleMute2Async(Lifetime) ?? Task.CompletedTask);

    private async void Mixer053EMute3Click(object sender, RoutedEventArgs e) =>
        await (ViewModel?.Mixer053E?.ToggleMute3Async(Lifetime) ?? Task.CompletedTask);

    private async void Mixer053EMute4Click(object sender, RoutedEventArgs e) =>
        await (ViewModel?.Mixer053E?.ToggleMute4Async(Lifetime) ?? Task.CompletedTask);

    private async void Mixer053EMicMuteClick(object sender, RoutedEventArgs e) =>
        await (ViewModel?.Mixer053E?.ToggleMicMuteAsync(Lifetime) ?? Task.CompletedTask);

    private async void Mixer053EApplyBrightnessClick(object sender, RoutedEventArgs e) =>
        await (ViewModel?.Mixer053E?.ApplyBrightnessAsync(Lifetime) ?? Task.CompletedTask);
}
