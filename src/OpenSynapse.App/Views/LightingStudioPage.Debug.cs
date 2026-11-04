#if DEBUG
using Microsoft.UI.Xaml;

namespace OpenSynapse.App.Views;

public sealed partial class LightingStudioPage
{
    private bool _studioSelectionDumpAttached;

    private bool StartStudioSelectionQaIfRequested()
    {
        var mode = Environment.GetEnvironmentVariable("OPENSSYNAPSE_STUDIO_SELECTION_QA");
        if (string.IsNullOrWhiteSpace(mode)) return false;
        if (_studioSelectionQa) return true;
        _studioSelectionQa = true;
        _timer.Stop();
        DeviceCanvas.Devices = new[] { StudioCanvas.DebugCreateGridVisual("studio-selection-qa", 6, 8) };
        DeviceCanvas.Profile = null;
        DeviceCanvas.Refresh();
        DeviceCanvas.Visibility = Visibility.Visible;
        FallbackPreview.Visibility = Visibility.Collapsed;
        DeviceCanvas.UpdateLayout();
        DeviceCanvas.DebugPaintAll(0x20, 0x40, 0x60);
        if (!mode.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            DeviceCanvas.DebugSelectBlock(2, 2, 2, 4);
        }

        WriteStudioSelectionDump();
        DispatcherQueue.TryEnqueue(() =>
        {
            DeviceCanvas.FitToView();
            DeviceCanvas.DebugCenterFirstHost();
        });

        return true;
    }

    private void AttachStudioSelectionDump()
    {
        if (_studioSelectionDumpAttached) return;
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPENSSYNAPSE_STUDIO_SELECTION_QA_OUT")) &&
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPENSSYNAPSE_STUDIO_LED_DUMP"))) return;
        _studioSelectionDumpAttached = true;
        DeviceCanvas.SelectionChanged += (_, _) => WriteStudioSelectionDump();
    }

    private void WriteStudioSelectionDump()
    {
        WriteDumpFile(
            Environment.GetEnvironmentVariable("OPENSSYNAPSE_STUDIO_SELECTION_QA_OUT"),
            DeviceCanvas.DebugSelectionState());
        WriteDumpFile(
            Environment.GetEnvironmentVariable("OPENSSYNAPSE_STUDIO_LED_DUMP"),
            DeviceCanvas.DebugLedBounds());
    }

    private static void WriteDumpFile(string? path, string value)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, value);
            File.Move(temporary, path, overwrite: true);
        }
        catch (IOException) { }
    }

    private bool StartStudioBenchmarkIfRequested()
    {
        if (_studioBenchmark is not null) return true;
        if (!StudioCanvasBenchmark.TryCreate(DeviceCanvas, out var benchmark) || benchmark is null) return false;
        _studioBenchmark = benchmark;
        _studioBenchmarkOwnsCanvas = true;
        _timer.Stop();
        DeviceCanvas.Visibility = Visibility.Visible;
        FallbackPreview.Visibility = Visibility.Collapsed;
        benchmark.Start();
        return true;
    }
}
#endif