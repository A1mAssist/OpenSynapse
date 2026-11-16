using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OpenSynapse.App.ViewModels;

namespace OpenSynapse.App.Views;

public sealed partial class AccessoryBodyPanel : UserControl
{
    public AccessoryBodyPanel()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => UpdateVisibility();
    }

    private RazerAccessoryBodyViewModel? Body => DataContext as RazerAccessoryBodyViewModel;

    private void UpdateVisibility() =>
        Visibility = Body is null ? Visibility.Collapsed : Visibility.Visible;

    private async void ThermalRefreshClick(object sender, RoutedEventArgs e)
    {
        if (Body?.Thermal is { } thermal)
        {
            await thermal.RefreshAsync(CancellationToken.None);
        }
    }

    private async void ThermalChannelApplyClick(object sender, RoutedEventArgs e)
    {
        if (Body?.Thermal is not { } thermal ||
            sender is not Button { Tag: RazerThermalChannelRowViewModel row })
        {
            return;
        }

        await row.ApplyAsync(CancellationToken.None);
    }

    private async void HapticsApplyClick(object sender, RoutedEventArgs e)
    {
        if (Body?.Haptics is { } haptics)
        {
            await haptics.ApplyAsync(CancellationToken.None);
        }
    }

    private async void SpeakerRefreshClick(object sender, RoutedEventArgs e)
    {
        if (Body?.Speaker is { } speaker)
        {
            await speaker.RefreshAsync(CancellationToken.None);
        }
    }

    private async void SpeakerApplyClick(object sender, RoutedEventArgs e)
    {
        if (Body?.Speaker is { } speaker)
        {
            await speaker.ApplyAsync(CancellationToken.None);
        }
    }

    private async void DockRefreshClick(object sender, RoutedEventArgs e)
    {
        if (Body?.Dock is { } dock)
        {
            await dock.RefreshAsync(CancellationToken.None);
        }
    }

    private async void DockApplyClick(object sender, RoutedEventArgs e)
    {
        if (Body?.Dock is { } dock)
        {
            await dock.ApplyAsync(CancellationToken.None);
        }
    }

    private async void ControlPodRefreshClick(object sender, RoutedEventArgs e)
    {
        if (Body?.ControlPod is { } controlPod)
        {
            await controlPod.RefreshAsync(CancellationToken.None);
        }
    }

    private async void ControlPodApplyClick(object sender, RoutedEventArgs e)
    {
        if (Body?.ControlPod is { } controlPod)
        {
            await controlPod.ApplyAsync(CancellationToken.None);
        }
    }
}
