namespace OpenSynapse.App;

public sealed partial class MainWindow
{
    /// <summary>
    /// Keeps the launch overlay up until the first device scan completes so the
    /// first click cannot queue behind the startup HID probe. The watch loop is
    /// still the retry path when the scan exceeds the launch budget.
    /// </summary>
    private async Task<bool> RefreshDevicesBeforeLaunchAsync()
    {
        using var launchRefresh = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        launchRefresh.CancelAfter(TimeSpan.FromSeconds(8));
        try
        {
            await _viewModel.Refresh.RefreshDevicesAsync(launchRefresh.Token);
            return true;
        }
        catch (OperationCanceledException) when (!_lifetime.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Let the watch loop retry instead of aborting the rest of startup.
            return false;
        }
    }
}
