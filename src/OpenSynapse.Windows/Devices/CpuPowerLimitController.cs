using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using OpenSynapse.Core.Devices;

namespace OpenSynapse.Windows.Devices;

/// <summary>
/// Reads and writes the Intel RAPL package power wall through PawnIO. The controller
/// keeps one executor for its lifetime and never hardcodes the register units.
/// </summary>
public sealed class CpuPowerLimitController : IDisposable
{
    private const uint UnitMsr = 0x606;
    private const uint PowerLimitMsr = 0x610;
    private const uint EnergyStatusMsr = 0x611;
    private const uint AccessDeniedHresult = 0x80070005;
    private const double MinimumSampleSeconds = 0.25;

    private static readonly string PawnIoLibraryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        "PawnIO",
        "PawnIOLib.dll");

    private static readonly string ModulePath =
        Path.Combine(AppContext.BaseDirectory, "IntelMSR.bin");

    private readonly IntPtr _library;
    private readonly IntPtr _handle;
    private readonly PawnIoExecute _execute;
    private readonly PawnIoClose _close;
    private readonly ulong _firmwareRegister;
    private readonly double _powerUnit;
    private readonly double _energyUnit;
    private readonly double _timeUnit;
    private readonly object _sampleGate = new();
    private uint? _lastEnergy;
    private long _lastSampleTimestamp;
    private bool _disposed;

    private CpuPowerLimitController(
        IntPtr library,
        IntPtr handle,
        PawnIoExecute execute,
        PawnIoClose close,
        ulong firmwareRegister,
        ulong unitRegister)
    {
        _library = library;
        _handle = handle;
        _execute = execute;
        _close = close;
        _firmwareRegister = firmwareRegister;
        _powerUnit = CpuPowerLimit.PowerUnitFromRegister(unitRegister);
        _energyUnit = CpuPowerLimit.EnergyUnitFromRegister(unitRegister);
        _timeUnit = CpuPowerLimit.TimeUnitFromRegister(unitRegister);
    }

    /// <summary>
    /// Opens PawnIO, loads IntelMSR.bin and snapshots the unit and wall registers.
    /// Only <see cref="CpuPowerLimitAvailability.AccessDenied"/> is worth retrying
    /// through the machine service; every other result is final.
    /// </summary>
    public static CpuPowerLimitAvailability TryOpen(
        out CpuPowerLimitController? controller,
        out string? detail)
    {
        controller = null;

        if (IsAmdCpu(out var processorName))
        {
            detail = $"CPU power limits require an Intel CPU ({processorName}).";
            return CpuPowerLimitAvailability.Failed;
        }

        if (!File.Exists(PawnIoLibraryPath))
        {
            detail = $"PawnIO is not installed ({PawnIoLibraryPath}).";
            return CpuPowerLimitAvailability.PawnIoMissing;
        }

        if (!File.Exists(ModulePath))
        {
            detail = $"The PawnIO MSR module is missing ({ModulePath}).";
            return CpuPowerLimitAvailability.ModuleMissing;
        }

        var library = IntPtr.Zero;
        PawnIoOpen open;
        PawnIoLoad load;
        PawnIoExecute execute;
        PawnIoClose close;
        try
        {
            library = NativeLibrary.Load(PawnIoLibraryPath);
            open = GetDelegate<PawnIoOpen>(library, "pawnio_open");
            load = GetDelegate<PawnIoLoad>(library, "pawnio_load");
            execute = GetDelegate<PawnIoExecute>(library, "pawnio_execute");
            close = GetDelegate<PawnIoClose>(library, "pawnio_close");
        }
        catch (Exception exception)
        {
            if (library != IntPtr.Zero)
            {
                NativeLibrary.Free(library);
            }

            detail = $"PawnIO could not be loaded: {exception.Message}";
            return CpuPowerLimitAvailability.Failed;
        }

        var openResult = open(out var handle);
        if (openResult < 0)
        {
            NativeLibrary.Free(library);
            detail =
                $"PawnIO could not open an executor (HRESULT {FormatResult(openResult)}).";
            return (uint)openResult == AccessDeniedHresult
                ? CpuPowerLimitAvailability.AccessDenied
                : CpuPowerLimitAvailability.Failed;
        }

        try
        {
            var loadResult = LoadModule(load, handle, File.ReadAllBytes(ModulePath));
            if (loadResult < 0)
            {
                throw new InvalidOperationException(
                    $"Loading IntelMSR.bin failed (HRESULT {FormatResult(loadResult)}).");
            }

            var unitRegister = ReadMsr(execute, handle, UnitMsr);
            var firmwareRegister = ReadMsr(execute, handle, PowerLimitMsr);
            controller = new CpuPowerLimitController(
                library,
                handle,
                execute,
                close,
                firmwareRegister,
                unitRegister);
            detail = null;
            return CpuPowerLimitAvailability.Available;
        }
        catch (Exception exception)
        {
            close(handle);
            NativeLibrary.Free(library);
            detail = exception.Message;
            return CpuPowerLimitAvailability.Failed;
        }
    }

    /// <summary>The current wall, the open-time snapshot and the lock bit.</summary>
    public CpuPowerLimitReport Read()
    {
        ThrowIfDisposed();
        return BuildReport(ReadMsr(_execute, _handle, PowerLimitMsr));
    }

    /// <summary>
    /// Writes only the two watt fields, based on the open-time register so the
    /// firmware enable, clamp and time-window bits survive.
    /// </summary>
    public CpuPowerLimitReport Apply(CpuPowerLimit limit)
    {
        ThrowIfDisposed();
        var clamped = limit.Clamp(CpuPowerLimit.MaximumWatts);
        var current = ReadMsr(_execute, _handle, PowerLimitMsr);
        ThrowIfLocked(current);
        ThrowIfLocked(_firmwareRegister);
        WriteMsr(
            _execute,
            _handle,
            PowerLimitMsr,
            clamped.EncodeWattsOnly(_firmwareRegister, _powerUnit));
        return BuildReport(ReadMsr(_execute, _handle, PowerLimitMsr));
    }

    /// <summary>
    /// Restores an explicit pair of walls. The upper bound only guards against dirty
    /// persisted data and deliberately sits above the user ceiling.
    /// </summary>
    public CpuPowerLimitReport RestoreTo(double pl1Watts, double pl2Watts)
    {
        ThrowIfDisposed();
        var request = new CpuPowerLimit(pl1Watts, pl2Watts, 0, 0)
            .Clamp(CpuPowerLimit.MaximumRestoreWatts);
        var current = ReadMsr(_execute, _handle, PowerLimitMsr);
        ThrowIfLocked(current);
        ThrowIfLocked(_firmwareRegister);
        WriteMsr(
            _execute,
            _handle,
            PowerLimitMsr,
            request.EncodeWattsOnly(_firmwareRegister, _powerUnit));
        return BuildReport(ReadMsr(_execute, _handle, PowerLimitMsr));
    }

    /// <summary>
    /// Fallback restore that uses the open-time snapshot. It is not the factory value
    /// once this process has written the register; callers with a persisted baseline
    /// must use <see cref="RestoreTo"/> instead.
    /// </summary>
    public CpuPowerLimitReport RestoreFirmware()
    {
        var firmware = CpuPowerLimit.Decode(_firmwareRegister, _powerUnit, _timeUnit);
        return RestoreTo(firmware.Pl1Watts, firmware.Pl2Watts);
    }

    /// <summary>
    /// Samples package power from the free-running 32-bit energy counter. The first
    /// call only establishes a baseline; short intervals are ignored as noise.
    /// </summary>
    public double? SamplePackageWatts()
    {
        ThrowIfDisposed();
        lock (_sampleGate)
        {
            var energy = (uint)ReadMsr(_execute, _handle, EnergyStatusMsr);
            var timestamp = Stopwatch.GetTimestamp();
            if (_lastEnergy is not { } previous)
            {
                _lastEnergy = energy;
                _lastSampleTimestamp = timestamp;
                return null;
            }

            var seconds = (timestamp - _lastSampleTimestamp) /
                (double)Stopwatch.Frequency;
            if (seconds < MinimumSampleSeconds)
            {
                return null;
            }

            // The counter is 32-bit and wraps; unsigned subtraction keeps the delta right.
            var delta = (uint)(energy - previous);
            _lastEnergy = energy;
            _lastSampleTimestamp = timestamp;
            return delta * _energyUnit / seconds;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            _close(_handle);
        }
        finally
        {
            NativeLibrary.Free(_library);
        }
    }

    private CpuPowerLimitReport BuildReport(ulong register)
    {
        var current = CpuPowerLimit.Decode(register, _powerUnit, _timeUnit);
        var firmware = CpuPowerLimit.Decode(_firmwareRegister, _powerUnit, _timeUnit);
        return new CpuPowerLimitReport(
            CpuPowerLimitAvailability.Available,
            null,
            current.Pl1Watts,
            current.Pl2Watts,
            firmware.Pl1Watts,
            firmware.Pl2Watts,
            CpuPowerLimit.MaximumWatts,
            CpuPowerLimit.IsLocked(register));
    }

    private static ulong ReadMsr(PawnIoExecute execute, IntPtr handle, uint msr)
    {
        var input = new ulong[] { msr };
        var output = new ulong[1];
        var result = execute(
            handle,
            "ioctl_read_msr",
            input,
            new UIntPtr(1),
            output,
            new UIntPtr(1),
            out var returned);
        if (result < 0)
        {
            throw new InvalidOperationException(
                $"Reading MSR 0x{msr:X3} failed (HRESULT {FormatResult(result)}).");
        }

        if (returned.ToUInt64() < 1)
        {
            throw new InvalidOperationException(
                $"Reading MSR 0x{msr:X3} returned no value.");
        }

        return output[0];
    }

    private static void WriteMsr(
        PawnIoExecute execute,
        IntPtr handle,
        uint msr,
        ulong value)
    {
        var input = new ulong[] { msr, value };
        var result = execute(
            handle,
            "ioctl_write_msr",
            input,
            new UIntPtr(2),
            Array.Empty<ulong>(),
            UIntPtr.Zero,
            out _);
        if (result < 0)
        {
            throw new InvalidOperationException(
                $"Writing MSR 0x{msr:X3} failed (HRESULT {FormatResult(result)}).");
        }
    }

    private static int LoadModule(PawnIoLoad load, IntPtr handle, byte[] blob)
    {
        var buffer = Marshal.AllocHGlobal(blob.Length);
        try
        {
            Marshal.Copy(blob, 0, buffer, blob.Length);
            return load(handle, buffer, new UIntPtr((uint)blob.Length));
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static TDelegate GetDelegate<TDelegate>(IntPtr library, string name)
        where TDelegate : Delegate =>
        Marshal.GetDelegateForFunctionPointer<TDelegate>(
            NativeLibrary.GetExport(library, name));

    private static string FormatResult(int hresult) => $"0x{(uint)hresult:X8}";

    /// <summary>Intel RAPL is Intel-only; AMD's SMU mailbox is a different channel.</summary>
    private static bool IsAmdCpu(out string? processorName)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            processorName = (key?.GetValue("ProcessorNameString") as string)?.Trim();
            return processorName is not null &&
                (processorName.Contains("AMD", StringComparison.OrdinalIgnoreCase) ||
                 processorName.Contains("Ryzen", StringComparison.OrdinalIgnoreCase) ||
                 processorName.Contains("EPYC", StringComparison.OrdinalIgnoreCase) ||
                 processorName.Contains("Threadripper", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception exception) when (
            exception is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            processorName = null;
            return false;
        }
    }

    private static void ThrowIfLocked(ulong register)
    {
        if (CpuPowerLimit.IsLocked(register))
        {
            throw new InvalidOperationException(
                "The CPU power limit is locked by firmware and cannot be changed.");
        }
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(_disposed, this);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int PawnIoOpen(out IntPtr handle);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int PawnIoLoad(IntPtr handle, IntPtr blob, UIntPtr size);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int PawnIoExecute(
        IntPtr handle,
        [MarshalAs(UnmanagedType.LPStr)] string name,
        [In] ulong[] input,
        UIntPtr inputSize,
        [Out] ulong[] output,
        UIntPtr outputSize,
        out UIntPtr returnSize);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int PawnIoClose(IntPtr handle);
}
