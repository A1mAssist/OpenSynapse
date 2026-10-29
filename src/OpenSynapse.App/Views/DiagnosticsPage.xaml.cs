using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OpenSynapse.App.ViewModels;
using Windows.ApplicationModel.DataTransfer;

namespace OpenSynapse.App.Views;

public sealed partial class DiagnosticsPage : UserControl
{
    internal MainWindow Host { get; set; } = null!;

    private MainViewModel ViewModel => (MainViewModel)DataContext;
    private CancellationToken Lifetime => Host.LifetimeToken;

    public DiagnosticsPage() => InitializeComponent();

    private async void RefreshClick(object sender, RoutedEventArgs e)
    {
        await ViewModel.Refresh.RefreshDevicesAsync(Lifetime);
        _ = ViewModel.RefreshHueAsync(Lifetime);
    }

    private void CopyDiagnosticsClick(object sender, RoutedEventArgs e)
    {
        var package = new DataPackage();
        package.SetText(ViewModel.Diagnostics.CreateReport());
        Clipboard.SetContent(package);
        DiagnosticsCopyStatusText.Text = AppStrings.Text("DiagnosticsCopied");
    }

    private async void ScanHidCommandsClick(object sender, RoutedEventArgs e)
    {
        DiagnosticsCopyStatusText.Text = AppStrings.Text("HidScanRunning");
        try
        {
            var path = await ViewModel.OpenRazer.ScanSelectedDeviceAsync(Lifetime);
            DiagnosticsCopyStatusText.Text = AppStrings.FormatText("HidScanDone", path);
        }
        catch (OperationCanceledException)
        {
            DiagnosticsCopyStatusText.Text = string.Empty;
        }
        catch (InvalidOperationException)
        {
            ViewModel.ReportApplicationError(AppStrings.Text("HidScanNoDevice"));
            DiagnosticsCopyStatusText.Text = string.Empty;
        }
        catch (Exception exception)
        {
            ViewModel.ReportApplicationError(exception.Message);
            DiagnosticsCopyStatusText.Text = string.Empty;
        }
    }
}
