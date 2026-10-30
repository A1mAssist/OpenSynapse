using Microsoft.UI.Xaml;
using OpenSynapse.Windows.Audio;

namespace OpenSynapse.App.ViewModels;

/// <summary>
/// Kraken V4 (056B) has no 91-byte audio interface, so the audio page cannot discover it.
/// THX is an APO plus the VSSrv service and needs no HID interface, so its install entry
/// lives on this page instead.
/// </summary>
public sealed partial class OpenRazerKrakenViewModel
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
            "ThxDownloadFailed" or "ThxDownloadVerificationFailed" =>
                AppStrings.Text("RazerHeadsetThxDownloadFailed"),
            _ when ThxStatus.Availability == ThxHeadsetAvailability.Available =>
                AppStrings.Text("RazerHeadsetThxReady"),
            _ => string.Empty,
        };

    public string ThxInstallerUri => ThxHeadsetAdmission.IsSupported(Connection.ProductId)
        ? ThxHeadsetAdmission.GetServiceInstallerUri(Connection.ProductId)
        : string.Empty;

    public Visibility ThxInstallerVisibility =>
        ThxStatus.Availability == ThxHeadsetAvailability.Available
            ? Visibility.Collapsed
            : ThxVisibility;

    public Visibility ThxDownloadVisibility =>
        ThxStatus.Availability == ThxHeadsetAvailability.NativeMissing
            ? Visibility.Visible
            : Visibility.Collapsed;

    public bool CanDownloadThxComponent =>
        !_thxBusy && ThxStatus.Availability == ThxHeadsetAvailability.NativeMissing;

    public string ThxGameProfilesUri =>
        ThxHeadsetAdmission.GetProfile(Connection.ProductId)?.GameProfilesUri ?? string.Empty;

    public Visibility ThxGameProfilesVisibility =>
        ThxGameProfilesUri.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

    public string ThxPatcherUri =>
        ThxHeadsetAdmission.GetProfile(Connection.ProductId)?.PatcherUri ?? string.Empty;

    public Visibility ThxPatcherVisibility =>
        ThxPatcherUri.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

    public async Task InstallThxNativeAsync(CancellationToken cancellationToken = default)
    {
        if (!CanDownloadThxComponent)
        {
            return;
        }

        _thxBusy = true;
        _thxErrorText = string.Empty;
        OnPropertyChanged(nameof(CanDownloadThxComponent));
        try
        {
            _thxStatus = await ThxHeadsetAdmission.InstallNativeComponentAsync(
                Connection.ProductId, cancellationToken);
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
            OnPropertyChanged(nameof(ThxDownloadVisibility));
            OnPropertyChanged(nameof(CanDownloadThxComponent));
        }
    }
}
