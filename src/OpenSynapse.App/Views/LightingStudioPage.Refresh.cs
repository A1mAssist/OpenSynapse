namespace OpenSynapse.App.Views;

public sealed partial class LightingStudioPage
{
    private bool _refreshRunning;
    private bool _refreshPending;

    internal async Task RefreshDevicesFromHostAsync()
    {
        if (_viewModel is null) return;
        if (_refreshRunning)
        {
            // A discovery is already in flight; run once more with the final state.
            _refreshPending = true;
            return;
        }

        _refreshRunning = true;
        try
        {
            do
            {
                _refreshPending = false;
                await Host.RefreshStudioArgbAsync();
                RefreshDevicesWithTiming("hotplug");
                StudioTimingMark("hotplug-refresh-done");
            } while (_refreshPending && _viewModel is not null);
        }
        finally
        {
            _refreshRunning = false;
        }
    }
}