using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;

namespace OpenSynapse.App.Views.DevicePanels;

public sealed partial class ConnectedDeviceHeader : UserControl
{
    public ConnectedDeviceHeader()
    {
        InitializeComponent();
    }
    internal void BindStatus(object source, string path) =>
        DeviceHeaderStatus.SetBinding(TextBlock.TextProperty, new Binding
        {
            Source = source,
            Path = new PropertyPath(path),
        });
}
