using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace OpenSynapse.Windows.Lifecycle;

/// <summary>
/// Applies the GPU core and memory clock offsets through NVAPI. The request block matches
/// the one Razer's bladeNative.dll submits: 0x1CF8 bytes, version 0x11C94, one pstate with
/// one clock, domain 0 for the core and 4 for the memory, delta in kHz. Razer sends the two
/// domains as separate calls, followed by readback; failed writes restore the old snapshot.
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

    // NvAPIWrapper's local PerformanceStates20InfoV1/ClockEntryV1/BaseVoltageEntryV1:
    // header=5 uints; state=2 uints + 8 clocks + 4 voltages; clock=3 uints + 3 ints + 5 uints.
    internal const int MaximumPstates = 16;
    internal const int MaximumClocks = 8;
    internal const int MaximumBaseVoltages = 4;
    internal const int ClockEntrySize = 44;
    internal const int PstateSize = 8 + MaximumClocks * ClockEntrySize + MaximumBaseVoltages * 24;
    internal const int PstatesInfoV1Size = PstateIdOffset + MaximumPstates * PstateSize;

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
    private static readonly object DriverGate = new();

    /// <summary>
    /// True only for a readable, editable P0 snapshot with both supported clock domains.
    /// </summary>
    public static bool TryProbe(out string? error) => TryRead(out _, out _, out error);

    /// <summary>
    /// Reads both offsets from one returned Pstates20 array. Unrepresentable or unsafe
    /// snapshots are unavailable, never a successful synthetic zero.
    /// </summary>
    public static bool TryRead(out int coreMegahertz, out int memoryMegahertz, out string? error)
    {
        lock (DriverGate)
        {
            coreMegahertz = memoryMegahertz = 0;
            if (Api.Value is not { } api)
            {
                error = "nvapi64.dll did not expose the NVAPI entry points.";
                return false;
            }
            if (!TryGetPrimaryGpu(api, out var gpu, out error)) return false;
            return TryRead(buffer => api.GetPstates20(gpu, buffer),
                out coreMegahertz, out memoryMegahertz, out error);
        }
    }

    internal static bool TryRead(Func<byte[], int> getPstates,
        out int coreMegahertz, out int memoryMegahertz, out string? error)
    {
        coreMegahertz = memoryMegahertz = 0;
        if (!TryReadSnapshot(getPstates, out var snapshot, out error)) return false;
        coreMegahertz = snapshot.Core.DeltaKhz / 1000;
        memoryMegahertz = snapshot.Memory.DeltaKhz / 1000;
        return true;
    }

    /// <summary>
    /// Keeps the laptop clamps, also requires the driver's writable delta ranges, and
    /// succeeds only after readback matches both requested offsets.
    /// </summary>
    public static bool TryApply(int coreMegahertz, int memoryMegahertz, out string? error)
    {
        lock (DriverGate)
        {
            if (Api.Value is not { } api)
            {
                error = "nvapi64.dll did not expose the NVAPI entry points.";
                return false;
            }
            if (!TryGetPrimaryGpu(api, out var gpu, out error)) return false;
            return TryApply(coreMegahertz, memoryMegahertz,
                buffer => api.GetPstates20(gpu, buffer),
                buffer => api.SetPstates20(gpu, buffer), Thread.Sleep, out error);
        }
    }

    internal static bool TryApply(int coreMegahertz, int memoryMegahertz,
        Func<byte[], int> getPstates, Func<byte[], int> setPstates,
        Action<int> delay, out string? error)
    {
        if (!TryReadSnapshot(getPstates, out var before, out error)) return false;
        var core = ClampCore(coreMegahertz) * 1000;
        var memory = ClampMemory(memoryMegahertz) * 1000;
        if (!before.Core.Allows(core) || !before.Memory.Allows(memory))
        {
            error = "Requested offsets exceed the driver's writable delta ranges.";
            return false;
        }

        if (Matches(before, core, memory))
        {
            error = null;
            return true;
        }

        string? failure;
        if (before.Core.DeltaKhz != core &&
            !TrySet(setPstates, delay, ClockDomainGraphics, core, out failure))
            return Restore(before, getPstates, setPstates, delay, failure!, out error);
        if (before.Memory.DeltaKhz != memory &&
            !TrySet(setPstates, delay, ClockDomainMemory, memory, out failure))
            return Restore(before, getPstates, setPstates, delay, failure!, out error);
        if (!TryReadSnapshot(getPstates, out var actual, out failure))
            return Restore(before, getPstates, setPstates, delay,
                $"Offset readback failed: {failure}", out error);
        if (!Matches(actual, core, memory))
            return Restore(before, getPstates, setPstates, delay,
                $"Offset readback mismatch: requested {core}/{memory} kHz; " +
                $"reported {actual.Core.DeltaKhz}/{actual.Memory.DeltaKhz} kHz.", out error);
        error = null;
        return true;
    }

    private static bool Matches(ClockSnapshot snapshot, int coreKhz, int memoryKhz) =>
        snapshot.Core.DeltaKhz == coreKhz && snapshot.Memory.DeltaKhz == memoryKhz;

    private static bool Restore(ClockSnapshot before, Func<byte[], int> getPstates,
        Func<byte[], int> setPstates, Action<int> delay, string failure, out string? error)
    {
        // No caller cancellation is used: both domains must be attempted even if one fails.
        _ = TrySet(setPstates, delay, ClockDomainGraphics, before.Core.DeltaKhz, out var coreError);
        _ = TrySet(setPstates, delay, ClockDomainMemory, before.Memory.DeltaKhz, out var memoryError);
        var read = TryReadSnapshot(getPstates, out var restored, out var readError);
        error = read && Matches(restored, before.Core.DeltaKhz, before.Memory.DeltaKhz)
            ? $"{failure} Original offsets restored and verified."
            : $"{failure} Rollback failed: core={coreError ?? "accepted"}; " +
              $"memory={memoryError ?? "accepted"}; " +
              (read ? $"reported {restored.Core.DeltaKhz}/{restored.Memory.DeltaKhz} kHz."
                    : $"readback={readError}");
        return false;
    }

    private static bool TryReadSnapshot(Func<byte[], int> getPstates,
        out ClockSnapshot snapshot, out string? error)
    {
        // GetPstates20 outputs all states; unlike the setter, only version is an input.
        var parameters = new byte[ParametersSize];
        WriteUInt32(parameters, VersionOffset, ParametersVersion);
        var status = getPstates(parameters);
        if (status != 0)
        {
            snapshot = default;
            error = $"NvAPI_GPU_GetPstates20 returned 0x{status:X8}.";
            return false;
        }
        return TryParseSnapshot(parameters, out snapshot, out error);
    }

    internal readonly record struct ClockDelta(int DeltaKhz, int MinimumKhz, int MaximumKhz)
    {
        internal bool Allows(int value) => value >= MinimumKhz && value <= MaximumKhz;
    }

    internal readonly record struct ClockSnapshot(ClockDelta Core, ClockDelta Memory);

    internal static bool TryParseSnapshot(ReadOnlySpan<byte> buffer,
        out ClockSnapshot snapshot, out string? error)
    {
        snapshot = default;
        error = "Malformed or non-editable NVAPI Pstates20 V1 snapshot.";
        if (buffer.Length < PstatesInfoV1Size || ReadUInt32(buffer, 0) != ParametersVersion ||
            (ReadUInt32(buffer, 4) & 1) == 0) return false;
        var states = ReadUInt32(buffer, NumPstatesOffset);
        var clocks = ReadUInt32(buffer, NumClocksOffset);
        var voltages = ReadUInt32(buffer, 0x10);
        if (states is < 1 or > MaximumPstates || clocks is < 1 or > MaximumClocks ||
            voltages > MaximumBaseVoltages) return false;

        var p0 = -1;
        for (var index = 0; index < states; index++)
        {
            var offset = PstateIdOffset + index * PstateSize;
            if (ReadUInt32(buffer, offset) != 0) continue;
            if (p0 >= 0) return false;
            p0 = offset;
        }
        if (p0 < 0 || (ReadUInt32(buffer, p0 + 4) & 1) == 0) return false;

        ClockDelta? core = null, memory = null;
        for (var index = 0; index < clocks; index++)
        {
            var offset = p0 + 8 + index * ClockEntrySize;
            var domain = ReadUInt32(buffer, offset);
            if (domain != ClockDomainGraphics && domain != ClockDomainMemory) continue;
            if (ReadUInt32(buffer, offset + 4) > 1 ||
                (ReadUInt32(buffer, offset + 8) & 1) == 0) return false;
            var delta = new ClockDelta(ReadInt32(buffer, offset + 12),
                ReadInt32(buffer, offset + 16), ReadInt32(buffer, offset + 20));
            var minimum = domain == ClockDomainGraphics ? LaptopCoreMinimumMhz : MemoryMinimumMhz;
            var maximum = domain == ClockDomainGraphics ? LaptopCoreMaximumMhz : MemoryMaximumMhz;
            // The public API is whole MHz; truncating a fractional baseline would corrupt restore.
            if (delta.MaximumKhz <= delta.MinimumKhz || !delta.Allows(delta.DeltaKhz) || delta.DeltaKhz % 1000 != 0 ||
                delta.DeltaKhz < minimum * 1000 || delta.DeltaKhz > maximum * 1000) return false;
            if (domain == ClockDomainGraphics)
            {
                if (core is not null) return false;
                core = delta;
            }
            else
            {
                if (memory is not null) return false;
                memory = delta;
            }
        }
        if (core is null || memory is null) return false;
        snapshot = new ClockSnapshot(core.Value, memory.Value);
        error = null;
        return true;
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> buffer, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(offset, sizeof(uint)));

    private static int ReadInt32(ReadOnlySpan<byte> buffer, int offset) =>
        BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(offset, sizeof(int)));

    public static int ClampCore(int megahertz) =>
        Math.Clamp(megahertz, LaptopCoreMinimumMhz, LaptopCoreMaximumMhz);

    public static int ClampMemory(int megahertz) =>
        Math.Clamp(megahertz, MemoryMinimumMhz, MemoryMaximumMhz);

    /// <summary>
    /// The exact block Razer builds: version, one pstate, one clock, the clock domain and
    /// the delta in kHz. The remaining bytes stay zero.
    /// </summary>
    internal static byte[] CreateParameters(int clockDomain, int deltaMegahertz) =>
        CreateParametersKhz(clockDomain, deltaMegahertz * 1000);

    private static byte[] CreateParametersKhz(int clockDomain, int deltaKhz)
    {
        var parameters = new byte[ParametersSize];
        WriteUInt32(parameters, VersionOffset, ParametersVersion);
        WriteUInt32(parameters, NumPstatesOffset, 1);
        WriteUInt32(parameters, NumClocksOffset, 1);
        WriteUInt32(parameters, PstateIdOffset, 0);
        WriteUInt32(parameters, ClockDomainOffset, (uint)clockDomain);
        WriteUInt32(parameters, ClockDeltaOffset, unchecked((uint)deltaKhz));
        return parameters;
    }

    private static bool TrySet(
        Func<byte[], int> setPstates,
        Action<int> delay,
        int clockDomain,
        int deltaKhz,
        out string? error)
    {
        var status = 0;
        // The GPU drops out of its performance state right after a mode switch, so the
        // first writes can be refused while it settles. Razer retries 30 times at 90 ms.
        for (var attempt = 0; attempt < RetryCount; attempt++)
        {
            status = setPstates(CreateParametersKhz(clockDomain, deltaKhz));
            if (status == 0)
            {
                error = null;
                return true;
            }

            if (attempt + 1 < RetryCount) delay(RetryDelayMilliseconds);
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
    private delegate int GetPstates20Delegate(nint gpu, [In, Out] byte[] parameters);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int SetPstates20Delegate(nint gpu, byte[] parameters);
}
