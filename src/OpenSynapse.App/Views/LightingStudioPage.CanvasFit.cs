using System.Diagnostics;
using Microsoft.UI.Xaml;

namespace OpenSynapse.App.Views;

public sealed partial class LightingStudioPage
{
    private void RefreshCanvas()
    {
#if DEBUG
        if (_studioBenchmarkOwnsCanvas || _studioSelectionQa) return;
#endif
        if (_viewModel is null) return;
        var timingStarted = Stopwatch.GetTimestamp();
        var visuals = _viewModel.DeviceVisuals;
#if DEBUG
        var dumpPath = Environment.GetEnvironmentVariable("OPENSSYNAPSE_STUDIO_DUMP");
        if (!string.IsNullOrEmpty(dumpPath))
        {
            File.AppendAllText(dumpPath,
                $"{Environment.NewLine}page visuals={visuals.Count} selected={_viewModel.SelectedDevice?.Name} canvas={DeviceCanvas.Visibility}");
        }
#endif
        if (visuals.Count == 0)
        {
            DeviceCanvas.Visibility = Visibility.Collapsed;
            FallbackPreview.Visibility = Visibility.Visible;
            StudioTimingMarkRefresh(timingStarted, visuals, "empty");
            return;
        }

        var visualSignature = string.Join(
            "|",
            visuals.Select(visual =>
                FormattableString.Invariant(
                    $"{visual.InstanceId}:{visual.Width:0.##}x{visual.Height:0.##}:{visual.Leds.Count}")));
        var needsFit = !_canvasFitted ||
            !string.Equals(_fittedVisualSignature, visualSignature, StringComparison.Ordinal);
        DeviceCanvas.Devices = visuals.ToArray();
        DeviceCanvas.Profile = _viewModel.Profile;
        DeviceCanvas.Refresh();
        DeviceCanvas.Visibility = Visibility.Visible;
        FallbackPreview.Visibility = Visibility.Collapsed;
        DeviceCanvas.UpdateLayout();
        // New hosts mean new brushes and possibly different matrix dimensions; repaint the current frame.
        RenderPreview();
        StudioTimingMarkRefresh(timingStarted, visuals, "build");
        // Fit after the device manifest replaces the temporary grid, and when the
        // selected device changes. Later frame refreshes keep the user's zoom.
        if (!needsFit) return;
        _canvasFitted = true;
        _fittedVisualSignature = visualSignature;
        if (_userZoomed) return;
        void FitCanvas()
        {
            if (_userZoomed) return;
            _fitting = true;
            try
            {
                DeviceCanvas.CancelPendingZoomView();
                DeviceCanvas.FitToView();
                DeviceCanvas.CancelPendingZoomView();
            }
            finally
            {
                _fitting = false;
            }
        }

        DispatcherQueue.TryEnqueue(() =>
        {
            FitCanvas();
            DispatcherQueue.TryEnqueue(FitCanvas);
        });
    }
}
