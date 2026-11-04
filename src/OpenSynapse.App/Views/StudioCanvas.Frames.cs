using OpenSynapse.Core.Lighting;
using Windows.UI;

namespace OpenSynapse.App.Views;

/// <summary>Per-device and single-device frame application.</summary>
public sealed partial class StudioCanvas
{
    private IReadOnlyDictionary<string, StudioDeviceFrame>? _lastFrames;
    private (IReadOnlyList<StudioRgb> Frame, int Rows, int Columns)? _lastFrame;

    public void SetFrames(IReadOnlyDictionary<string, StudioDeviceFrame> frames)
    {
        ArgumentNullException.ThrowIfNull(frames);
        _lastFrames = frames;
        _lastFrame = null;
        ApplyFrames(frames);
    }

    public void SetFrame(IReadOnlyList<StudioRgb> frame, int rows, int columns)
    {
        if (rows <= 0 || columns <= 0 || (long)rows * columns > frame.Count) return;
        _lastFrame = (frame, rows, columns);
        _lastFrames = null;
        ApplyFrame(frame, rows, columns);
    }

    /// <summary>A refresh rebuilds every host and brush, so the last frame must be replayed onto them.</summary>
    private void ReapplyFrames()
    {
        if (_lastFrames is { } frames) ApplyFrames(frames);
        else if (_lastFrame is { } frame) ApplyFrame(frame.Frame, frame.Rows, frame.Columns);
    }

    private void ApplyFrames(IReadOnlyDictionary<string, StudioDeviceFrame> frames)
    {
        foreach (var entry in _ledEntries)
        {
            if (!frames.TryGetValue(entry.InstanceId, out var deviceFrame)) continue;
            var row = entry.Led.MatrixRow;
            var column = entry.Led.MatrixCol;
            if (row < 0 || row >= deviceFrame.Rows || column < 0 || column >= deviceFrame.Columns) continue;
            if ((long)row * deviceFrame.Columns + column >= deviceFrame.Frame.Length) continue;
            var rgb = deviceFrame.Frame[row * deviceFrame.Columns + column];
            var color = Color.FromArgb(255, rgb.R, rgb.G, rgb.B);
            if (!entry.Brush.Color.Equals(color)) entry.Brush.Color = color;
        }
    }

    private void ApplyFrame(IReadOnlyList<StudioRgb> frame, int rows, int columns)
    {
        foreach (var entry in _ledEntries)
        {
            var row = entry.Led.MatrixRow;
            var column = entry.Led.MatrixCol;
            if (row < 0 || row >= rows || column < 0 || column >= columns) continue;
            var rgb = frame[row * columns + column];
            var color = Color.FromArgb(255, rgb.R, rgb.G, rgb.B);
            if (!entry.Brush.Color.Equals(color)) entry.Brush.Color = color;
        }
    }
}
