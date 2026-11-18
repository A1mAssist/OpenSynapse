using Microsoft.UI.Xaml;

namespace OpenSynapse.App.Views;

public sealed partial class LightingStudioPage
{
    private bool _previewSuspended;

    /// <summary>
    /// Hiding to the tray does not unload the cached page, so the 60fps preview
    /// clock would keep the UI thread busy with no visible consumer.
    /// </summary>
    internal void SetPreviewSuspended(bool suspended)
    {
        if (suspended)
        {
            if (!_timer.IsEnabled) return;
            _previewSuspended = true;
            _timer.Stop();
            return;
        }

        if (!_previewSuspended) return;
        _previewSuspended = false;
        if (Visibility == Visibility.Visible) _timer.Start();
    }

    internal void Shutdown()
    {
        _timer.Stop();
        _historyTimer.Stop();
        _captureSaveTimer.Stop();
        if (_viewModel is { } viewModel)
        {
            viewModel.CommitHistory();
            viewModel.Dispose();
        }
        _viewModel = null;
        _inputSampler = null;
    }
}
