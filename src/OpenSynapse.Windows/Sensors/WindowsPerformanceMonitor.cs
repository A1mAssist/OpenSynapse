using System.Diagnostics;
using System.Management;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using OpenSynapse.Core.Sensors;

namespace OpenSynapse.Windows.Sensors;

public sealed class WindowsPerformanceMonitor : IPerformanceMonitor, IDisposable
{
    private static readonly TimeSpan NvidiaSampleCacheInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan StorageSampleCacheInterval = TimeSpan.FromSeconds(30);
    private readonly string _cpuName;
    private readonly CpuHardwareMonitor _cpuHardware = new();
    private readonly Lazy<int?> _memoryClockMegahertz = new(ReadMemoryClockMegahertz);
    private readonly WindowsGpuActivityReader _gpuActivity = new();
    private readonly AmdAdlTelemetryReader _amdGpu = new();
    private readonly IntelIgclTelemetryReader _intelGpu = new();
    private readonly NvmlGpuReader _nvml = new();
    private NvidiaGpuSample? _nvidiaSample;
    private DateTimeOffset _nvidiaSampleAt = DateTimeOffset.MinValue;
    private readonly object _storageGate = new();
    private DateTimeOffset _storageSampleAt = DateTimeOffset.MinValue;
    private long? _storageUsed;
    private long? _storageTotal;
    private ulong _previousIdle;
    private ulong _previousKernel;
    private ulong _previousUser;

    /// <summary>
    /// Reads the CPU package temperature from the machine-wide service, which is the only
    /// process that can open PawnIO. Null keeps the locally sampled value.
    /// </summary>
    private readonly Func<CancellationToken, Task<double?>>? _remoteCpuTemperature;

    public WindowsPerformanceMonitor(
        Func<CancellationToken, Task<double?>>? remoteCpuTemperature = null)
    {
        _remoteCpuTemperature = remoteCpuTemperature;
        _cpuName = Registry.GetValue(
            @"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\CentralProcessor\0",
            "ProcessorNameString",
            "CPU") as string ?? "CPU";
        TryReadSystemTimes(out _previousIdle, out _previousKernel, out _previousUser);
    }

    public async ValueTask<PerformanceSnapshot> SampleAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var cpuUsage = ReadCpuUsage();
        var cpu = _cpuHardware.Read();
        var cpuTemperature = cpu.TemperatureCelsius;
        if (_cpuHardware.LastSource is CpuTemperatureSource.Acpi or CpuTemperatureSource.None)
        {
            cpuTemperature = await TryRemoteCpuTemperatureAsync(cancellationToken)
                .ConfigureAwait(false) ?? cpuTemperature;
        }
        ReadMemory(out var memoryUsed, out var memoryTotal);
        var memoryClock = _memoryClockMegahertz.Value;
        ReadStorageThrottled(out var storageUsed, out var storageTotal);
        var windowsGpus = _gpuActivity.Read();
        var nvidiaActive = WindowsGpuActivityReader.IsNvidiaActive(windowsGpus);
        var nvidia = nvidiaActive ? ReadNvidiaGpuThrottled() : null;
        var selected = WindowsGpuActivityReader.SelectDisplayGpu(windowsGpus);
        var amd = selected?.VendorId == 0x1002 ? _amdGpu.Read() : default;
        var intel = selected?.VendorId == 0x8086 ? _intelGpu.Read() : default;
        var gpuName = nvidia?.Name ?? selected?.Name ?? "GPU";
        var gpuUsage = nvidia?.UsagePercent ?? selected?.UsagePercent;
        var gpuTemperature = nvidia?.TemperatureCelsius ?? amd.TemperatureCelsius ?? intel.TemperatureCelsius;
        if (gpuTemperature is null)
        {
            gpuTemperature = _cpuHardware.ReadGpuTemperatureCelsius(VendorHint(selected?.VendorId));
        }

        return new PerformanceSnapshot(
            _cpuName.Trim(),
            cpuUsage,
            cpuTemperature,
            cpu.PowerWatts,
            cpu.ClockMegahertz,
            gpuName,
            gpuUsage,
            gpuTemperature ?? selected?.TemperatureCelsius,
            nvidia?.PowerWatts ?? amd.PowerWatts ?? intel.PowerWatts,
            nvidia?.ClockMegahertz ?? amd.ClockMegahertz ?? intel.ClockMegahertz,
            nvidia?.MemoryUsedMebibytes ?? selected?.MemoryUsedMebibytes,
            nvidia?.MemoryTotalMebibytes ?? selected?.MemoryTotalMebibytes,
            memoryUsed,
            memoryTotal,
            storageUsed,
            storageTotal,
            DateTimeOffset.UtcNow,
            windowsGpus.Count == 0
                ? "Could not read Windows GPU performance counters; other metrics are still updating."
                : nvidiaActive && nvidia is null
                    ? "NVIDIA is active, but detailed temperature, power, and frequency data are temporarily unavailable."
                    : null,
            selected?.IsIntegrated == true ? "Shared memory" : "Dedicated memory",
            memoryClock);
    }

    public async ValueTask<(double? CpuTemperatureCelsius, double? GpuTemperatureCelsius)> SampleTemperaturesAsync(
        bool includeCpu,
        bool includeGpu,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var cpu = includeCpu ? _cpuHardware.ReadTemperatureCelsius() : null;
        if (includeCpu &&
            _cpuHardware.LastSource is CpuTemperatureSource.Acpi or CpuTemperatureSource.None)
        {
            cpu = await TryRemoteCpuTemperatureAsync(cancellationToken)
                .ConfigureAwait(false) ?? cpu;
        }
        var selected = includeGpu ? _gpuActivity.ReadSelectedTemperature() : null;
        var gpu = selected?.VendorId == 0x1002
            ? _amdGpu.Read().TemperatureCelsius ?? selected?.TemperatureCelsius
            : selected?.VendorId == 0x8086
                ? _intelGpu.Read().TemperatureCelsius ?? selected?.TemperatureCelsius
                : selected?.TemperatureCelsius;
        if (includeGpu && selected is { VendorId: 0x10DE } && gpu is null)
        {
            gpu = ReadNvidiaGpuThrottled()?.TemperatureCelsius;
        }
        if (includeGpu && gpu is null)
        {
            gpu = _cpuHardware.ReadGpuTemperatureCelsius(VendorHint(selected?.VendorId));
        }
        return (cpu, gpu);
    }

    public void Dispose()
    {
        _gpuActivity.Dispose();
        _amdGpu.Dispose();
        _intelGpu.Dispose();
        _cpuHardware.Dispose();
        _nvml.Dispose();
    }

    private static string? VendorHint(uint? vendorId) => vendorId switch
    {
        0x8086 => "Intel",
        0x10DE => "NVIDIA",
        0x1002 => "AMD",
        _ => null,
    };

    private double? ReadCpuUsage()
    {
        if (!TryReadSystemTimes(out var idle, out var kernel, out var user))
        {
            return null;
        }

        var idleDelta = idle - _previousIdle;
        var totalDelta = kernel - _previousKernel + user - _previousUser;
        _previousIdle = idle;
        _previousKernel = kernel;
        _previousUser = user;

        return totalDelta == 0 ? null : Math.Clamp((totalDelta - idleDelta) * 100d / totalDelta, 0, 100);
    }

    private static bool TryReadSystemTimes(out ulong idle, out ulong kernel, out ulong user)
    {
        if (!NativeMethods.GetSystemTimes(out var idleTime, out var kernelTime, out var userTime))
        {
            idle = kernel = user = 0;
            return false;
        }

        idle = idleTime.ToUInt64();
        kernel = kernelTime.ToUInt64();
        user = userTime.ToUInt64();
        return true;
    }

    private static void ReadMemory(out ulong? used, out ulong? total)
    {
        var status = new NativeMethods.MemoryStatusEx();
        if (!NativeMethods.GlobalMemoryStatusEx(ref status))
        {
            used = total = null;
            return;
        }

        total = status.TotalPhysical;
        used = status.TotalPhysical - status.AvailablePhysical;
    }

    private static int? ReadMemoryClockMegahertz()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT ConfiguredClockSpeed, Speed FROM Win32_PhysicalMemory");
            using var results = searcher.Get();
            var maximum = 0;
            foreach (ManagementObject module in results)
            {
                using (module)
                {
                    var configured = ToInt32(module["ConfiguredClockSpeed"]);
                    var speed = ToInt32(module["Speed"]);
                    var clock = configured > 0 ? configured : speed;
                    if (clock > maximum) maximum = clock;
                }
            }

            return maximum > 0 ? maximum : null;
        }
        catch (Exception exception) when (
            exception is ManagementException or COMException or UnauthorizedAccessException or
            InvalidOperationException or FormatException or OverflowException)
        {
            return null;
        }
    }

    private static int ToInt32(object? value) => value is null ? 0 : Convert.ToInt32(value);

    private static void ReadStorage(out long? used, out long? total)
    {
        try
        {
            var drives = DriveInfo.GetDrives().Where(drive => drive.IsReady && drive.DriveType == DriveType.Fixed).ToArray();
            total = drives.Sum(drive => drive.TotalSize);
            used = drives.Sum(drive => drive.TotalSize - drive.AvailableFreeSpace);
        }
        catch (IOException)
        {
            used = total = null;
        }
        catch (UnauthorizedAccessException)
        {
            used = total = null;
        }
    }

    private void ReadStorageThrottled(out long? used, out long? total)
    {
        lock (_storageGate)
        {
            var now = DateTimeOffset.UtcNow;
            if (now - _storageSampleAt < StorageSampleCacheInterval)
            {
                used = _storageUsed;
                total = _storageTotal;
                return;
            }

            ReadStorage(out _storageUsed, out _storageTotal);
            _storageSampleAt = now;
            used = _storageUsed;
            total = _storageTotal;
        }
    }

    private async ValueTask<double?> TryRemoteCpuTemperatureAsync(CancellationToken cancellationToken)
    {
        if (_remoteCpuTemperature is null)
        {
            return null;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(800));
        try
        {
            return await _remoteCpuTemperature(timeout.Token).ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is OperationCanceledException or IOException or TimeoutException)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            return null;
        }
    }

    private NvidiaGpuSample? ReadNvidiaGpuThrottled()
    {
        var now = DateTimeOffset.UtcNow;
        if (now - _nvidiaSampleAt < NvidiaSampleCacheInterval)
        {
            return _nvidiaSample;
        }

        _nvidiaSample = _nvml.TryRead(out var sample) ? sample : null;
        _nvidiaSampleAt = DateTimeOffset.UtcNow;
        return _nvidiaSample;
    }

    private static class NativeMethods
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct FileTime
        {
            internal uint Low;
            internal uint High;

            internal readonly ulong ToUInt64() => ((ulong)High << 32) | Low;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        internal struct MemoryStatusEx
        {
            internal uint Length;
            internal uint MemoryLoad;
            internal ulong TotalPhysical;
            internal ulong AvailablePhysical;
            internal ulong TotalPageFile;
            internal ulong AvailablePageFile;
            internal ulong TotalVirtual;
            internal ulong AvailableVirtual;
            internal ulong AvailableExtendedVirtual;

            public MemoryStatusEx()
            {
                Length = (uint)Marshal.SizeOf<MemoryStatusEx>();
            }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetSystemTimes(out FileTime idleTime, out FileTime kernelTime, out FileTime userTime);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);
    }
}
