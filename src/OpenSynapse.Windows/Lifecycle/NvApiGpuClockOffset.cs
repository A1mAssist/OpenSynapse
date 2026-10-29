using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace OpenSynapse.Windows.Lifecycle;

/// <summary>
/// Applies the GPU core and memory clock offsets through NVAPI. The request block matches
/// the one Razer's bladeNative.dll submits: 0x1CF8 bytes, version 0x11C94, one pstate with
/// one clock, domain 0 for the core and 4 for the memory, delta in kHz. Razer sends the two
/// domains as separate calls, so this does too.
/// </summary>
public static class NvApiGpuClockOffset
{
    public const int LaptopCoreMinimumMhz = -500;
    public const int LaptopCoreMaximumMhz = 200;
    public const int MemoryMinimumMhz = -500;
    public const int MemoryMaximumMhz = 2000;

    internal const int ParametersSize = 0x1CF8;
    internal const uint ParametersVersion = 0x11C94;
    internal const int VersionOffset = 0x00;
    internal const int NumPstatesOffset = 0x08;
    internal const int NumClocksOffset = 0x0C;
    internal const int PstateIdOffset = 0x14;
    internal const int ClockDomainOffset = 0x1C;
    internal const int ClockDeltaOffset = 0x28;

    private const uint InitializeId = 0x0150E828;
    private const uint EnumPhysicalGpusId = 0xE5AC921F;
    private const uint GetPstates20Id = 0x6FF81213;
    private const uint SetPstates20Id = 0x0F4DAE6B;
    private const int MaximumPhysicalGpus = 64;
    private const int ClockDomainGraphics = 0;
    private const int ClockDomainMemory = 4;
    private const int RetryCount = 30;
    private const int RetryDelayMilliseconds = 90;

    private static readonly Lazy<NvApi?> Api = new(Load, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// True when the driver accepts a Pstates20 read. The UI hides the offset controls when
    /// this fails, because a laptop driver that refuses the read will refuse the write.
    /// </summary>
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

        // The getter rejects a zeroed block: it reads the version field first.
        var status = api.GetPstates20(gpu, CreateParameters(ClockDomainGraphics, 0));
        if (status != 0)
        {
            error = $"NvAPI_GPU_GetPstates20 returned 0x{status:X8}.";
            return false;
        }

        error = null;
        return true;
    }

    /// <summary>
    /// Reads the offsets the driver is currently holding, so the UI can show the real
    /// state after a restart. A value outside the laptop envelope is reported as zero:
    /// the block layout is taken from the write path, and a mismatch must not paint a
    /// bogus number onto a slider.
    /// </summary>
    public static bool TryRead(out int coreMegahertz, out int memoryMegahertz, out string? error)
    {
        coreMegahertz = 0;
        memoryMegahertz = 0;
        if (Api.Value is not { } api)
        {
            error = "nvapi64.dll did not expose the NVAPI entry points.";
            return false;
        }

        if (!TryGetPrimaryGpu(api, out var gpu, out error))
        {
            return false;
        }

        var core = ReadDomain(api, gpu, ClockDomainGraphics);
        var memory = ReadDomain(api, gpu, ClockDomainMemory);
        if (core is null || memory is null)
        {
            error = "NvAPI_GPU_GetPstates20 did not return the clock block.";
            return false;
        }

        coreMegahertz = Sanitize(core.Value, LaptopCoreMinimumMhz, LaptopCoreMaximumMhz);
        memoryMegahertz = Sanitize(memory.Value, MemoryMinimumMhz, MemoryMaximumMhz);
        error = null;
        return true;
    }

    internal static int Sanitize(int megahertz, int minimum, int maximum) =>
        megahertz >= minimum && megahertz <= maximum ? megahertz : 0;

    private static int? ReadDomain(NvApi api, nint gpu, int clockDomain)
    {
        var parameters = CreateParameters(clockDomain, 0);
        if (api.GetPstates20(gpu, parameters) != 0)
        {
            return null;
        }

        return BinaryPrimitives.ReadInt32LittleEndian(parameters.AsSpan(ClockDeltaOffset)) / 1000;
    }

    /// <summary>
    /// Writes both offsets. Values outside the laptop envelope are clamped, never rejected,
    /// so a slider cannot submit a number the driver would refuse.
    /// </summary>
    public static bool TryApply(int coreMegahertz, int memoryMegahertz, out string? error)
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

        if (!TrySet(api, gpu, ClockDomainGraphics, ClampCore(coreMegahertz), out error))
        {
            return false;
        }

        return TrySet(api, gpu, ClockDomainMemory, ClampMemory(memoryMegahertz), out error);
    }

    public static int ClampCore(int megahertz) =>
        Math.Clamp(megahertz, LaptopCoreMinimumMhz, LaptopCoreMaximumMhz);

    public static int ClampMemory(int megahertz) =>
        Math.Clamp(megahertz, MemoryMinimumMhz, MemoryMaximumMhz);

    /// <summary>
    /// The exact block Razer builds: version, one pstate, one clock, the clock domain and
    /// the delta in kHz. The remaining bytes stay zero.
    /// </summary>
    internal static byte[] CreateParameters(int clockDomain, int deltaMegahertz)
    {
        var parameters = new byte[ParametersSize];
        WriteUInt32(parameters, VersionOffset, ParametersVersion);
        WriteUInt32(parameters, NumPstatesOffset, 1);
        WriteUInt32(parameters, NumClocksOffset, 1);
        WriteUInt32(parameters, PstateIdOffset, 0);
        WriteUInt32(parameters, ClockDomainOffset, (uint)clockDomain);
        WriteUInt32(parameters, ClockDeltaOffset, unchecked((uint)(deltaMegahertz * 1000)));
        return parameters;
    }

    private static bool TrySet(
        NvApi api,
        nint gpu,
        int clockDomain,
        int deltaMegahertz,
        out string? error)
    {
        var parameters = CreateParameters(clockDomain, deltaMegahertz);
        var status = 0;
        // The GPU drops out of its performance state right after a mode switch, so the
        // first writes can be refused while it settles. Razer retries 30 times at 90 ms.
        for (var attempt = 0; attempt < RetryCount; attempt++)
        {
            status = api.SetPstates20(gpu, parameters);
            if (status == 0)
            {
                error = null;
                return true;
            }

            Thread.Sleep(RetryDelayMilliseconds);
        }

        error = $"NvAPI_GPU_SetPstates20(domain {clockDomain}) returned 0x{status:X8}.";
        return false;
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

    private static void WriteUInt32(byte[] buffer, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(offset, sizeof(uint)), value);

    private static NvApi? Load()
    {
        try
        {
            var library = NativeLibrary.Load("nvapi64.dll");
            var queryPointer = NativeLibrary.GetExport(library, "nvapi_QueryInterface");
            var query = Marshal.GetDelegateForFunctionPointer<QueryInterfaceDelegate>(queryPointer);

            var initialize = GetDelegate<InitializeDelegate>(query, InitializeId);
            var enumGpus = GetDelegate<EnumPhysicalGpusDelegate>(query, EnumPhysicalGpusId);
            var getPstates = GetDelegate<GetPstates20Delegate>(query, GetPstates20Id);
            var setPstates = GetDelegate<SetPstates20Delegate>(query, SetPstates20Id);
            if (initialize is null || enumGpus is null || getPstates is null || setPstates is null)
            {
                return null;
            }

            return new NvApi(initialize, enumGpus, getPstates, setPstates);
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
        GetPstates20Delegate GetPstates20,
        SetPstates20Delegate SetPstates20);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint QueryInterfaceDelegate(uint functionId);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int InitializeDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int EnumPhysicalGpusDelegate([Out] nint[] handles, out uint count);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetPstates20Delegate(nint gpu, byte[] parameters);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int SetPstates20Delegate(nint gpu, byte[] parameters);
}
