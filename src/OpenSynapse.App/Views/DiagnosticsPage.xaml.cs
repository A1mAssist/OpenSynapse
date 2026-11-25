using System.ComponentModel;
using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OpenSynapse.App.ViewModels;
using System.Runtime.InteropServices;
using Windows.ApplicationModel.DataTransfer;

namespace OpenSynapse.App.Views;

public sealed partial class DiagnosticsPage : UserControl
{
    internal MainWindow Host { get; set; } = null!;

    private MainViewModel ViewModel => (MainViewModel)DataContext;
    private CancellationToken Lifetime => Host.LifetimeToken;

    public DiagnosticsPage() => InitializeComponent();

    // A cached page is removed from the tree, but its ItemsRepeater keeps listening
    // to the shared row collection; the next rebuild then calls into the dead peer.
    internal void Shutdown() => RowsRepeater.ItemsSource = null;

    private async void RefreshClick(object sender, RoutedEventArgs e)
    {
        try
        {
            ViewModel.Refresh.SetDiagnosticsRebuildEnabled(true);
            await ViewModel.Refresh.RefreshDevicesAsync(Lifetime);
            if (ViewModel.Hue.IsConfigured) _ = ViewModel.RefreshHueAsync(Lifetime);
        }
        catch (OperationCanceledException) when (Lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            // An async void handler must not take the window down for one failed probe.
            ViewModel.ReportApplicationError(
                AppStrings.FormatText("LabeledError",
                    AppStrings.Text("Loc_B2441B64DA.Content"), exception.Message));
        }
    }

    private async void CopyDiagnosticsClick(object sender, RoutedEventArgs e)
    {
        try
        {
            // Entering the page requests a rebuild, but a fast click can land before the
            // watch loop drains that request. Refresh here so the copied report has rows.
            ViewModel.Refresh.SetDiagnosticsRebuildEnabled(true);
            await ViewModel.Refresh.RefreshDevicesAsync(Lifetime);
        }
        catch (OperationCanceledException) when (Lifetime.IsCancellationRequested)
        {
            return;
        }
        catch (Exception)
        {
            // Keep the last captured rows; copying them is still useful.
        }
        ViewModel.Diagnostics.EnsureRazerHidRows();

        try
        {
            var package = new DataPackage();
            package.SetText(ViewModel.Diagnostics.CreateReport());
            try
            {
                Clipboard.SetContent(package);
            }
            catch (COMException)
            {
                // The clipboard can be held open by another process for a few milliseconds.
                await Task.Delay(80, Lifetime);
                Clipboard.SetContent(package);
            }
            DiagnosticsCopyStatusText.Text = AppStrings.Text("DiagnosticsCopied");
        }
        catch (OperationCanceledException) when (Lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            DiagnosticsCopyStatusText.Text = AppStrings.FormatText("DiagnosticsCopyFailed", exception.Message);
        }
    }

    private async void ScanHidCommandsClick(object sender, RoutedEventArgs e)
    {
        DiagnosticsCopyStatusText.Text = AppStrings.Text("HidScanRunning");
        try
        {
            var path = await ViewModel.RazerHid.ScanSelectedDeviceAsync(Lifetime);
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

    private void ExportDiagnosticsLogsClick(object sender, RoutedEventArgs e)
    {
        ViewModel.Diagnostics.EnsureRazerHidRows();

        try
        {
            var path = ViewModel.Diagnostics.ExportLogBundle();
            DiagnosticsCopyStatusText.Text = AppStrings.Text("DiagnosticsLogsExported");
            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"")
                {
                    UseShellExecute = true,
                });
            }
            catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
            {
                // The bundle is already on disk even when Explorer refuses to open.
            }
        }
        catch (Exception exception)
        {
            DiagnosticsCopyStatusText.Text = AppStrings.FormatText("DiagnosticsLogsExportFailed", exception.Message);
        }
    }
}
