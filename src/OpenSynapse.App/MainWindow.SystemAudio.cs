using Microsoft.UI.Xaml;
using OpenSynapse.App.ViewModels;
using OpenSynapse.Windows.Audio;

namespace OpenSynapse.App;

public sealed partial class MainWindow
{
    internal SystemAudioViewModel SystemAudio { get; } =
        new(new WindowsSystemAudioController());

    private async void SystemAudioRefreshClick(object sender, RoutedEventArgs e) =>
        await SystemAudio.LoadAsync(_lifetime.Token);

    private async void SystemAudioApplyRenderClick(object sender, RoutedEventArgs e) =>
        await SystemAudio.ApplyRenderAsync(_lifetime.Token);

    private async void SystemAudioApplyCaptureClick(object sender, RoutedEventArgs e) =>
        await SystemAudio.ApplyCaptureAsync(_lifetime.Token);
}
