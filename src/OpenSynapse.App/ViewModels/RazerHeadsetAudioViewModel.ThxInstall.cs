using OpenSynapse.Windows.Audio;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHeadsetAudioViewModel
{
    public async Task InstallThxNativeAsync(CancellationToken cancellationToken = default)
    {
        if (!CanDownloadThxComponent || !BeginOperation()) return;
        await InstallThxAsync(
            () => ThxHeadsetAdmission.InstallNativeComponentAsync(_connection.ProductId, cancellationToken),
            cancellationToken);
    }

    public async Task InstallThxComponentsAsync(CancellationToken cancellationToken = default)
    {
        if (!CanInstallThx || !BeginOperation()) return;
        await InstallThxAsync(
            () => ThxHeadsetAdmission.InstallComponentsAsync(_connection.ProductId, cancellationToken),
            cancellationToken);
    }

    private async Task InstallThxAsync(
        Func<Task<ThxHeadsetStatus>> install,
        CancellationToken cancellationToken)
    {
        try
        {
            OperationStatusText = AppStrings.Text("RazerHeadsetThxDownloading");
            _thxStatus = await install();
            ErrorText =
                (_thxStatus.ErrorCode is "ThxDownloadFailed" or "ThxDownloadVerificationFailed") ||
                _thxStatus.ErrorCode?.StartsWith("ThxInstaller", StringComparison.Ordinal) == true
                    ? AppStrings.Text("RazerHeadsetThxDownloadFailed")
                    : string.Empty;
            OperationStatusText = _thxStatus.Availability == ThxHeadsetAvailability.Available
                ? AppStrings.Text("RazerHeadsetThxReady")
                : string.Empty;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            IsBusy = false;
        }

        if (!cancellationToken.IsCancellationRequested)
        {
            await RefreshThxAsync(cancellationToken);
        }
    }

}
