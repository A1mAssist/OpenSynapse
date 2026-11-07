using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Lighting;

/// <summary>
/// Product 695/696 LED layout from Synapse DeviceManifest_695_0.json and
/// DeviceManifest_696_0.json. Renderers use the 7 x 15 logical canvas; HID
/// reports use the sparse 6 x 16 device frame.
/// </summary>
public static class BladeLightingLayout16
{
    public const int LogicalRows = 7;
    public const int LogicalColumns = 15;
    public const int LogicalPixelCount = LogicalRows * LogicalColumns;
    public const int DeviceRows = 6;
    public const int DeviceColumns = 16;
    public const int DevicePixelCount = DeviceRows * DeviceColumns;

    private static readonly sbyte[] LogicalToDeviceIndex =
    [
        1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15,
        17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, -1, 31,
        33, 34, 35, 36, 37, 38, 39, 40, 41, 42, 43, 44, 45, -1, 47,
        49, 50, 51, 52, 53, 54, 55, 56, 57, 58, 59, 60, -1, -1, 63,
        65, -1, 67, 68, 69, 70, 71, 72, 73, 74, 75, 76, -1, -1, 79,
        81, 82, 83, 85, -1, -1, 86, -1, -1, 89, 90, 91, 92, 93, 94,
        -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, 95, -1,
    ];

    public static RazerRgb[] MapToDeviceFrame(IReadOnlyList<RazerRgb> logicalFrame)
    {
        ArgumentNullException.ThrowIfNull(logicalFrame);
        if (logicalFrame.Count != LogicalPixelCount)
        {
            throw new ArgumentException(
                "A Blade 16 2024 logical frame must contain exactly 7 x 15 colors.",
                nameof(logicalFrame));
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
