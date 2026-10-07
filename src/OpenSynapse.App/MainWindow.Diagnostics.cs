using Microsoft.UI.Xaml;
using Windows.ApplicationModel.DataTransfer;

namespace OpenSynapse.App;

public sealed partial class MainWindow
{
    private void CopyDiagnosticsClick(object sender, RoutedEventArgs e)
    {
        var package = new DataPackage();
        package.SetText(_viewModel.CreateDiagnosticsReport());
        Clipboard.SetContent(package);
        DiagnosticsCopyStatusText.Text = AppStrings.Text("DiagnosticsCopied");
    }
}
