using Microsoft.UI.Xaml;

namespace OpenSynapse.App;

public sealed partial class MainWindow
{
    private BladeFanCurveWindow? _bladeFanCurveWindow;

    private void OpenBladeFanCurveWindowClick(object sender, RoutedEventArgs e)
    {
        if (_bladeFanCurveWindow is not null)
        {
            _bladeFanCurveWindow.BringToFront();
            return;
        }

        var window = new BladeFanCurveWindow(
            AppWindow,
            _viewModel,
            RootLayout.XamlRoot?.RasterizationScale ?? 1d);
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_bladeFanCurveWindow, window))
            {
                _bladeFanCurveWindow = null;
            }
        };
        _bladeFanCurveWindow = window;
        window.Activate();
        window.BringToFront();
    }

    private void CloseBladeFanCurveWindow()
    {
        var window = _bladeFanCurveWindow;
        _bladeFanCurveWindow = null;
        window?.Close();
    }
}