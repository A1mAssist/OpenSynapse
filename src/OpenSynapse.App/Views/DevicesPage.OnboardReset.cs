using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OpenSynapse.App.ViewModels;
using OpenSynapse.App.Views.DevicePanels;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.Views;

public sealed partial class DevicesPage
{
    private async void ConnectedDeviceCardResetClick(object sender, RoutedEventArgs e)
    {
        if (sender is not ConnectedDeviceCard { DataContext: ConnectedDeviceRowViewModel row } ||
            !row.CanResetOnboardProfileNow)
        {
            return;
        }

        if (row.Source is not RazerHidDeviceRowViewModel { Connection.IsReady: true } sourceRow)
        {
            row.SetResetOnboardProfileStatus(
                AppStrings.Text("DeviceResetOnboardProfileWirelessIdle"), isError: true);
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = AppStrings.Text("DeviceResetOnboardProfileTitle"),
            Content = AppStrings.Text("DeviceResetOnboardProfileBody"),
            PrimaryButtonText = AppStrings.Text("DeviceResetOnboardProfileAction"),
            CloseButtonText = AppStrings.Text("Text_949856B3"),
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        row.SetResetOnboardProfileStatus(string.Empty, isError: false);
        row.SetResetOnboardProfileBusy(true);
        try
        {
            await ViewModel.RazerHid.ResetActiveOnboardProfileAsync(sourceRow, Lifetime);
            row.SetResetOnboardProfileStatus(
                AppStrings.Text("DeviceResetOnboardProfileDone"), isError: false);
            _ = ClearResetOnboardProfileStatusAsync(row);
        }
        catch (OperationCanceledException) when (Lifetime.IsCancellationRequested)
        {
        }
        catch (NotSupportedException)
        {
            row.DisableResetOnboardProfile();
            row.SetResetOnboardProfileStatus(
                AppStrings.Text("DeviceResetOnboardProfileUnsupported"), isError: true);
        }
        catch (RazerDeviceTimeoutException exception) when (exception.WirelessLinkDown)
        {
            row.SetResetOnboardProfileStatus(
                AppStrings.Text("DeviceResetOnboardProfileWirelessIdle"), isError: true);
        }
        catch (RazerCommandRejectedException)
        {
            row.SetResetOnboardProfileStatus(
                AppStrings.Text("RazerHidOnboardProfileRejected"), isError: true);
        }
        catch (Exception exception) when (
            exception is RazerDeviceTimeoutException or RazerReportMismatchException)
        {
            row.SetResetOnboardProfileStatus(
                AppStrings.Text("RazerHidOnboardProfileTimeout"), isError: true);
        }
        catch (InvalidOperationException)
        {
            row.SetResetOnboardProfileStatus(
                AppStrings.Text("RazerHidOnboardProfileFailed"), isError: true);
        }
        catch (Exception exception)
        {
            row.SetResetOnboardProfileStatus(
                AppStrings.Text("RazerHidOnboardProfileFailed"), isError: true);
            ViewModel.ReportApplicationError(exception.Message);
        }
        finally
        {
            row.SetResetOnboardProfileBusy(false);
        }
    }

    private async Task ClearResetOnboardProfileStatusAsync(ConnectedDeviceRowViewModel row)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(4), Lifetime);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        row.SetResetOnboardProfileStatus(string.Empty, isError: false);
    }
}
