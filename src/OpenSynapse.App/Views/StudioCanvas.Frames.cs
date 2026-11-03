using OpenSynapse.Core.Lighting;
using Windows.UI;

namespace OpenSynapse.App.Views;

/// <summary>Per-device and single-device frame application.</summary>
public sealed partial class StudioCanvas
{
    public void SetFrames(IReadOnlyDictionary<string, StudioDeviceFrame> frames)
    {
        ArgumentNullException.ThrowIfNull(frames);
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

    public void SetFrame(IReadOnlyList<StudioRgb> frame, int rows, int columns)
    {
        if (rows <= 0 || columns <= 0 || (long)rows * columns > frame.Count) return;
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
