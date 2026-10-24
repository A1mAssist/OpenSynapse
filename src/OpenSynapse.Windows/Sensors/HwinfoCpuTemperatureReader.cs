using Hwinfo.SharedMemory;

namespace OpenSynapse.Windows.Sensors;

internal sealed class HwinfoCpuTemperatureReader : IDisposable
{
    private readonly SharedMemoryReader _reader = new(new SharedMemoryReaderOptions
    {
        MutexTimeout = TimeSpan.FromMilliseconds(100),
        StalenessTimeout = TimeSpan.FromSeconds(10),
        ReuseUnchangedPolls = true,
    });

    internal double? ReadTemperatureCelsius()
    {
        try
        {
            return _reader.TryReadLocal(out var readings)
                ? SelectCpuTemperature(readings.Readings)
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

    public void Dispose() => _reader.Dispose();

    private static bool IsCpuSensor(Sensor sensor)
    {
        var name = $"{sensor.NameUser} {sensor.NameOrig}";
        return !name.Contains("GPU", StringComparison.OrdinalIgnoreCase) &&
            (name.Contains("CPU", StringComparison.OrdinalIgnoreCase) ||
             name.Contains("Processor", StringComparison.OrdinalIgnoreCase) ||
             name.Contains("Core Ultra", StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasLabel(SensorReading reading, string value) =>
        reading.LabelUser.Contains(value, StringComparison.OrdinalIgnoreCase) ||
        reading.LabelOrig.Contains(value, StringComparison.OrdinalIgnoreCase);
}
