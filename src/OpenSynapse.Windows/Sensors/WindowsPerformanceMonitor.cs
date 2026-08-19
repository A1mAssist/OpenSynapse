using System.Diagnostics;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using OpenSynapse.Core.Sensors;

namespace OpenSynapse.Windows.Sensors;

public sealed class WindowsPerformanceMonitor : IPerformanceMonitor, IDisposable
{
    private readonly string _cpuName;
    private readonly CpuHardwareMonitor _cpuHardware = new();
    private readonly WindowsGpuActivityReader _gpuActivity = new();
    private readonly AmdAdlTelemetryReader _amdGpu = new();
    private ulong _previousIdle;
    private ulong _previousKernel;
    private ulong _previousUser;

    public WindowsPerformanceMonitor()
    {
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
        ReadMemory(out var memoryUsed, out var memoryTotal);
        ReadStorage(out var storageUsed, out var storageTotal);
        var windowsGpus = _gpuActivity.Read();
        var nvidiaActive = WindowsGpuActivityReader.IsNvidiaActive(windowsGpus);
        var nvidia = nvidiaActive ? await ReadNvidiaGpuAsync(cancellationToken) : null;
        var integrated = WindowsGpuActivityReader.SelectIntegrated(windowsGpus);
        var windowsNvidia = WindowsGpuActivityReader.SelectNvidia(windowsGpus);
        var selected = nvidiaActive ? windowsNvidia : integrated;
        var amd = selected?.VendorId == 0x1002 ? _amdGpu.Read() : default;
        var gpuName = nvidia?.Name ?? selected?.Name ?? "GPU";
        var gpuUsage = nvidia?.UsagePercent ?? selected?.UsagePercent;

        return new PerformanceSnapshot(
            _cpuName.Trim(),
            cpuUsage,
            cpu.TemperatureCelsius,
            cpu.PowerWatts,
            cpu.ClockMegahertz,
            gpuName,
            gpuUsage,
            nvidia?.TemperatureCelsius ?? amd.TemperatureCelsius ?? selected?.TemperatureCelsius,
            nvidia?.PowerWatts ?? amd.PowerWatts,
            nvidia?.ClockMegahertz ?? amd.ClockMegahertz,
            nvidia?.MemoryUsedMebibytes ?? selected?.MemoryUsedMebibytes,
            nvidia?.MemoryTotalMebibytes ?? selected?.MemoryTotalMebibytes,
            memoryUsed,
            memoryTotal,
            storageUsed,
            storageTotal,
            DateTimeOffset.UtcNow,
            windowsGpus.Count == 0
                ? "无法读取 Windows GPU 性能计数器；其余指标仍在刷新。"
                : nvidiaActive && nvidia is null
                    ? "NVIDIA 正在工作，但详细温度、功耗和频率暂时不可用。"
                    : null,
            selected?.IsIntegrated == true ? "共享内存" : "专用显存");
    }

    public void Dispose()
    {
        _gpuActivity.Dispose();
        _amdGpu.Dispose();
        _cpuHardware.Dispose();
    }

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

    private static async Task<NvidiaGpuSample?> ReadNvidiaGpuAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "nvidia-smi.exe",
                Arguments = "--query-gpu=name,temperature.gpu,utilization.gpu,power.draw,clocks.current.graphics,memory.used,memory.total --format=csv,noheader,nounits",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return null;
            }

            var output = await process.StandardOutput.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            return process.ExitCode == 0 ? NvidiaSmiOutputParser.Parse(output) : null;
        }
        catch (Exception exception) when (exception is Win32Exception or OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            return null;
        }
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
