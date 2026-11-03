using OpenSynapse.Core.Lighting;

namespace OpenSynapse.App.Views;

/// <summary>Bucket selection scoped to the clicked SVG selection group.</summary>
public sealed partial class StudioCanvas
{
    private void SelectBucket(StudioDeviceVisual device, StudioVisualLed led)
    {
        var group = led.SelectionGroup;
        if (string.IsNullOrEmpty(group))
        {
            ApplySelection(StudioCanvasGeometry.DeviceCells(device));
            return;
        }

        var cells = new List<StudioLedRef>();
        var instanceId = device.InstanceId ?? string.Empty;
        foreach (var entry in _ledEntries)
        {
            if (!string.Equals(entry.InstanceId, instanceId, StringComparison.OrdinalIgnoreCase)) continue;
            if (!string.Equals(entry.Led.SelectionGroup, group, StringComparison.Ordinal)) continue;
            AddCell(cells, new StudioLedRef(entry.Led.MatrixRow, entry.Led.MatrixCol));
        }

        ApplySelection(cells);
    }
}
