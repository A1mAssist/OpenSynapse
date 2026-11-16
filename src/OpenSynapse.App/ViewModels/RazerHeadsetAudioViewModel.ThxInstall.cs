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
            _diagnostic?.Invoke(
                $"thx install availability={_thxStatus.Availability} " +
                $"error={_thxStatus.ErrorCode ?? "-"} reboot={_thxStatus.RebootRequired}");
            ErrorText = _thxStatus.RebootRequired
                ? AppStrings.Text("RazerHeadsetThxRebootRequired")
                : _thxStatus.ErrorCode switch
                {
                    "ThxInstallerCancelled" =>
                        AppStrings.Text("RazerHeadsetThxInstallCancelled"),
                    "ThxInstallerBusy" =>
                        AppStrings.Text("RazerHeadsetThxInstallBusy"),
                    "ThxDownloadFailed" or "ThxDownloadVerificationFailed" =>
                        AppStrings.Text("RazerHeadsetThxDownloadFailed"),
                    var code when code?.StartsWith(
                        "ThxInstallerExit",
                        StringComparison.Ordinal) == true =>
                        AppStrings.FormatText(
                            "RazerHeadsetThxInstallerExited",
                            code["ThxInstallerExit".Length..]),
                    var code when code?.StartsWith(
                        "ThxInstaller",
                        StringComparison.Ordinal) == true =>
                        AppStrings.Text("RazerHeadsetThxDownloadFailed"),
                    _ => string.Empty,
                };
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
