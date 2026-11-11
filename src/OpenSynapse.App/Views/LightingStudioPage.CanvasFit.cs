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
            _builtVisualSignature = null;
            DeviceCanvas.Visibility = Visibility.Collapsed;
            FallbackPreview.Visibility = Visibility.Visible;
            StudioTimingMarkRefresh(timingStarted, visuals, "empty");
            return;
        }

        var profile = _viewModel.Profile;
        var placements = profile is null
            ? Enumerable.Empty<string>()
            : profile.Devices.Select(placement => FormattableString.Invariant(
                $"{placement.InstanceId}:{placement.X:0.##}:{placement.Y:0.##}:{placement.Rotation}"));
        var visualSignature = string.Join(
            "|",
            visuals.Select(visual =>
                    FormattableString.Invariant(
                        $"{visual.InstanceId}:{visual.ProductId}:{visual.Width:0.##}x{visual.Height:0.##}:{visual.Leds.Count}:{visual.UsesSvg}:{visual.Paths.Count}"))
                .Concat(placements)
                .Prepend(profile?.Id ?? string.Empty));
        var needsFit = !_canvasFitted ||
            !string.Equals(_fittedVisualSignature, visualSignature, StringComparison.Ordinal);
        if (DeviceCanvas.Visibility == Visibility.Visible &&
            string.Equals(_builtVisualSignature, visualSignature, StringComparison.Ordinal))
        {
            return;
        }

        DeviceCanvas.Devices = visuals.ToArray();
        DeviceCanvas.Profile = _viewModel.Profile;
        DeviceCanvas.Refresh();
        _builtVisualSignature = visualSignature;
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
