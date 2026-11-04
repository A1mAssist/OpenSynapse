namespace OpenSynapse.Core.Lighting;

/// <summary>
/// One editor cell. A single visual key can drive several hardware LEDs, so the
/// frame writer needs every device position that shares that matrix position.
/// </summary>
public readonly record struct StudioMatrixEditorCell(
    IReadOnlyList<(int Row, int Column)> DevicePositions)
{
    public bool IsPlaceholder => DevicePositions is not { Count: > 0 };
    public int DeviceRow => DevicePositions is { Count: > 0 } positions ? positions[0].Row : -1;
    public int DeviceCol => DevicePositions is { Count: > 0 } positions ? positions[0].Column : -1;
}

/// <summary>
/// Builds the device-card matrix editor in the manifest's visual grid, so keys keep
/// their real gaps instead of being packed into the HID frame's padding columns.
/// Writes still use the associated DeviceRow/DeviceCol pairs.
/// </summary>
public static class StudioMatrixEditorLayout
{
    public static IReadOnlyList<StudioMatrixEditorCell> Build(StudioLedLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        if (layout.MatrixRows <= 0 || layout.MatrixColumns <= 0 ||
            layout.MatrixRows > 256 || layout.MatrixColumns > 256)
        {
            return BuildUniform(layout.DeviceRows, layout.DeviceColumns);
        }

        var byMatrix = new Dictionary<(int Row, int Col), List<(int Row, int Col)>>();
        foreach (var led in layout.Leds)
        {
            if (led.DeviceRow < 0 || led.DeviceRow >= layout.DeviceRows ||
                led.DeviceCol < 0 || led.DeviceCol >= layout.DeviceColumns)
            {
                continue;
            }

            if (!byMatrix.TryGetValue((led.MatrixRow, led.MatrixCol), out var positions))
            {
                positions = [];
                byMatrix[(led.MatrixRow, led.MatrixCol)] = positions;
            }
            positions.Add((led.DeviceRow, led.DeviceCol));
        }

        var cells = new List<StudioMatrixEditorCell>(layout.MatrixRows * layout.MatrixColumns);
        for (var row = 0; row < layout.MatrixRows; row++)
        {
            for (var column = 0; column < layout.MatrixColumns; column++)
            {
                cells.Add(byMatrix.TryGetValue((row, column), out var positions)
                    ? new StudioMatrixEditorCell(positions)
                    : new StudioMatrixEditorCell([]));
            }
        }

        return cells;
    }

    public static IReadOnlyList<StudioMatrixEditorCell> BuildUniform(int deviceRows, int deviceColumns)
    {
        var cells = new List<StudioMatrixEditorCell>(Math.Max(0, deviceRows) * Math.Max(0, deviceColumns));
        for (var row = 0; row < deviceRows; row++)
        {
            for (var column = 0; column < deviceColumns; column++)
            {
                cells.Add(new StudioMatrixEditorCell([(row, column)]));
            }
        }

        return cells;
    }
}
