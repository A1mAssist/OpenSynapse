using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OpenSynapse.App.ViewModels;

namespace OpenSynapse.App.Views.DevicePanels;

public sealed partial class RazerLaylaPanel : UserControl
{
    public RazerLaylaPanel() => InitializeComponent();
    private async void SaveClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is RazerLaylaViewModel pod) await pod.SaveAsync();
    }
}
