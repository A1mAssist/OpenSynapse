#if DEBUG
using System.Text.Json;
using Microsoft.UI.Xaml;

namespace OpenSynapse.App.Views;

public sealed partial class LightingStudioPage
{
    private bool _viewportQaStarted;

    private async Task RunStudioViewportQaIfRequestedAsync()
    {
        var path = Environment.GetEnvironmentVariable("OPENSSYNAPSE_STUDIO_VIEWPORT_QA_OUT");
        if (string.IsNullOrWhiteSpace(path) || _viewportQaStarted) return;
        _viewportQaStarted = true;
        var checks = new List<string>();
        void Check(bool passed, string name)
        {
            if (!passed) throw new InvalidOperationException(name);
            checks.Add(name);
        }

        try
        {
            await Task.Delay(800);
            Check(_canvasFitted && !_userAdjustedView && DeviceCanvas.DebugHostCount > 0,
                "Initial view fits automatically");
            var zoom = DeviceCanvas.ZoomPercent;
            var before = DeviceCanvas.DebugViewOffset;
            DeviceCanvas.DebugPanBy(0, 0);
            Check(!_userAdjustedView, "A click without movement keeps automatic fitting");
            DeviceCanvas.DebugPanBy(100, 40);
            await Task.Delay(150);
            var panned = DeviceCanvas.DebugViewOffset;
            Check(_userAdjustedView && Math.Abs(panned.X - before.X + 100) < 0.5 &&
                Math.Abs(panned.Y - before.Y + 40) < 0.5, "Panning moves the viewport and disables automatic fitting");

            foreach (var mode in new[] { "edit", "browse" })
            {
                ToggleEditClick(EditButton, new RoutedEventArgs());
                await Task.Delay(500);
                var after = DeviceCanvas.DebugViewOffset;
                Check(Math.Abs(DeviceCanvas.ZoomPercent - zoom) < 0.001 &&
                    Math.Abs(after.X - panned.X) < 0.5 && Math.Abs(after.Y - panned.Y) < 0.5,
                    $"Switching to {mode} preserves the panned viewport and zoom");
            }

            WriteDumpFile(path, JsonSerializer.Serialize(new { status = "passed", checks, zoom,
                offsetX = panned.X, offsetY = panned.Y }));
        }
        catch (Exception exception)
        {
            WriteDumpFile(path, JsonSerializer.Serialize(new { status = "failed", checks, error = exception.ToString() }));
        }
    }
}
#endif
