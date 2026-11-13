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
    // A PnP GPU switch or a driver restart re-enumerates the adapter under a new LUID,
    // so the cache is refreshed on read instead of being frozen for the process lifetime.
    private Dictionary<string, GpuAdapter> _adapters = TryEnumerateAdapters()
        .ToDictionary(adapter => adapter.Luid, StringComparer.OrdinalIgnoreCase);
    // Counter creation expands every GPU engine instance and costs seconds on the
    // first call, so keep it on the sampling thread instead of the UI constructor.
    private readonly Lazy<PdhWildcardCounter?> _engineUsage =
        new(() => PdhWildcardCounter.TryCreate(@"\GPU Engine(*)\Utilization Percentage"));
    private readonly Lazy<PdhWildcardCounter?> _memoryDedicated =
        new(() => PdhWildcardCounter.TryCreate(@"\GPU Adapter Memory(*)\Dedicated Usage"));
    private readonly Lazy<PdhWildcardCounter?> _memoryShared =
        new(() => PdhWildcardCounter.TryCreate(@"\GPU Adapter Memory(*)\Shared Usage"));

    internal IReadOnlyList<WindowsGpuSample> Read()
    {
        try
        {
            var adapters = RefreshAdapters();
            var engines = ReadEngines(adapters);
            var memory = ReadMemory(adapters);
            return adapters.Values.Select(adapter =>
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

    internal (uint VendorId, double? TemperatureCelsius)? ReadSelectedTemperature()
    {
        try
        {
            var selected = SelectDisplayGpu(Read());
            return selected is null
                ? null
                : (selected.VendorId, selected.TemperatureCelsius);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or UnauthorizedAccessException or
            PlatformNotSupportedException or ExternalException)
        {
            return null;
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

    /// <summary>
    /// The adapter whose counters feed the overview and fan curve. Prefer an active
    /// NVIDIA GPU, then a discrete AMD adapter, then the integrated adapter.
    /// </summary>
    internal static WindowsGpuSample? SelectDisplayGpu(IReadOnlyList<WindowsGpuSample> samples) =>
        samples
            .Where(sample => sample.VendorId == NvidiaVendorId && sample.ExternalUsagePercent >= 0.1)
            .OrderByDescending(sample => sample.ExternalUsagePercent)
            .FirstOrDefault()
        ?? samples
            .Where(sample => sample.VendorId == AmdVendorId && !sample.IsIntegrated)
            .OrderByDescending(sample => sample.ExternalUsagePercent)
            .FirstOrDefault()
        ?? SelectIntegrated(samples);

    private Dictionary<string, GpuAdapter> RefreshAdapters()
    {
        var enumerated = TryEnumerateAdapters();
        if (!NeedsAdapterRefresh(
                _adapters.Keys,
                enumerated.Select(adapter => adapter.Luid).ToArray()))
        {
            return _adapters;
        }

        var refreshed = enumerated.ToDictionary(adapter => adapter.Luid, StringComparer.OrdinalIgnoreCase);
        _adapters = refreshed;
        return refreshed;
    }

    /// <summary>
    /// True when DXGI reports a different adapter set than the cache holds. An empty
    /// round is a failed enumeration, not a machine without GPUs, so it keeps the cache.
    /// </summary>
    internal static bool NeedsAdapterRefresh(
        IReadOnlyCollection<string> cachedLuids,
        IReadOnlyCollection<string> observedLuids) =>
        observedLuids.Count > 0 &&
        (observedLuids.Count != cachedLuids.Count ||
         observedLuids.Any(luid => !cachedLuids.Contains(luid)));

    public void Dispose()
    {
        DisposeCounter(_engineUsage);
        DisposeCounter(_memoryDedicated);
        DisposeCounter(_memoryShared);
    }

    private static void DisposeCounter(Lazy<PdhWildcardCounter?> counter)
    {
        if (counter.IsValueCreated)
        {
            counter.Value?.Dispose();
        }
    }

    private Dictionary<string, Dictionary<string, EngineUsage>> ReadEngines(
        Dictionary<string, GpuAdapter> adapters)
    {
        var result = new Dictionary<string, Dictionary<string, EngineUsage>>(StringComparer.OrdinalIgnoreCase);
        var engineUsage = _engineUsage.Value;
        if (engineUsage is null)
        {
            return result;
        }

        foreach (var sample in engineUsage.Read())
        {
            var parsed = ParseEngineInstance(sample.Name);
            if (parsed is null || !adapters.ContainsKey(parsed.Value.Luid))
            {
                continue;
            }

            var value = Math.Max(0, sample.Value);
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

    private Dictionary<string, MemoryUsage> ReadMemory(Dictionary<string, GpuAdapter> adapters)
    {
        var result = new Dictionary<string, MemoryUsage>(StringComparer.OrdinalIgnoreCase);
        var dedicatedUsage = _memoryDedicated.Value;
        if (dedicatedUsage is null)
        {
            return result;
        }

        var shared = _memoryShared.Value?.Read()
            .GroupBy(sample => sample.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => Math.Max(0, group.First().Value),
                StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var sample in dedicatedUsage.Read())
        {
            var match = LuidRegex().Match(sample.Name);
            if (!match.Success)
            {
                continue;
            }
            var luid = NormalizeLuid(match);
            if (!adapters.ContainsKey(luid))
            {
                continue;
            }

            result[luid] = new MemoryUsage(
                Math.Max(0, sample.Value),
                shared.GetValueOrDefault(sample.Name));
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
