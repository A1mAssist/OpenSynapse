using System.Buffers.Binary;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace OpenSynapse.Windows.Lifecycle;

/// <summary>
/// Reads and applies the driver-level graphics clock ceiling. NVAPI exposes the current
/// boost lock, while the write goes through nvidia-smi, matching G-Helper's behaviour.
/// </summary>
public static class NvApiGpuClockLimit
{
    public const int MinimumMhz = 400;
    public const int MaximumMhz = 3000;

    internal const int ParametersSize = 780;
    internal const uint ParametersVersion = 780u | (2u << 16);
    internal const int VersionOffset = 0x00;
    internal const int CountOffset = 0x08;
    internal const int FirstLockVoltageOffset = 0x1C;

    private const uint InitializeId = 0x0150E828;
    private const uint EnumPhysicalGpusId = 0xE5AC921F;
    private const uint GetClockBoostLockId = 0xE440B867;
    private const int MaximumPhysicalGpus = 64;
    private const int TimeoutMilliseconds = 10_000;

    private static readonly Lazy<NvApi?> Api = new(Load, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>True when the driver accepts a boost-lock read.</summary>
    public static bool TryProbe(out string? error)
    {
        if (Api.Value is not { } api)
        {
            error = "nvapi64.dll did not expose the NVAPI entry points.";
            return false;
        }

        if (!TryGetPrimaryGpu(api, out var gpu, out error))
        {
            return false;
        }

        var status = api.GetClockBoostLock(gpu, CreateParameters());
        if (status != 0)
        {
            error = $"NvAPI_GPU_GetClockBoostLock returned 0x{status:X8}.";
            return false;
        }

        error = null;
        return true;
    }

    /// <summary>
    /// Returns the current ceiling, or zero when no lock is set. Zero is the UI's
    /// default sentinel; the driver's maximum is also treated as no lock.
    /// </summary>
    public static bool TryRead(out int megahertz, out string? error)
    {
        megahertz = 0;
        if (Api.Value is not { } api)
        {
            error = "nvapi64.dll did not expose the NVAPI entry points.";
            return false;
        }

        if (!TryGetPrimaryGpu(api, out var gpu, out error))
        {
            return false;
        }

        var parameters = CreateParameters();
        var status = api.GetClockBoostLock(gpu, parameters);
        if (status != 0)
        {
            error = $"NvAPI_GPU_GetClockBoostLock returned 0x{status:X8}.";
            return false;
        }

        var count = BinaryPrimitives.ReadUInt32LittleEndian(
            parameters.AsSpan(CountOffset, sizeof(uint)));
        if (count == 0)
        {
            error = null;
            return true;
        }

        var microvolts = BinaryPrimitives.ReadUInt32LittleEndian(
            parameters.AsSpan(FirstLockVoltageOffset, sizeof(uint)));
        var value = (int)(microvolts / 1000);
        megahertz = value is >= MinimumMhz and < MaximumMhz ? value : 0;
        error = null;
        return true;
    }

    /// <summary>Applies the ceiling through nvidia-smi; the maximum means reset.</summary>
    public static bool TryApply(int megahertz, out string? error)
    {
        var startInfo = new ProcessStartInfo(ResolveNvidiaSmi())
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in BuildArguments(megahertz))
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                error = "nvidia-smi could not be started.";
                return false;
            }

            var output = process.StandardOutput.ReadToEndAsync();
            var errorOutput = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(TimeoutMilliseconds))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                }

                error = "nvidia-smi did not finish within 10 seconds.";
                return false;
            }

            var detail = errorOutput.GetAwaiter().GetResult().Trim();
            if (detail.Length == 0)
            {
                detail = output.GetAwaiter().GetResult().Trim();
            }

            if (process.ExitCode != 0)
            {
                error = detail.Length == 0
                    ? $"nvidia-smi exited with code {process.ExitCode}."
                    : $"nvidia-smi exited with code {process.ExitCode}: {detail}";
                return false;
            }

            error = null;
            return true;
        }
        catch (Exception exception) when (
            exception is Win32Exception or InvalidOperationException or IOException or
                UnauthorizedAccessException or ObjectDisposedException or AggregateException)
        {
            error = exception.Message;
            return false;
        }
    }

    public static int Clamp(int megahertz) => Math.Clamp(megahertz, MinimumMhz, MaximumMhz);

    internal static string[] BuildArguments(int megahertz) =>
        megahertz >= MaximumMhz
            ? ["-rgc"]
            : ["-lgc", FormattableString.Invariant($"0,{Clamp(megahertz)}")];

    internal static byte[] CreateParameters()
    {
        // Count is an output field for the getter; the driver fills it.
        var parameters = new byte[ParametersSize];
        BinaryPrimitives.WriteUInt32LittleEndian(
            parameters.AsSpan(VersionOffset, sizeof(uint)),
            ParametersVersion);
        return parameters;
    }

    private static string ResolveNvidiaSmi()
    {
        var systemPath = Path.Combine(Environment.SystemDirectory, "nvidia-smi.exe");
        return File.Exists(systemPath) ? systemPath : "nvidia-smi";
    }

    private static bool TryGetPrimaryGpu(NvApi api, out nint gpu, out string? error)
    {
        var status = api.Initialize();
        if (status != 0)
        {
            gpu = 0;
            error = $"NvAPI_Initialize returned 0x{status:X8}.";
            return false;
        }

        var handles = new nint[MaximumPhysicalGpus];
        status = api.EnumPhysicalGpus(handles, out var count);
        if (status != 0 || count == 0)
        {
            gpu = 0;
            error = $"NvAPI_EnumPhysicalGPUs returned 0x{status:X8} with {count} GPU(s).";
            return false;
        }

        gpu = handles[0];
        error = null;
        return true;
    }

    private static NvApi? Load()
    {
        try
        {
            var library = NativeLibrary.Load("nvapi64.dll");
            var queryPointer = NativeLibrary.GetExport(library, "nvapi_QueryInterface");
            var query = Marshal.GetDelegateForFunctionPointer<QueryInterfaceDelegate>(queryPointer);

            var initialize = GetDelegate<InitializeDelegate>(query, InitializeId);
            var enumGpus = GetDelegate<EnumPhysicalGpusDelegate>(query, EnumPhysicalGpusId);
            var getClockBoostLock = GetDelegate<GetClockBoostLockDelegate>(query, GetClockBoostLockId);
            if (initialize is null || enumGpus is null || getClockBoostLock is null)
            {
                return null;
            }

            return new NvApi(initialize, enumGpus, getClockBoostLock);
        }
        catch (Exception exception) when (
            exception is DllNotFoundException or EntryPointNotFoundException or
                BadImageFormatException or MarshalDirectiveException or ArgumentException)
        {
            return null;
        }
    }

    private static T? GetDelegate<T>(QueryInterfaceDelegate query, uint id)
        where T : Delegate
    {
        var pointer = query(id);
        return pointer == 0 ? null : Marshal.GetDelegateForFunctionPointer<T>(pointer);
    }

    private sealed record NvApi(
        InitializeDelegate Initialize,
        EnumPhysicalGpusDelegate EnumPhysicalGpus,
        GetClockBoostLockDelegate GetClockBoostLock);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint QueryInterfaceDelegate(uint functionId);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int InitializeDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int EnumPhysicalGpusDelegate([Out] nint[] handles, out uint count);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetClockBoostLockDelegate(nint gpu, byte[] parameters);
}
