using System.Globalization;

namespace OpenSynapse.Windows.Sensors;

public sealed record NvidiaGpuSample(
    string Name,
    double? TemperatureCelsius,
    double? UsagePercent,
    double? PowerWatts,
    int? ClockMegahertz,
    long? MemoryUsedMebibytes,
    long? MemoryTotalMebibytes);

public static class NvidiaSmiOutputParser
{
    public static NvidiaGpuSample? Parse(string? output)
    {
        var line = output?.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (line is null)
        {
            return null;
        }

        var values = line.Split(',', StringSplitOptions.TrimEntries);
        if (values.Length != 7 || string.IsNullOrWhiteSpace(values[0]))
        {
            return null;
        }

        return new NvidiaGpuSample(
            values[0],
            ParseDouble(values[1]),
            ParseDouble(values[2]),
            ParseDouble(values[3]),
            ParseInt(values[4]),
            ParseLong(values[5]),
            ParseLong(values[6]));
    }

    private static double? ParseDouble(string value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) ? result : null;

    private static int? ParseInt(string value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : null;

    private static long? ParseLong(string value) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : null;
}
