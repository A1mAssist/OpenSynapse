using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Lighting;

/// <summary>
/// Product 710 LED layout from Synapse DeviceManifest_710_0.json.
/// Renderers use the 7 x 16 logical canvas; HID reports use the sparse 6 x 17 device frame.
/// </summary>
public static class BladeLightingLayout
{
    public const int LogicalRows = 7;
    public const int LogicalColumns = 16;
    public const int LogicalPixelCount = LogicalRows * LogicalColumns;
    public const int DeviceRows = BladeLightingProtocol.Rows;
    public const int DeviceColumns = BladeLightingProtocol.Columns;
    public const int DevicePixelCount = DeviceRows * DeviceColumns;

    private static readonly sbyte[] LogicalToDeviceIndex =
    [
        1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16,
        18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, -1, 32, 33,
        35, 36, 37, 38, 39, 40, 41, 42, 43, 44, 45, 46, 47, -1, 49, 50,
        52, 53, 54, 55, 56, 57, 58, 59, 60, 61, 62, 63, -1, -1, 66, 67,
        69, -1, 71, 72, 73, 74, 75, 76, 77, 78, 79, 80, -1, -1, 83, 84,
        86, 87, 88, 90, -1, -1, 91, -1, -1, 94, 95, 96, 97, 98, 99, 101,
        -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, 100, -1, -1,
    ];

    public static RazerRgb[] MapToDeviceFrame(IReadOnlyList<RazerRgb> logicalFrame)
    {
        ArgumentNullException.ThrowIfNull(logicalFrame);
        if (logicalFrame.Count != LogicalPixelCount)
        {
            throw new ArgumentException("Blade 逻辑灯光帧必须正好包含 7 x 16 个颜色。", nameof(logicalFrame));
        }

        var deviceFrame = new RazerRgb[DevicePixelCount];
        for (var logicalIndex = 0; logicalIndex < LogicalToDeviceIndex.Length; logicalIndex++)
        {
            var deviceIndex = LogicalToDeviceIndex[logicalIndex];
            if (deviceIndex >= 0)
            {
                deviceFrame[deviceIndex] = logicalFrame[logicalIndex];
            }
        }

        return deviceFrame;
    }

    public static bool TryGetDevicePosition(
        int logicalRow,
        int logicalColumn,
        out int deviceRow,
        out int deviceColumn)
    {
        if ((uint)logicalRow >= LogicalRows || (uint)logicalColumn >= LogicalColumns)
        {
            deviceRow = -1;
            deviceColumn = -1;
            return false;
        }

        var deviceIndex = LogicalToDeviceIndex[logicalRow * LogicalColumns + logicalColumn];
        if (deviceIndex < 0)
        {
            deviceRow = -1;
            deviceColumn = -1;
            return false;
        }

        deviceRow = deviceIndex / DeviceColumns;
        deviceColumn = deviceIndex % DeviceColumns;
        return true;
    }
}
