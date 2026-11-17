using Microsoft.UI.Xaml;
using OpenSynapse.Windows.Audio;

namespace OpenSynapse.App.ViewModels;

public sealed partial class BlackSharkHeadsetViewModel
{
    private ThxHeadsetStatus? _thxStatus;
    private bool _thxBusy;
    private string _thxErrorText = string.Empty;

    private ThxHeadsetStatus ThxStatus => _thxStatus ??=
        ThxHeadsetAdmission.IsSupported(_connection.ProductId)
            ? ThxHeadsetAdmission.Check(_connection.ProductId)
            : new ThxHeadsetStatus(ThxHeadsetAvailability.UnsupportedDevice, null);

    public Visibility ThxVisibility => ThxHeadsetAdmission.IsSupported(_connection.ProductId)
        ? Visibility.Visible
        : Visibility.Collapsed;

    public string ThxStatusText => _thxErrorText.Length > 0
        ? _thxErrorText
        : ThxStatus.ErrorCode switch
        {
            "ThxServiceMissing" => AppStrings.Text("RazerHeadsetThxServiceMissing"),
            "ThxNativeMissing" => AppStrings.Text("RazerHeadsetThxNativeMissing"),
            "ThxRebootRequired" => AppStrings.Text("RazerHeadsetThxRebootRequired"),
            "ThxDownloadFailed" or "ThxDownloadVerificationFailed" =>
                AppStrings.Text("RazerHeadsetThxDownloadFailed"),
            _ when ThxStatus.Availability == ThxHeadsetAvailability.Available =>
                AppStrings.Text("RazerHeadsetThxReady"),
            _ => string.Empty,
        };

    public Visibility ThxInstallerVisibility =>
        ThxStatus.Availability == ThxHeadsetAvailability.Available || ThxStatus.RebootRequired
            ? Visibility.Collapsed
            : ThxVisibility;

    public bool CanInstallThx =>
        !_thxBusy && ThxStatus.Availability != ThxHeadsetAvailability.Available &&
        !ThxStatus.RebootRequired;

    public async Task InstallThxComponentsAsync(CancellationToken cancellationToken = default)
    {
        if (!CanInstallThx) return;

        _thxBusy = true;
        _thxErrorText = string.Empty;
        NotifyThxStatus();
        try
        {
            _thxStatus = await ThxHeadsetAdmission.InstallComponentsAsync(
                _connection.ProductId, cancellationToken);
            if (_thxStatus.Availability != ThxHeadsetAvailability.Available &&
                string.IsNullOrEmpty(_thxStatus.ErrorCode))
            {
                _thxErrorText = AppStrings.Text("RazerHeadsetThxIncomplete");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (IsExpectedException(exception) || exception is HttpRequestException)
        {
            _thxErrorText = AppStrings.Text("RazerHeadsetThxDownloadFailed");
        }
        finally
        {
            _thxBusy = false;
            NotifyThxStatus();
        }
    }

    public void RefreshThx()
    {
        _thxErrorText = string.Empty;
        _thxStatus = ThxHeadsetAdmission.Check(_connection.ProductId);
        NotifyThxStatus();
    }

    private void NotifyThxStatus()
    {
        OnPropertyChanged(nameof(ThxStatusText));
        OnPropertyChanged(nameof(ThxInstallerVisibility));
        OnPropertyChanged(nameof(CanInstallThx));
    }
}
