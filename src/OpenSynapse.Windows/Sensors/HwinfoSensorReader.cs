using Hwinfo.SharedMemory;

namespace OpenSynapse.Windows.Sensors;

internal sealed class HwinfoSensorReader : IDisposable
{
    private readonly SharedMemoryReader _reader = new(new SharedMemoryReaderOptions
    {
        MutexTimeout = TimeSpan.FromMilliseconds(100),
        StalenessTimeout = TimeSpan.FromSeconds(10),
        ReuseUnchangedPolls = true,
    });

    internal double? ReadCpuTemperatureCelsius() =>
        TryRead(readings => SelectCpuTemperature(readings));

    internal double? ReadGpuTemperatureCelsius(string? vendorHint) =>
        TryRead(readings => SelectGpuTemperature(readings, vendorHint));

    private double? TryRead(Func<IEnumerable<SensorReading>, double?> selector)
    {
        try
        {
            return _reader.TryReadLocal(out var readings)
                ? selector(readings.Readings)
                : null;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidDataException or
            TimeoutException or InvalidOperationException)
        {
            return null;
        }
    }

    internal static double? SelectCpuTemperature(IEnumerable<SensorReading> readings)
    {
        var temperatures = readings
            .Where(reading => reading.ReadingType == SensorType.Temp &&
                double.IsFinite(reading.Value) && reading.Value is >= 1 and <= 125 &&
                IsCpuSensor(reading.Sensor))
            .ToArray();
        if (temperatures.Length == 0)
        {
            return null;
        }

        var package = temperatures.FirstOrDefault(reading => HasLabel(reading, "package"));
        if (package.Sensor is not null)
        {
            return package.Value;
        }

        var coreMax = temperatures.FirstOrDefault(reading => HasLabel(reading, "core max"));
        if (coreMax.Sensor is not null)
        {
            return coreMax.Value;
        }

        return temperatures.MaxBy(reading => reading.Value).Value;
    }

    internal static double? SelectGpuTemperature(
        IEnumerable<SensorReading> readings,
        string? vendorHint = null)
    {
        var temperatures = readings
            .Where(reading => reading.ReadingType == SensorType.Temp &&
                double.IsFinite(reading.Value) && reading.Value is >= 1 and <= 125 &&
                IsGpuSensor(reading.Sensor))
            .ToArray();
        if (temperatures.Length == 0)
        {
            return null;
        }

        // A laptop can publish both its integrated and discrete GPU. Prefer the sensor
        // whose name matches the adapter Windows is currently driving.
        SensorReading[] vendorMatches = string.IsNullOrWhiteSpace(vendorHint)
            ? []
            : temperatures
                .Where(reading => SensorName(reading.Sensor).Contains(
                    vendorHint, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        var candidates = vendorMatches.Length > 0 ? vendorMatches : temperatures;
        var standard = candidates.FirstOrDefault(reading =>
            HasLabel(reading, "GPU Temperature") ||
            HasLabel(reading, "GPU Core Temperature") ||
            HasLabel(reading, "GPU Temp"));
        return standard.Sensor is not null
            ? standard.Value
            : candidates.MaxBy(reading => reading.Value).Value;
    }

    public void Dispose() => _reader.Dispose();

    private static bool IsCpuSensor(Sensor sensor)
    {
        var name = SensorName(sensor);
        return !name.Contains("GPU", StringComparison.OrdinalIgnoreCase) &&
            (name.Contains("CPU", StringComparison.OrdinalIgnoreCase) ||
             name.Contains("Processor", StringComparison.OrdinalIgnoreCase) ||
             name.Contains("Core Ultra", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsGpuSensor(Sensor sensor)
    {
        var name = SensorName(sensor);
        return name.Contains("GPU", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Graphics", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Arc", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Radeon", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase);
    }

    private static string SensorName(Sensor sensor) =>
        $"{sensor.NameUser} {sensor.NameOrig}";

    private static bool HasLabel(SensorReading reading, string value) =>
        reading.LabelUser.Contains(value, StringComparison.OrdinalIgnoreCase) ||
        reading.LabelOrig.Contains(value, StringComparison.OrdinalIgnoreCase);
}
