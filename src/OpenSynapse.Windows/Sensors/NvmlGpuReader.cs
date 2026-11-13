using System.Runtime.InteropServices;

namespace OpenSynapse.Windows.Sensors;

public sealed record NvidiaGpuSample(
    string Name,
    double? TemperatureCelsius,
    double? UsagePercent,
    double? PowerWatts,
    int? ClockMegahertz,
    long? MemoryUsedMebibytes,
    long? MemoryTotalMebibytes);

/// <summary>
/// Reads NVIDIA board telemetry through NVML, the library nvidia-smi itself wraps.
/// One in-process call replaces one process launch: no PATH dependency, no timeout
/// to abandon, no orphan when the driver stalls.
/// </summary>
internal sealed class NvmlGpuReader : IDisposable
{
    private const int NvmlSuccess = 0;
    private const uint TemperatureSensorGpu = 0;
    private const uint ClockGraphics = 0;
    private const int NameBufferLength = 96;
    private const uint MemoryInfoV2Version = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct Utilization
    {
        internal uint Gpu;
        internal uint Memory;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryInfo
    {
        internal ulong Total;
        internal ulong Free;
        internal ulong Used;
    }

    // v2 reports driver-reserved memory separately, so Used matches what nvidia-smi
    // prints. The v1 struct folds the reservation into Used (260 MiB on an idle card).
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryInfoV2
    {
        internal uint Version;
        internal ulong Total;
        internal ulong Reserved;
        internal ulong Free;
        internal ulong Used;
    }

    private delegate int InitDelegate();
    private delegate int ShutdownDelegate();
    private delegate int GetHandleDelegate(uint index, out IntPtr device);
    private delegate int GetNameDelegate(IntPtr device, IntPtr name, uint length);
    private delegate int GetTemperatureDelegate(IntPtr device, uint sensor, out uint temperature);
    private delegate int GetUtilizationDelegate(IntPtr device, out Utilization utilization);
    private delegate int GetPowerDelegate(IntPtr device, out uint milliwatts);
    private delegate int GetClockDelegate(IntPtr device, uint clock, out uint megahertz);
    private delegate int GetMemoryDelegate(IntPtr device, out MemoryInfo memory);
    private delegate int GetMemoryV2Delegate(IntPtr device, ref MemoryInfoV2 memory);

    private readonly object _gate = new();
    private IntPtr _library;
    private IntPtr _device;
    private InitDelegate? _init;
    private ShutdownDelegate? _shutdown;
    private GetHandleDelegate? _getHandle;
    private GetNameDelegate? _getName;
    private GetTemperatureDelegate? _getTemperature;
    private GetUtilizationDelegate? _getUtilization;
    private GetPowerDelegate? _getPower;
    private GetClockDelegate? _getClock;
    private GetMemoryDelegate? _getMemory;
    private GetMemoryV2Delegate? _getMemoryV2;
    private bool _initialized;
    private bool _unavailable;
    private bool _disposed;

    internal bool TryInitialize(out string? error)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                error = "The NVML reader is disposed.";
                return false;
            }

            return TryInitializeInner(out error);
        }
    }

    internal bool TryRead(out NvidiaGpuSample? sample)
    {
        sample = null;
        lock (_gate)
        {
            if (_disposed || !TryInitializeInner(out _))
            {
                return false;
            }

            var name = ReadName();
            if (name is null)
            {
                return false;
            }

            double? temperature = null;
            double? usage = null;
            double? power = null;
            int? clock = null;
            long? used = null;
            long? total = null;

            if (_getTemperature!(_device, TemperatureSensorGpu, out var celsius) == NvmlSuccess)
            {
                temperature = celsius;
            }

            if (_getUtilization!(_device, out var utilization) == NvmlSuccess)
            {
                usage = utilization.Gpu;
            }

            if (_getPower!(_device, out var milliwatts) == NvmlSuccess)
            {
                power = milliwatts / 1000d;
            }

            if (_getClock!(_device, ClockGraphics, out var megahertz) == NvmlSuccess)
            {
                clock = (int)megahertz;
            }

            if (_getMemoryV2 is not null && TryReadMemoryV2(out var usedMebibytes, out var totalMebibytes))
            {
                used = usedMebibytes;
                total = totalMebibytes;
            }
            else if (_getMemory!(_device, out var memory) == NvmlSuccess)
            {
                used = (long)(memory.Used / (1024 * 1024));
                total = (long)(memory.Total / (1024 * 1024));
            }

            if (temperature is null && usage is null && power is null && clock is null && used is null)
            {
                return false;
            }

            sample = new NvidiaGpuSample(name, temperature, usage, power, clock, used, total);
            return true;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_initialized)
            {
                _shutdown?.Invoke();
            }

            ReleaseLibrary();
        }
    }

    private bool TryReadMemoryV2(out long? used, out long? total)
    {
        var memory = new MemoryInfoV2
        {
            Version = ((uint)Marshal.SizeOf<MemoryInfoV2>()) | (MemoryInfoV2Version << 24),
        };
        if (_getMemoryV2!(_device, ref memory) != NvmlSuccess)
        {
            used = null;
            total = null;
            return false;
        }

        used = (long)(memory.Used / (1024 * 1024));
        total = (long)(memory.Total / (1024 * 1024));
        return true;
    }

    private bool TryInitializeInner(out string? error)
    {
        if (_device != IntPtr.Zero)
        {
            error = null;
            return true;
        }

        if (_unavailable)
        {
            error = "NVML is not available on this machine.";
            return false;
        }

        return Open(out error);
    }

    private bool Open(out string? error)
    {
        try
        {
            _library = NativeLibrary.Load("nvml.dll");
            _init = Bind<InitDelegate>("nvmlInit_v2");
            _shutdown = Bind<ShutdownDelegate>("nvmlShutdown");
            _getHandle = Bind<GetHandleDelegate>("nvmlDeviceGetHandleByIndex_v2");
            _getName = Bind<GetNameDelegate>("nvmlDeviceGetName");
            _getTemperature = Bind<GetTemperatureDelegate>("nvmlDeviceGetTemperature");
            _getUtilization = Bind<GetUtilizationDelegate>("nvmlDeviceGetUtilizationRates");
            _getPower = Bind<GetPowerDelegate>("nvmlDeviceGetPowerUsage");
            _getClock = Bind<GetClockDelegate>("nvmlDeviceGetClockInfo");
            _getMemory = Bind<GetMemoryDelegate>("nvmlDeviceGetMemoryInfo");
            _getMemoryV2 = TryBind<GetMemoryV2Delegate>("nvmlDeviceGetMemoryInfo_v2");
        }
        catch (Exception exception) when (
            exception is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            error = exception.Message;
            _unavailable = true;
            ReleaseLibrary();
            return false;
        }

        var status = _init!();
        if (status != NvmlSuccess)
        {
            error = $"nvmlInit_v2 returned {status}.";
            _unavailable = true;
            ReleaseLibrary();
            return false;
        }

        _initialized = true;
        status = _getHandle!(0, out var device);
        if (status != NvmlSuccess)
        {
            // A sleeping Optimus GPU fails here and comes back later, so this is a
            // failed read, not a machine without NVML.
            error = $"nvmlDeviceGetHandleByIndex_v2 returned {status}.";
            _shutdown!();
            _initialized = false;
            ReleaseLibrary();
            return false;
        }

        _device = device;
        error = null;
        return true;
    }

    private T Bind<T>(string export) where T : Delegate
    {
        var pointer = NativeLibrary.GetExport(_library, export);
        return Marshal.GetDelegateForFunctionPointer<T>(pointer);
    }

    private T? TryBind<T>(string export) where T : Delegate =>
        NativeLibrary.TryGetExport(_library, export, out var pointer)
            ? Marshal.GetDelegateForFunctionPointer<T>(pointer)
            : null;

    private string? ReadName()
    {
        var buffer = Marshal.AllocHGlobal(NameBufferLength);
        try
        {
            if (_getName!(_device, buffer, NameBufferLength) != NvmlSuccess)
            {
                return null;
            }

            var name = Marshal.PtrToStringAnsi(buffer);
            return string.IsNullOrWhiteSpace(name) ? null : name;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private void ReleaseLibrary()
    {
        if (_library == IntPtr.Zero)
        {
            return;
        }

        NativeLibrary.Free(_library);
        _library = IntPtr.Zero;
        _device = IntPtr.Zero;
        _initialized = false;
    }
}
