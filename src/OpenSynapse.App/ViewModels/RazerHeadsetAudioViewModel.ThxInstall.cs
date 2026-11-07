using OpenSynapse.Windows.Audio;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHeadsetAudioViewModel
{
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
            // A silent installer that exits 0 without starting the service used to look
            // like a no-op: say what is still missing instead of resetting the card.
            if (_thxStatus.Availability != ThxHeadsetAvailability.Available &&
                string.IsNullOrEmpty(ErrorText))
            {
                ErrorText = AppStrings.Text("RazerHeadsetThxIncomplete");
            }
            OperationStatusText = _thxStatus.Availability == ThxHeadsetAvailability.Available
                ? AppStrings.Text("RazerHeadsetThxReady")
                : string.Empty;
            NotifyThxStatus();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            ErrorText = exception.Message;
            OperationStatusText = string.Empty;
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
