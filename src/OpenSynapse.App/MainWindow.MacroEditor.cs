using Microsoft.UI.Xaml;
using OpenSynapse.App.ViewModels;

namespace OpenSynapse.App;

public sealed partial class MainWindow
{
    private MacroEditorWindow? _macroEditorWindow;

    /// <summary>
    /// The macro hook suppresses clicks made on our own UI. While the editor
    /// popout is in front it is the window being clicked, so it has to count
    /// as ours too.
    /// </summary>
    private bool IsOwnForegroundWindow(nint mainWindowHandle)
    {
        var foreground = GetForegroundWindow();
        return foreground != nint.Zero &&
            (foreground == mainWindowHandle || foreground == _macroEditorWindow?.Handle);
    }

    private void OpenMacroEditorWindowClick(object sender, RoutedEventArgs e)
    {
        if (_macroEditorWindow is not null)
        {
            _macroEditorWindow.BringToFront();
            return;
        }

        var window = new MacroEditorWindow(
            AppWindow,
            _viewModel,
            RootLayout.XamlRoot?.RasterizationScale ?? 1d);
        window.Closed += (_, _) =>
        {
            // A recording or a capture would otherwise stay armed after the
            // editor is gone.
            window.CancelPendingCapture();
            if (ReferenceEquals(_macroEditorWindow, window))
            {
                _macroEditorWindow = null;
            }
        };
        _macroEditorWindow = window;
        window.Activate();
        window.BringToFront();
    }

    private void CloseMacroEditorWindow()
    {
        var window = _macroEditorWindow;
        _macroEditorWindow = null;
        window?.Close();
    }
}
