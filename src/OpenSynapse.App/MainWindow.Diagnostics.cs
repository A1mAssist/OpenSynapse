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
    private async void ScanHidCommandsClick(object sender, RoutedEventArgs e)
    {
        DiagnosticsCopyStatusText.Text = AppStrings.Text("HidScanRunning");
        try
        {
            var path = await _viewModel.ScanSelectedOpenRazerDeviceAsync(_lifetime.Token);
            DiagnosticsCopyStatusText.Text = AppStrings.FormatText("HidScanDone", path);
        }
        catch (OperationCanceledException)
        {
            DiagnosticsCopyStatusText.Text = string.Empty;
        }
        catch (InvalidOperationException)
        {
            _viewModel.ReportApplicationError(AppStrings.Text("HidScanNoDevice"));
            DiagnosticsCopyStatusText.Text = string.Empty;
        }
        catch (Exception exception)
        {
            _viewModel.ReportApplicationError(exception.Message);
            DiagnosticsCopyStatusText.Text = string.Empty;
        }
    }
}
