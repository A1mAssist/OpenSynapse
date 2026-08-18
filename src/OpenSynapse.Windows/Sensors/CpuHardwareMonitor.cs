using System.Runtime.InteropServices;

namespace OpenSynapse.Windows.Sensors;

internal sealed class CpuHardwareMonitor : IDisposable
{
    private readonly PdhWildcardCounter? _temperature =
        PdhWildcardCounter.TryCreate(@"\Thermal Zone Information(*)\Temperature");
    private readonly PdhWildcardCounter? _power =
        PdhWildcardCounter.TryCreate(@"\Energy Meter(*)\Power");

    internal CpuHardwareSample Read() => new(
        SelectTemperatureCelsius(_temperature?.Read().Select(sample => sample.Value) ?? []),
        SelectPackagePowerWatts(_power?.Read() ?? []),
        ReadWindowsClock());

    public void Dispose()
    {
        _temperature?.Dispose();
        _power?.Dispose();
    }

    internal static double? SelectTemperatureCelsius(IEnumerable<double> kelvinValues)
    {
        var temperatures = kelvinValues
            .Where(value => double.IsFinite(value) && value is >= 200 and <= 500)
            .Select(value => value - 273.15)
            .Where(value => value is >= 1 and <= 125)
            .ToArray();
        return temperatures.Length == 0 ? null : temperatures.Max();
    }

    internal static double? SelectPackagePowerWatts(IEnumerable<PdhSample> samples)
    {
        var milliwatts = samples
            .Where(sample => IsRaplPackage(sample.Name))
            .Select(sample => sample.Value)
            .Where(value => double.IsFinite(value) && value is >= 100 and <= 500_000)
            .ToArray();
        return milliwatts.Length == 0 ? null : milliwatts.Sum() / 1000;
    }

    private static bool IsRaplPackage(string name)
    {
        const string prefix = "rapl_package";
        const string suffix = "_pkg";
        if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            !name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var package = name.AsSpan(prefix.Length, name.Length - prefix.Length - suffix.Length);
        return !package.IsEmpty && package.IndexOfAnyExceptInRange('0', '9') < 0;
    }

    private static int? ReadWindowsClock()
    {
        var processors = new ProcessorPowerInformation[Environment.ProcessorCount];
        var status = CallNtPowerInformation(
            11,
            IntPtr.Zero,
            0,
            processors,
            checked((uint)(processors.Length * Marshal.SizeOf<ProcessorPowerInformation>())));
        return status == 0
            ? SelectFastestCoreClock(processors.Select(item => item.CurrentMhz))
            : null;
    }

    internal static int? SelectFastestCoreClock(IEnumerable<uint> clocks)
    {
        var valid = clocks
            .Where(clock => clock is > 0 and <= 10000)
            .Select(clock => (int)clock)
            .ToArray();
        return valid.Length == 0 ? null : valid.Max();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessorPowerInformation
    {
        internal uint Number;
        internal uint MaxMhz;
        internal uint CurrentMhz;
        internal uint MhzLimit;
        internal uint MaxIdleState;
        internal uint CurrentIdleState;
    }

    [DllImport("powrprof.dll")]
    private static extern uint CallNtPowerInformation(
        int informationLevel,
        IntPtr inputBuffer,
        uint inputBufferLength,
        [Out] ProcessorPowerInformation[] outputBuffer,
        uint outputBufferLength);
}

internal readonly record struct PdhSample(string Name, double Value);

internal sealed class PdhWildcardCounter : IDisposable
{
    private const uint PdhFormatDouble = 0x00000200;
    private const uint PdhMoreData = 0x800007D2;

    private IntPtr _query;
    private readonly IntPtr _counter;

    private PdhWildcardCounter(IntPtr query, IntPtr counter)
    {
        _query = query;
        _counter = counter;
    }

    internal static PdhWildcardCounter? TryCreate(string path)
    {
        if (PdhOpenQuery(null, UIntPtr.Zero, out var query) != 0)
        {
            return null;
        }

        if (PdhAddEnglishCounter(query, path, UIntPtr.Zero, out var counter) == 0)
        {
            PdhCollectQueryData(query);
            return new PdhWildcardCounter(query, counter);
        }

        PdhCloseQuery(query);
        return null;
    }

    internal IReadOnlyList<PdhSample> Read()
    {
        if (_query == IntPtr.Zero || PdhCollectQueryData(_query) != 0)
        {
            return [];
        }

        uint bufferSize = 0;
        var status = PdhGetFormattedCounterArray(
            _counter,
            PdhFormatDouble,
            ref bufferSize,
            out var itemCount,
            IntPtr.Zero);
        if (status != PdhMoreData || bufferSize == 0)
        {
            return [];
        }

        var buffer = Marshal.AllocHGlobal(checked((int)bufferSize));
        try
        {
            status = PdhGetFormattedCounterArray(
                _counter,
                PdhFormatDouble,
                ref bufferSize,
                out itemCount,
                buffer);
            if (status != 0)
            {
                return [];
            }

            var result = new List<PdhSample>(checked((int)itemCount));
            var itemSize = Marshal.SizeOf<PdhFormattedCounterValueItem>();
            for (var index = 0; index < itemCount; index++)
            {
                var item = Marshal.PtrToStructure<PdhFormattedCounterValueItem>(
                    IntPtr.Add(buffer, checked((int)index * itemSize)));
                if (item.Value.Status is 0 or 1 &&
                    Marshal.PtrToStringUni(item.Name) is { Length: > 0 } name)
                {
                    result.Add(new PdhSample(name, item.Value.Value));
                }
            }
            return result;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public void Dispose()
    {
        if (_query != IntPtr.Zero)
        {
            PdhCloseQuery(_query);
            _query = IntPtr.Zero;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct PdhFormattedCounterValue
    {
        internal readonly uint Status;
        internal readonly double Value;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct PdhFormattedCounterValueItem
    {
        internal readonly IntPtr Name;
        internal readonly PdhFormattedCounterValue Value;
    }

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhOpenQuery(
        string? dataSource,
        UIntPtr userData,
        out IntPtr query);

    [DllImport("pdh.dll", EntryPoint = "PdhAddEnglishCounterW", CharSet = CharSet.Unicode)]
    private static extern uint PdhAddEnglishCounter(
        IntPtr query,
        string fullCounterPath,
        UIntPtr userData,
        out IntPtr counter);

    [DllImport("pdh.dll")]
    private static extern uint PdhCollectQueryData(IntPtr query);

    [DllImport("pdh.dll", EntryPoint = "PdhGetFormattedCounterArrayW", CharSet = CharSet.Unicode)]
    private static extern uint PdhGetFormattedCounterArray(
        IntPtr counter,
        uint format,
        ref uint bufferSize,
        out uint itemCount,
        IntPtr itemBuffer);

    [DllImport("pdh.dll")]
    private static extern uint PdhCloseQuery(IntPtr query);
}

internal readonly record struct CpuHardwareSample(
    double? TemperatureCelsius,
    double? PowerWatts,
    int? ClockMegahertz);
