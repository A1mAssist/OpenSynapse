using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace OpenSynapse.App.Views.DevicePanels;

public sealed partial class ConnectedDeviceCard : UserControl
{
    public ConnectedDeviceCard()
    {
        InitializeComponent();
    }

    public event EventHandler<RoutedEventArgs>? CardClicked;

    private void CardButtonClick(object sender, RoutedEventArgs e) =>
        CardClicked?.Invoke(this, e);
}
