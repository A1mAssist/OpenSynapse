using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace OpenSynapse.App.Views;

public sealed partial class OverviewPage : UserControl
{
    internal event Action<object>? DeviceRequested;

    public OverviewPage() => InitializeComponent();

    internal FrameworkElement TelemetrySection => SystemTelemetrySection;

    internal void ScrollToTop() => PageScrollViewer.ChangeView(null, 0, null, disableAnimation: true);

    private void ConnectedDeviceCardClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: { } source })
        {
            DeviceRequested?.Invoke(source);
        }
    }
}
