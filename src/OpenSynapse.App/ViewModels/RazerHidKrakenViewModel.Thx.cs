using Microsoft.UI.Xaml;
using OpenSynapse.Windows.Audio;

namespace OpenSynapse.App.ViewModels;

/// <summary>
/// Kraken V4 (056B) has no 91-byte audio interface, so the audio page cannot discover it.
/// THX is an APO plus the VSSrv service and needs no HID interface, so its install entry
/// lives on this page instead.
/// </summary>
public sealed partial class RazerHidKrakenViewModel
{
    private ThxHeadsetStatus? _thxStatus;
    private bool _thxBusy;
    private string _thxErrorText = string.Empty;

    private ThxHeadsetStatus ThxStatus => _thxStatus ??=
        ThxHeadsetAdmission.IsSupported(Connection.ProductId)
            ? ThxHeadsetAdmission.Check(Connection.ProductId)
            : new ThxHeadsetStatus(ThxHeadsetAvailability.UnsupportedDevice, null);

    public Visibility ThxVisibility => ThxHeadsetAdmission.IsSupported(Connection.ProductId)
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
            _ when ThxStatus.ErrorCode?.StartsWith("ThxInstaller", StringComparison.Ordinal) == true =>
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

    public string ThxGameProfilesUri =>
        ThxHeadsetAdmission.GetProfile(Connection.ProductId)?.GameProfilesUri ?? string.Empty;

    public Visibility ThxGameProfilesVisibility =>
        ThxGameProfilesUri.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

    public string ThxPatcherUri =>
        ThxHeadsetAdmission.GetProfile(Connection.ProductId)?.PatcherUri ?? string.Empty;

    public Visibility ThxPatcherVisibility =>
        ThxPatcherUri.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

    public async Task InstallThxComponentsAsync(CancellationToken cancellationToken = default)
    {
        if (!CanInstallThx)
        {
            return;
        }

        _thxBusy = true;
        _thxErrorText = string.Empty;
        OnPropertyChanged(nameof(CanInstallThx));
        try
        {
            _thxStatus = await ThxHeadsetAdmission.InstallComponentsAsync(
                Connection.ProductId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or HttpRequestException or
                InvalidOperationException)
        {
            _thxErrorText = AppStrings.Text("RazerHeadsetThxDownloadFailed");
        }
        finally
        {
            _thxBusy = false;
            OnPropertyChanged(nameof(ThxStatusText));
            OnPropertyChanged(nameof(ThxInstallerVisibility));
            OnPropertyChanged(nameof(CanInstallThx));
        }
    }

    public void RefreshThx()
    {
        _thxErrorText = string.Empty;
        _thxStatus = ThxHeadsetAdmission.Check(Connection.ProductId);
        OnPropertyChanged(nameof(ThxStatusText));
        OnPropertyChanged(nameof(ThxInstallerVisibility));
        OnPropertyChanged(nameof(CanInstallThx));
    }
}
