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
            "ThxServiceBroken" => AppStrings.Text("RazerHeadsetThxServiceBroken"),
            "ThxServiceStopped" => AppStrings.Text("RazerHeadsetThxServiceMissing"),
            "ThxNativeMissing" => AppStrings.Text("RazerHeadsetThxNativeMissing"),
            "ThxRebootRequired" => AppStrings.Text("RazerHeadsetThxRebootRequired"),
            "ThxDownloadFailed" or "ThxDownloadVerificationFailed" =>
                AppStrings.Text("RazerHeadsetThxDownloadFailed"),
            "ThxInstallerCancelled" => AppStrings.Text("RazerHeadsetThxInstallCancelled"),
            "ThxInstallerBusy" => AppStrings.Text("RazerHeadsetThxInstallBusy"),
            "ThxInstallerTimeout" => AppStrings.Text("RazerHeadsetThxInstallTimeout"),
            var code when code?.StartsWith("ThxInstallerExit", StringComparison.Ordinal) == true =>
                AppStrings.FormatText(
                    "RazerHeadsetThxInstallerExited",
                    code["ThxInstallerExit".Length..]),
            var code when code?.StartsWith("ThxInstaller", StringComparison.Ordinal) == true =>
                AppStrings.Text("RazerHeadsetThxDownloadFailed"),
            _ when ThxStatus.Availability == ThxHeadsetAvailability.InstalledButInactive =>
                AppStrings.Text("RazerHeadsetThxRejectedInstalled"),
            _ when ThxStatus.Availability == ThxHeadsetAvailability.Available =>
                AppStrings.Text("RazerHeadsetThxReady"),
            _ => string.Empty,
        };

    private bool NeedsThxInstall =>
        ThxStatus.Availability is not (ThxHeadsetAvailability.Available or
            ThxHeadsetAvailability.InstalledButInactive) ||
        (ThxStatus.Availability == ThxHeadsetAvailability.InstalledButInactive &&
            ThxStatus.ErrorCode == "ThxServiceStopped");

    public Visibility ThxInstallerVisibility =>
        NeedsThxInstall && !ThxStatus.RebootRequired
            ? ThxVisibility
            : Visibility.Collapsed;

    public bool CanInstallThx =>
        !_thxBusy && NeedsThxInstall &&
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

    public async Task RefreshThxAsync(CancellationToken cancellationToken = default)
    {
        _thxErrorText = string.Empty;
        if (ThxHeadsetAdmission.Check(_connection.ProductId) is { RebootRequired: true } rebootStatus)
        {
            _thxStatus = await ThxHeadsetAdmission.TryClearPendingRebootAsync(
                _connection.ProductId,
                _connection.ContainerId,
                _diagnostic,
                cancellationToken).ConfigureAwait(true) ?? rebootStatus;
            NotifyThxStatus();
            return;
        }

        _thxStatus = await ThxHeadsetAdmission
            .CheckAsync(_connection.ProductId, cancellationToken)
            .ConfigureAwait(true);
        NotifyThxStatus();
    }

    private void NotifyThxStatus()
    {
        OnPropertyChanged(nameof(ThxStatusText));
        OnPropertyChanged(nameof(ThxInstallerVisibility));
        OnPropertyChanged(nameof(CanInstallThx));
    }
}
