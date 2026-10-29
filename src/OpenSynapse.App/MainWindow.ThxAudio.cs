using Microsoft.UI.Xaml;

namespace OpenSynapse.App;

public sealed partial class MainWindow
{
    private ThxAudioWindow? _thxAudioWindow;

    internal void OpenThxAudioWindow()
    {
        if (_thxAudioWindow is not null)
        {
            _thxAudioWindow.BringToFront();
            return;
        }

        var window = new ThxAudioWindow(
            AppWindow,
            _viewModel.ThxAudio,
            RootLayout.XamlRoot?.RasterizationScale ?? 1d);
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_thxAudioWindow, window))
            {
                _thxAudioWindow = null;
            }
        };
        _thxAudioWindow = window;
        window.Activate();
        window.BringToFront();
    }

    private void CloseThxAudioWindow()
    {
        var window = _thxAudioWindow;
        _thxAudioWindow = null;
        window?.Close();
    }
}
