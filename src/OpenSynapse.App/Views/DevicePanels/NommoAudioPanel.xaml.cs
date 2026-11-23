using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OpenSynapse.App.ViewModels;

namespace OpenSynapse.App.Views.DevicePanels;

public sealed partial class NommoAudioPanel : UserControl
{
    private CancellationTokenSource? _lifetime;
    public NommoAudioPanel()
    {
        InitializeComponent();
        Loaded += (_, _) => _lifetime ??= new();
        Unloaded += (_, _) => { _lifetime?.Cancel(); _lifetime?.Dispose(); _lifetime = null; };
    }
    private async void RefreshClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is RazerSpeakerAccessoryViewModel vm && _lifetime is { } lifetime)
            await vm.RefreshAsync(lifetime.Token);
    }
    private async void ApplyClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is RazerSpeakerAccessoryViewModel vm && _lifetime is { } lifetime)
            await vm.ApplyAsync(lifetime.Token);
    }
}
