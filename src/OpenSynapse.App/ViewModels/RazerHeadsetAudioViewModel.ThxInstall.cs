using OpenSynapse.Windows.Audio;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHeadsetAudioViewModel
{
    public async Task InstallThxComponentsAsync(CancellationToken cancellationToken = default)
    {
        if (!CanInstallThx || !BeginOperation()) return;
        _diagnostic?.Invoke($"thx install requested pid={_connection.ProductId:X4}");

        // Progress<T> posts to the UI context. Stop accepting updates once the
        // command has finished so a queued 100% callback cannot overwrite the
        // final state with the "downloading" text.
        var finished = false;
        var lastPercent = -1;
        var progress = new Progress<double>(fraction =>
        {
            // IsBusy turns false before the post-install refresh, so a queued
            // progress callback cannot put the card back into "downloading".
            if (finished || !IsBusy) return;
            var percent = (int)Math.Round(Math.Clamp(fraction, 0d, 1d) * 100d);
            if (percent == lastPercent) return;
            lastPercent = percent;
            OperationStatusText =
                $"{AppStrings.Text("RazerHeadsetThxDownloading")} {percent}%";
        });
        try
        {
            await InstallThxAsync(
                () => ThxHeadsetAdmission.InstallComponentsAsync(
                    _connection.ProductId,
                    cancellationToken,
                    progress,
                    _diagnostic),
                cancellationToken);
        }
        finally
        {
            finished = true;
        }
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
                    "ThxInstallerTimeout" =>
                        AppStrings.Text("RazerHeadsetThxInstallTimeout"),
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
        catch (Exception exception)
        {
            _diagnostic?.Invoke(
                $"thx install failed: {exception.GetType().Name}: {exception.Message}");
            ErrorText = AppStrings.Text("RazerHeadsetThxDownloadFailed");
            OperationStatusText = string.Empty;
        }
        finally
        {
            IsBusy = false;
        }

        if (!cancellationToken.IsCancellationRequested && _thxStatus?.RebootRequired != true)
        {
            await RefreshThxAsync(cancellationToken);
        }
    }

}
