namespace OpenSynapse.Core.Lighting;

/// <summary>Device-aware LED selection helpers shared by the canvas and region factory.</summary>
public static class StudioLedSelection
{
    /// <summary>One canvas cell with the device's input/region identity attached.</summary>
    public static StudioLedRef CellFor(StudioDeviceVisual device, int row, int col)
    {
        ArgumentNullException.ThrowIfNull(device);
        var led = device.Layout?.FindMatrix(row, col);
        var input = device.Layout?.FindInputAt(row, col);
        return new StudioLedRef(
            row,
            col,
            led?.RegionId,
            led?.RegionIndex,
            input?.InputType,
            input is { } mapped
                ? FormattableString.Invariant($"[{mapped.InputCode},{mapped.InputModifier}]")
                : null,
            device.InstanceId);
    }

    public static bool SameInstance(string? left, string? right) =>
        string.Equals(left ?? string.Empty, right ?? string.Empty, StringComparison.OrdinalIgnoreCase);

    public static int IndexOfCell(IReadOnlyList<StudioLedRef> leds, StudioLedRef cell)
    {
        for (var index = 0; index < leds.Count; index++)
        {
            if (leds[index].Row == cell.Row && leds[index].Col == cell.Col &&
                SameInstance(leds[index].InstanceId, cell.InstanceId))
                return index;
        }

        return -1;
    }

    public static bool Contains(IReadOnlyList<StudioLedRef> leds, StudioLedRef cell) =>
        IndexOfCell(leds, cell) >= 0;

    public static bool SameSelection(IReadOnlyList<StudioLedRef> current, IReadOnlyList<StudioLedRef> next)
    {
        if (next.Count != current.Count) return false;
        foreach (var led in next)
        {
            if (IndexOfCell(current, led) < 0) return false;
        }

        return true;
    }

    public static List<StudioLedRef> SelectCell(
        IReadOnlyList<StudioLedRef> current, StudioLedRef cell, bool toggle)
    {
        var next = new List<StudioLedRef>(current);
        var index = IndexOfCell(next, cell);
        if (!toggle)
        {
            next.Clear();
            next.Add(cell);
        }
        else if (index >= 0)
        {
            next.RemoveAt(index);
        }
        else
        {
            next.Add(cell);
        }

        return next;
    }

    public static List<StudioLedRef> PenCell(
        IReadOnlyList<StudioLedRef> current, StudioLedRef cell, bool erase)
    {
        var next = new List<StudioLedRef>(current);
        var index = IndexOfCell(next, cell);
        if (index >= 0) next.RemoveAt(index);
        else if (!erase) next.Add(cell);
        return next;
    }

    /// <summary>
    /// Official bucket rule: the clicked device's cells inside the region that owns the
    /// cell, otherwise that device's cells not yet covered by a region.
    /// </summary>
    public static List<StudioLedRef> BucketCells(
        IReadOnlyList<StudioRegion> regions, StudioDeviceVisual device, int row, int column)
    {
        ArgumentNullException.ThrowIfNull(regions);
        ArgumentNullException.ThrowIfNull(device);
        var next = new List<StudioLedRef>();
        foreach (var region in regions)
        {
            foreach (var target in region.Devices)
            {
                if (!SameInstance(target.InstanceId, device.InstanceId)) continue;
                if (!target.Leds.Any(led => led.Row == row && led.Col == column)) continue;
                foreach (var led in target.Leds)
                {
                    var cell = CellFor(device, led.Row, led.Col);
                    if (IndexOfCell(next, cell) < 0) next.Add(cell);
                }

                return next;
            }
        }

        var covered = new HashSet<(int Row, int Col)>();
        foreach (var region in regions)
        {
            foreach (var target in region.Devices)
            {
                if (!SameInstance(target.InstanceId, device.InstanceId)) continue;
                foreach (var led in target.Leds) covered.Add((led.Row, led.Col));
            }
        }

        foreach (var led in device.Leds)
        {
            if (covered.Contains((led.MatrixRow, led.MatrixCol))) continue;
            var cell = CellFor(device, led.MatrixRow, led.MatrixCol);
            if (IndexOfCell(next, cell) < 0) next.Add(cell);
        }

        return next;
    }

    /// <summary>Every distinct LED of one device, tagged with its instance.</summary>
    public static List<StudioLedRef> DeviceCells(StudioDeviceVisual device)
    {
        ArgumentNullException.ThrowIfNull(device);
        var next = new List<StudioLedRef>();
        foreach (var led in device.Leds)
        {
            var cell = CellFor(device, led.MatrixRow, led.MatrixCol);
            if (IndexOfCell(next, cell) < 0) next.Add(cell);
        }

        return next;
    }
}
