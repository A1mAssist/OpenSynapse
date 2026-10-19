using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OpenSynapse.App.ViewModels;
using Windows.System;

namespace OpenSynapse.App;

public sealed partial class ThxAudioView : UserControl
{
    public ThxAudioView()
    {
        InitializeComponent();
        Loaded += (_, _) => Localized.RefreshTree(this);
    }

    private ThxAudioViewModel ViewModel => (ThxAudioViewModel)DataContext;

    private async void RefreshThxAudioClick(object sender, RoutedEventArgs e) =>
        await ViewModel.RefreshAsync();

    private async void ApplyThxEqualizerClick(object sender, RoutedEventArgs e) =>
        await ViewModel.ApplyEqualizerAsync();

    private void ResetThxEqualizerClick(object sender, RoutedEventArgs e) =>
        ViewModel.ResetEqualizerToFactory();

    private void FlattenThxEqualizerClick(object sender, RoutedEventArgs e) =>
        ViewModel.FlattenEqualizer();

    private async void ApplyThxVoiceClarityClick(object sender, RoutedEventArgs e) =>
        await ViewModel.ApplyVoiceClarityAsync();

    private async void ApplyThxSoundNormalizationClick(object sender, RoutedEventArgs e) =>
        await ViewModel.ApplySoundNormalizationAsync();

    private async void DownloadThxComponentClick(object sender, RoutedEventArgs e) =>
        await ViewModel.InstallNativeComponentAsync();

    private async void OpenThxInstallClick(object sender, RoutedEventArgs e) =>
        await Launcher.LaunchUriAsync(new Uri("https://www.razer.com/synapse-4"));
}