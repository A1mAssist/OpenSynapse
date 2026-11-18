using Microsoft.UI.Xaml;
using OpenSynapse.Windows.Devices;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerKeyboardViewModel
{
    private string _firmwareUpdateStatus = string.Empty;
    private string _firmwareUpdateUrl = string.Empty;
    private bool _isFirmwareUpdateBusy;

    public string FirmwareUpdateStatusText
    {
        get => _firmwareUpdateStatus;
        private set => SetField(ref _firmwareUpdateStatus, value);
    }

    public string FirmwareUpdateUrl
    {
        get => _firmwareUpdateUrl;
        private set
        {
            if (SetField(ref _firmwareUpdateUrl, value))
            {
                OnPropertyChanged(nameof(FirmwareUpdateDownloadVisibility));
            }
        }
    }

    public Visibility FirmwareUpdateDownloadVisibility =>
        string.IsNullOrWhiteSpace(FirmwareUpdateUrl) ? Visibility.Collapsed : Visibility.Visible;

    public bool CanCheckFirmwareUpdate =>
        !_isFirmwareUpdateBusy && FirmwareText is not (null or "" or "--");

    public async Task CheckFirmwareUpdateAsync(CancellationToken cancellationToken = default)
    {
        if (!CanCheckFirmwareUpdate)
        {
            return;
        }

        _isFirmwareUpdateBusy = true;
        FirmwareUpdateUrl = string.Empty;
        OnPropertyChanged(nameof(CanCheckFirmwareUpdate));
        FirmwareUpdateStatusText = AppStrings.Text("FirmwareUpdateChecking");
        try
        {
            var check = await RazerPeripheralFirmwareClient.CheckAsync(
                _connection.ProductId, FirmwareText, Name, cancellationToken);
            if (check is null)
            {
                FirmwareUpdateStatusText = AppStrings.Text("FirmwareUpdateSupportPageUnavailable");
            }
            else if (check.HasUpdate)
            {
                FirmwareUpdateStatusText =
                    AppStrings.FormatText("FirmwareUpdateAvailable", $"v{check.LatestVersion}");
                FirmwareUpdateUrl = check.TargetUrl ?? string.Empty;
            }
            else
            {
                FirmwareUpdateStatusText = AppStrings.Text("FirmwareUpdateCurrent");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is HttpRequestException or IOException or InvalidOperationException or
            TaskCanceledException or UriFormatException)
        {
            FirmwareUpdateStatusText = AppStrings.FormatText("FirmwareUpdateFailed", exception.Message);
        }
        finally
        {
            _isFirmwareUpdateBusy = false;
            OnPropertyChanged(nameof(CanCheckFirmwareUpdate));
        }
    }

    public async Task InstallFirmwareUpdateAsync(CancellationToken cancellationToken = default)
    {
        if (_isFirmwareUpdateBusy || string.IsNullOrWhiteSpace(FirmwareUpdateUrl))
        {
            return;
        }

        _isFirmwareUpdateBusy = true;
        OnPropertyChanged(nameof(CanCheckFirmwareUpdate));
        FirmwareUpdateStatusText = AppStrings.Text("FirmwareUpdateDownloading");
        try
        {
            var progress = new Progress<int>(percent =>
                FirmwareUpdateStatusText =
                    $"{AppStrings.Text("FirmwareUpdateDownloading")} {percent}%");
            var result = await RazerPeripheralFirmwareClient.DownloadAndLaunchAsync(
                FirmwareUpdateUrl, progress, cancellationToken);
            FirmwareUpdateStatusText = result.Failure switch
            {
                RazerFirmwareInstallFailure.None =>
                    AppStrings.Text("FirmwareUpdateInstallerStarted"),
                RazerFirmwareInstallFailure.InvalidPackage =>
                    AppStrings.Text("FirmwareUpdateSignatureFailed"),
                RazerFirmwareInstallFailure.Cancelled =>
                    AppStrings.Text("FirmwareUpdateCancelled"),
                _ => AppStrings.FormatText("FirmwareUpdateLaunchFailed", result.Error ?? string.Empty),
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is HttpRequestException or IOException or InvalidOperationException or
            TaskCanceledException or UriFormatException)
        {
            FirmwareUpdateStatusText = AppStrings.FormatText("FirmwareUpdateLaunchFailed", exception.Message);
        }
        finally
        {
            _isFirmwareUpdateBusy = false;
            OnPropertyChanged(nameof(CanCheckFirmwareUpdate));
        }
    }
}
