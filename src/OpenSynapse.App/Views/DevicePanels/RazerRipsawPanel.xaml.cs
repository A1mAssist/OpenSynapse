using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OpenSynapse.App.ViewModels;

namespace OpenSynapse.App.Views.DevicePanels;

public sealed partial class RazerRipsawPanel : UserControl
{
    private CancellationTokenSource? _lifetime;
    public RazerRipsawPanel()
    {
        InitializeComponent();
        Loaded += (_, _) => _lifetime ??= new();
        Unloaded += (_, _) => { _lifetime?.Cancel(); _lifetime?.Dispose(); _lifetime = null; };
    }

    private async void RefreshClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is RazerRipsawViewModel vm && _lifetime is { } lifetime)
            await vm.RefreshAsync(lifetime.Token);
    }

    private async void ApplyClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is RazerRipsawViewModel vm && _lifetime is { } lifetime &&
            sender is FrameworkElement { DataContext: RazerRipsawChannelViewModel row })
            await vm.ApplyChannelAsync(row, lifetime.Token);
    }
}
