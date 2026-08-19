using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace OpenSynapse.Windows.Sensors;

internal sealed record WindowsGpuSample(
    string Name,
    uint VendorId,
    double UsagePercent,
    double ExternalUsagePercent,
    long MemoryUsedMebibytes,
    long MemoryTotalMebibytes,
    bool IsIntegrated = false,
    double? TemperatureCelsius = null);

internal sealed partial class WindowsGpuActivityReader : IDisposable
{
    private const uint NvidiaVendorId = 0x10DE;
    private const uint AmdVendorId = 0x1002;
    private const uint IntelVendorId = 0x8086;
    private const uint DxgiAdapterFlagSoftware = 0x02;
    private const int DxgiErrorNotFound = unchecked((int)0x887A0002);
    private readonly Dictionary<string, GpuAdapter> _adapters = TryEnumerateAdapters()
        .ToDictionary(adapter => adapter.Luid, StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PerformanceCounter> _engineCounters =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, MemoryCounters> _memoryCounters =
        new(StringComparer.OrdinalIgnoreCase);

    internal IReadOnlyList<WindowsGpuSample> Read()
    {
        try
        {
            var engines = ReadEngines();
            var memory = ReadMemory();
            return _adapters.Values.Select(adapter =>
            {
                var adapterEngines = engines.GetValueOrDefault(adapter.Luid);
                var usage = adapterEngines is null ? 0 : adapterEngines.Values.Max(engine => engine.Total);
                var externalUsage = adapterEngines is null ? 0 : adapterEngines.Values.Max(engine => engine.External);
                var adapterMemory = memory.GetValueOrDefault(adapter.Luid);
                var usedMemory = adapter.IsIntegrated
                    ? adapterMemory.SharedBytes
                    : adapterMemory.DedicatedBytes;
                return new WindowsGpuSample(
                    adapter.Name,
                    adapter.VendorId,
                    Math.Clamp(usage, 0, 100),
                    Math.Clamp(externalUsage, 0, 100),
                    ToMebibytes(usedMemory),
                    ToMebibytes(adapter.IsIntegrated
                        ? adapter.SharedMemoryBytes
                        : adapter.DedicatedMemoryBytes),
                    adapter.IsIntegrated,
                    ReadTemperature(adapter.NativeLuid));
            }).ToArray();
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or UnauthorizedAccessException or
            PlatformNotSupportedException or ExternalException)
        {
            return [];
        }
    }

    internal static bool IsNvidiaActive(IReadOnlyList<WindowsGpuSample> samples) =>
        samples.Any(sample => sample.VendorId == NvidiaVendorId && sample.ExternalUsagePercent >= 0.1);

    internal static WindowsGpuSample? SelectNvidia(IReadOnlyList<WindowsGpuSample> samples) =>
        samples
            .Where(sample => sample.VendorId == NvidiaVendorId)
            .OrderByDescending(sample => sample.ExternalUsagePercent)
            .FirstOrDefault();

    internal static WindowsGpuSample? SelectIntegrated(IReadOnlyList<WindowsGpuSample> samples) =>
        samples
            .Where(sample => sample.IsIntegrated)
            .OrderByDescending(sample => sample.UsagePercent)
            .FirstOrDefault();

    public void Dispose()
    {
        foreach (var counter in _engineCounters.Values)
        {
            counter.Dispose();
        }
        foreach (var counters in _memoryCounters.Values)
        {
            counters.Dispose();
        }
        _engineCounters.Clear();
        _memoryCounters.Clear();
    }

    private Dictionary<string, Dictionary<string, EngineUsage>> ReadEngines()
    {
        var result = new Dictionary<string, Dictionary<string, EngineUsage>>(StringComparer.OrdinalIgnoreCase);
        var instances = new PerformanceCounterCategory("GPU Engine").GetInstanceNames();
        RemoveStale(_engineCounters, instances);
        foreach (var instance in instances)
        {
            var parsed = ParseEngineInstance(instance);
            if (parsed is null || !_adapters.ContainsKey(parsed.Value.Luid))
            {
                continue;
            }

            if (!_engineCounters.TryGetValue(instance, out var counter))
            {
                counter = new PerformanceCounter(
                    "GPU Engine", "Utilization Percentage", instance, readOnly: true);
                _engineCounters.Add(instance, counter);
            }

            var value = Math.Max(0, counter.NextValue());
            var byEngine = result.GetValueOrDefault(parsed.Value.Luid);
            if (byEngine is null)
            {
                byEngine = new Dictionary<string, EngineUsage>(StringComparer.OrdinalIgnoreCase);
                result.Add(parsed.Value.Luid, byEngine);
            }
            var current = byEngine.GetValueOrDefault(parsed.Value.Engine);
            byEngine[parsed.Value.Engine] = new EngineUsage(
                current.Total + value,
                current.External + (parsed.Value.ProcessId == Environment.ProcessId ? 0 : value));
        }
        return result;
    }

    private Dictionary<string, MemoryUsage> ReadMemory()
    {
        var result = new Dictionary<string, MemoryUsage>(StringComparer.OrdinalIgnoreCase);
        var instances = new PerformanceCounterCategory("GPU Adapter Memory").GetInstanceNames();
        RemoveStale(_memoryCounters, instances);
        foreach (var instance in instances)
        {
            var match = LuidRegex().Match(instance);
            if (!match.Success)
            {
                continue;
            }
            var luid = NormalizeLuid(match);
            if (!_adapters.ContainsKey(luid))
            {
                continue;
            }

            if (!_memoryCounters.TryGetValue(instance, out var counters))
            {
                counters = new MemoryCounters(instance);
                _memoryCounters.Add(instance, counters);
            }
            result[luid] = new MemoryUsage(
                Math.Max(0, counters.Dedicated.NextValue()),
                Math.Max(0, counters.Shared.NextValue()));
        }
        return result;
    }

    private static EngineInstance? ParseEngineInstance(string instance)
    {
        var match = EngineRegex().Match(instance);
        if (!match.Success ||
            !int.TryParse(match.Groups["pid"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var pid))
        {
            return null;
        }
        return new EngineInstance(
            NormalizeLuid(match),
            match.Groups["engine"].Value,
            pid);
    }

    private static string NormalizeLuid(Match match) =>
        $"luid_0x{uint.Parse(match.Groups["high"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture):x8}_" +
        $"0x{uint.Parse(match.Groups["low"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture):x8}";

    private static void RemoveStale<T>(Dictionary<string, T> counters, IReadOnlyCollection<string> instances)
        where T : IDisposable
    {
        var current = instances.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var stale in counters.Keys.Where(key => !current.Contains(key)).ToArray())
        {
            counters.Remove(stale, out var counter);
            counter?.Dispose();
        }
    }

    private static long ToMebibytes(double bytes) => checked((long)Math.Round(bytes / 1048576d));

    private static IReadOnlyList<GpuAdapter> EnumerateAdapters()
    {
        var result = new List<GpuAdapter>();
        var factoryId = new Guid("770AAE78-F26F-4DBA-A829-253C83D1B387");
        var status = CreateDXGIFactory1(ref factoryId, out var factory);
        if (status < 0)
        {
            Marshal.ThrowExceptionForHR(status);
        }

        try
        {
            var factoryVtable = Marshal.ReadIntPtr(factory);
            var enumAdapters = Marshal.GetDelegateForFunctionPointer<EnumAdapters1Delegate>(
                Marshal.ReadIntPtr(factoryVtable, IntPtr.Size * 12));
            for (uint index = 0; ; index++)
            {
                status = enumAdapters(factory, index, out var adapter);
                if (status == DxgiErrorNotFound)
                {
                    break;
                }
                if (status < 0)
                {
                    Marshal.ThrowExceptionForHR(status);
                }

                try
                {
                    var adapterVtable = Marshal.ReadIntPtr(adapter);
                    var getDescription = Marshal.GetDelegateForFunctionPointer<GetDesc1Delegate>(
                        Marshal.ReadIntPtr(adapterVtable, IntPtr.Size * 10));
                    status = getDescription(adapter, out var description);
                    if (status < 0)
                    {
                        Marshal.ThrowExceptionForHR(status);
                    }
                    if ((description.Flags & DxgiAdapterFlagSoftware) == 0)
                    {
                        var isIntegrated = IsIntegrated(description);
                        result.Add(new GpuAdapter(
                            description.Description.TrimEnd('\0'),
                            description.VendorId,
                            FormatLuid(description.AdapterLuid),
                            description.AdapterLuid,
                            description.DedicatedVideoMemory,
                            description.SharedSystemMemory,
                            isIntegrated));
                    }
                }
                finally
                {
                    _ = Marshal.Release(adapter);
                }
            }
        }
        finally
        {
            _ = Marshal.Release(factory);
        }
        return result;
    }

    private static IReadOnlyList<GpuAdapter> TryEnumerateAdapters()
    {
        try
        {
            return EnumerateAdapters();
        }
        catch (Exception exception) when (
            exception is ExternalException or PlatformNotSupportedException)
        {
            return [];
        }
    }

    private static string FormatLuid(Luid luid) =>
        $"luid_0x{unchecked((uint)luid.HighPart):x8}_0x{luid.LowPart:x8}";

    private static bool IsIntegrated(DxgiAdapterDescription1 description)
    {
        if (TryQueryAdapter(description.AdapterLuid, AdapterInfoType, out AdapterType type) &&
            (type.Value & HybridIntegratedFlag) != 0)
        {
            return true;
        }

        return description.VendorId is AmdVendorId or IntelVendorId &&
            description.DedicatedVideoMemory < 1024u * 1024 * 1024;
    }

    private static double? ReadTemperature(Luid luid)
    {
        if (!TryQueryAdapter(luid, AdapterPerformanceDataType, out AdapterPerformanceData data))
        {
            return null;
        }

        var temperature = data.Temperature / 10d;
        return temperature is >= 1 and <= 125 ? temperature : null;
    }

    private static bool TryQueryAdapter<T>(Luid luid, int type, out T data) where T : struct
    {
        data = default;
        var open = new OpenAdapterFromLuid { AdapterLuid = luid };
        try
        {
            if (D3DKMTOpenAdapterFromLuid(ref open) != 0)
            {
                return false;
            }

            var size = Marshal.SizeOf<T>();
            var buffer = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(data, buffer, false);
                var query = new QueryAdapterInfo
                {
                    AdapterHandle = open.AdapterHandle,
                    Type = type,
                    Data = buffer,
                    DataSize = checked((uint)size),
                };
                if (D3DKMTQueryAdapterInfo(ref query) != 0)
                {
                    return false;
                }

                data = Marshal.PtrToStructure<T>(buffer);
                return true;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch (Exception exception) when (
            exception is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            return false;
        }
        finally
        {
            if (open.AdapterHandle != 0)
            {
                var close = new CloseAdapter { AdapterHandle = open.AdapterHandle };
                _ = D3DKMTCloseAdapter(ref close);
            }
        }
    }

    [GeneratedRegex(@"luid_0x(?<high>[0-9a-f]+)_0x(?<low>[0-9a-f]+)", RegexOptions.IgnoreCase)]
    private static partial Regex LuidRegex();

    [GeneratedRegex(@"^pid_(?<pid>\d+)_luid_0x(?<high>[0-9a-f]+)_0x(?<low>[0-9a-f]+)_phys_\d+_(?<engine>eng_\d+_engtype_.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex EngineRegex();

    [DllImport("dxgi.dll")]
    private static extern int CreateDXGIFactory1(ref Guid factoryId, out nint factory);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int EnumAdapters1Delegate(nint factory, uint index, out nint adapter);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetDesc1Delegate(nint adapter, out DxgiAdapterDescription1 description);

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DxgiAdapterDescription1
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Description;
        public uint VendorId;
        public uint DeviceId;
        public uint SubSystemId;
        public uint Revision;
        public nuint DedicatedVideoMemory;
        public nuint DedicatedSystemMemory;
        public nuint SharedSystemMemory;
        public Luid AdapterLuid;
        public uint Flags;
    }

    private sealed record GpuAdapter(
        string Name,
        uint VendorId,
        string Luid,
        Luid NativeLuid,
        double DedicatedMemoryBytes,
        double SharedMemoryBytes,
        bool IsIntegrated);
    private readonly record struct EngineInstance(string Luid, string Engine, int ProcessId);
    private readonly record struct EngineUsage(double Total, double External);
    private readonly record struct MemoryUsage(double DedicatedBytes, double SharedBytes);

    private sealed class MemoryCounters(string instance) : IDisposable
    {
        internal PerformanceCounter Dedicated { get; } =
            new("GPU Adapter Memory", "Dedicated Usage", instance, readOnly: true);
        internal PerformanceCounter Shared { get; } =
            new("GPU Adapter Memory", "Shared Usage", instance, readOnly: true);

        public void Dispose()
        {
            Dedicated.Dispose();
            Shared.Dispose();
        }
    }

    private const int AdapterInfoType = 15;
    private const int AdapterPerformanceDataType = 62;
    private const uint HybridIntegratedFlag = 1u << 5;

    [StructLayout(LayoutKind.Sequential)]
    private struct OpenAdapterFromLuid
    {
        internal Luid AdapterLuid;
        internal uint AdapterHandle;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CloseAdapter
    {
        internal uint AdapterHandle;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct QueryAdapterInfo
    {
        internal uint AdapterHandle;
        internal int Type;
        internal nint Data;
        internal uint DataSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AdapterType
    {
        internal uint Value;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AdapterPerformanceData
    {
        internal uint PhysicalAdapterIndex;
        internal ulong MemoryFrequency;
        internal ulong MaxMemoryFrequency;
        internal ulong MaxMemoryFrequencyOverclocked;
        internal ulong MemoryBandwidth;
        internal ulong PcieBandwidth;
        internal uint FanRpm;
        internal uint Power;
        internal uint Temperature;
        internal byte PowerStateOverride;
    }

    [DllImport("gdi32.dll")]
    private static extern int D3DKMTOpenAdapterFromLuid(ref OpenAdapterFromLuid data);

    [DllImport("gdi32.dll")]
    private static extern int D3DKMTQueryAdapterInfo(ref QueryAdapterInfo data);

    [DllImport("gdi32.dll")]
    private static extern int D3DKMTCloseAdapter(ref CloseAdapter data);
}
