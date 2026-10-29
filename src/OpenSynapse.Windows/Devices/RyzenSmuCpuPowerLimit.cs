using System.Runtime.InteropServices;
using OpenSynapse.Core.Devices;

namespace OpenSynapse.Windows.Devices;

/// <summary>
/// AMD keeps the package wall in the SMU mailbox. PawnIO's RyzenSMU module owns the
/// per-family mailbox registers, so this class only speaks its IOCTLs. The power
/// commands below are the MP1 messages AMD uses for STAPM, fast PPT and slow PPT.
/// </summary>
public sealed class RyzenSmuCpuPowerLimit : ICpuPowerLimitController
{
    private const string ModuleFileName = "RyzenSMU.bin";
    private const string LibraryFileName = "PawnIOLib.dll";

    private const string GetCodeNameIoctl = "ioctl_get_code_name";
    private const string ResolvePmTableIoctl = "ioctl_resolve_pm_table";
    private const string UpdatePmTableIoctl = "ioctl_update_pm_table";
    private const string ReadPmTableIoctl = "ioctl_read_pm_table";
    private const string SendSmuCommandIoctl = "ioctl_send_smu_command";

    private const int PmTableWords = 64;
    private const uint AccessDeniedHresult = 0x80070005;
    private const double MilliwattsPerWatt = 1000;

    // Code names returned by PawnIO.Modules 0.2.11 RyzenSMU.p. Only mobile APUs whose
    // MP1 power messages are the 0x14/0x15/0x16 set are listed.
    private const ulong Renoir = 1;
    private const ulong Rembrandt = 10;
    private const ulong Cezanne = 13;
    private const ulong Lucienne = 22;
    private const ulong Phoenix = 23;
    private const ulong Phoenix2 = 24;
    private const ulong Mendocino = 25;
    private const ulong HawkPoint = 30;
    private const ulong StrixPoint = 31;
    private const ulong StrixHalo = 32;
    private const ulong KrackanPoint = 33;
    private const ulong KrackanPoint2 = 34;

    private readonly IntPtr _library;
    private readonly IntPtr _handle;
    private readonly PawnIoExecute _execute;
    private readonly PawnIoClose _close;
    private readonly SmuCommands _commands;
    private readonly CpuPowerLimit _firmware;
    private bool _disposed;

    private RyzenSmuCpuPowerLimit(
        IntPtr library,
        IntPtr handle,
        PawnIoExecute execute,
        PawnIoClose close,
        SmuCommands commands,
        CpuPowerLimit firmware)
    {
        _library = library;
        _handle = handle;
        _execute = execute;
        _close = close;
        _commands = commands;
        _firmware = firmware;
    }

    /// <summary>
    /// Loads RyzenSMU.bin, resolves the CPU family and snapshots the power table.
    /// Only access-denied is worth retrying through the machine service.
    /// </summary>
    public static CpuPowerLimitAvailability TryOpen(
        out ICpuPowerLimitController? controller,
        out string? detail)
    {
        controller = null;

        var libraryPath = FindPawnIoLibrary();
        if (libraryPath is null)
        {
            detail = "PawnIO is not installed; the Ryzen SMU path needs its signed driver.";
            return CpuPowerLimitAvailability.PawnIoMissing;
        }

        var modulePath = Path.Combine(AppContext.BaseDirectory, ModuleFileName);
        if (!File.Exists(modulePath))
        {
            detail = $"The PawnIO SMU module is missing ({modulePath}).";
            return CpuPowerLimitAvailability.ModuleMissing;
        }

        var library = IntPtr.Zero;
        var handle = IntPtr.Zero;
        PawnIoClose? close = null;
        try
        {
            library = NativeLibrary.Load(libraryPath);
            var open = GetDelegate<PawnIoOpen>(library, "pawnio_open");
            var load = GetDelegate<PawnIoLoad>(library, "pawnio_load");
            var execute = GetDelegate<PawnIoExecute>(library, "pawnio_execute");
            close = GetDelegate<PawnIoClose>(library, "pawnio_close");

            var openResult = open(out handle);
            if (openResult < 0)
            {
                NativeLibrary.Free(library);
                detail = $"PawnIO could not open an executor (HRESULT 0x{(uint)openResult:X8}).";
                return (uint)openResult == AccessDeniedHresult
                    ? CpuPowerLimitAvailability.AccessDenied
                    : CpuPowerLimitAvailability.Failed;
            }

            var loadResult = LoadModule(load, handle, File.ReadAllBytes(modulePath));
            if (loadResult < 0)
            {
                close(handle);
                NativeLibrary.Free(library);
                detail = $"RyzenSMU.bin rejected this CPU (HRESULT 0x{(uint)loadResult:X8}).";
                return CpuPowerLimitAvailability.Failed;
            }

            var codeName = new ulong[1];
            var codeNameResult = execute(
                handle, GetCodeNameIoctl, [], 0, codeName, 1, out var codeNameWritten);
            if (codeNameResult < 0 || codeNameWritten.ToUInt64() != 1)
            {
                close(handle);
                NativeLibrary.Free(library);
                detail = $"The Ryzen code name could not be read (HRESULT 0x{(uint)codeNameResult:X8}).";
                return CpuPowerLimitAvailability.Failed;
            }

            var commands = CommandsFor(codeName[0]);
            if (commands is null)
            {
                close(handle);
                NativeLibrary.Free(library);
                detail = "This Ryzen family does not use the supported SMU power messages.";
                return CpuPowerLimitAvailability.Failed;
            }

            var resolved = new ulong[2];
            var resolveResult = execute(
                handle, ResolvePmTableIoctl, [], 0, resolved, 2, out var resolvedWritten);
            if (resolveResult < 0 || resolvedWritten.ToUInt64() != 2)
            {
                close(handle);
                NativeLibrary.Free(library);
                detail = $"The SMU power table could not be located (HRESULT 0x{(uint)resolveResult:X8}).";
                return CpuPowerLimitAvailability.Failed;
            }

            if (!TryReadTable(execute, handle, out var table, out var readError) ||
                !TryDecode(table, out var firmware, out readError))
            {
                close(handle);
                NativeLibrary.Free(library);
                detail = readError;
                return CpuPowerLimitAvailability.Failed;
            }

            controller = new RyzenSmuCpuPowerLimit(
                library, handle, execute, close, commands.Value, firmware);
            detail = null;
            return CpuPowerLimitAvailability.Available;
        }
        catch (Exception exception) when (
            exception is DllNotFoundException or EntryPointNotFoundException or
                BadImageFormatException or IOException or UnauthorizedAccessException)
        {
            if (handle != IntPtr.Zero && close is not null)
            {
                close(handle);
            }

            if (library != IntPtr.Zero)
            {
                NativeLibrary.Free(library);
            }

            detail = $"The Ryzen SMU path could not be opened: {exception.Message}";
            return CpuPowerLimitAvailability.Failed;
        }
    }

    public CpuPowerLimitReport Read()
    {
        ThrowIfDisposed();
        return TryReadTable(_execute, _handle, out var table, out var error) &&
            TryDecode(table, out var limit, out error)
                ? BuildReport(limit)
                : CpuPowerLimitReport.Unavailable(CpuPowerLimitAvailability.Failed, error);
    }

    public CpuPowerLimitReport Apply(CpuPowerLimit limit)
    {
        ThrowIfDisposed();
        return Write(limit.Clamp(CpuPowerLimit.MaximumWatts));
    }

    public CpuPowerLimitReport RestoreTo(double pl1Watts, double pl2Watts)
    {
        ThrowIfDisposed();
        var request = new CpuPowerLimit(pl1Watts, pl2Watts, 0, 0)
            .Clamp(CpuPowerLimit.MaximumRestoreWatts);
        return Write(request);
    }

    public CpuPowerLimitReport RestoreFirmware()
    {
        ThrowIfDisposed();
        return RestoreTo(_firmware.Pl1Watts, _firmware.Pl2Watts);
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
        catch (Exception exception) when (
            exception is EntryPointNotFoundException or SEHException)
        {
        }

        NativeLibrary.Free(_library);
    }

    internal static SmuCommands? CommandsFor(ulong codeName) => codeName switch
    {
        Renoir or Lucienne or Cezanne or Rembrandt or Phoenix or Phoenix2 or
            Mendocino or HawkPoint or StrixPoint or StrixHalo or
            KrackanPoint or KrackanPoint2 => new SmuCommands(0x14, 0x15, 0x16),
        _ => null,
    };

    internal static bool TryDecode(
        ulong[] table,
        out CpuPowerLimit limit,
        out string? error)
    {
        limit = default;
        if (table.Length < 3)
        {
            error = "The SMU power table is shorter than the two PPT limits.";
            return false;
        }

        var fast = FloatAt(table, 2) / MilliwattsPerWatt;
        var slow = FloatAt(table, 4) / MilliwattsPerWatt;
        if (!(fast > 0 && slow > 0 && fast < 500 && slow < 500))
        {
            error = $"The SMU power table returned implausible limits ({slow} W / {fast} W).";
            return false;
        }

        limit = new CpuPowerLimit(
            Math.Min(slow, fast),
            Math.Max(slow, fast),
            CpuPowerLimit.MinimumTimeSeconds,
            CpuPowerLimit.MaximumTimeSeconds);
        error = null;
        return true;
    }

    private static float FloatAt(ulong[] table, int index) =>
        BitConverter.Int32BitsToSingle(
            (int)((table[index / 2] >> ((index % 2) * 32)) & 0xFFFFFFFF));

    internal static uint ToMilliwatts(double watts) =>
        (uint)Math.Round(watts * MilliwattsPerWatt);

    private CpuPowerLimitReport Write(CpuPowerLimit limit)
    {
        var slow = ToMilliwatts(limit.Pl1Watts);
        var fast = ToMilliwatts(limit.Pl2Watts);
        if (!TrySend(_commands.Stapm, slow, out var error) ||
            !TrySend(_commands.Slow, slow, out error) ||
            !TrySend(_commands.Fast, fast, out error))
        {
            return CpuPowerLimitReport.Unavailable(CpuPowerLimitAvailability.Failed, error);
        }

        // A rejected message is the only reliable failure signal; when the table cannot
        // be re-read, report what was accepted instead of failing a write that worked.
        return TryReadTable(_execute, _handle, out var table, out _) &&
            TryDecode(table, out var actual, out _)
                ? BuildReport(actual)
                : BuildReport(limit);
    }

    private bool TrySend(uint message, uint milliwatts, out string? error)
    {
        var input = new ulong[7];
        input[0] = message;
        input[1] = milliwatts;
        var status = _execute(
            _handle, SendSmuCommandIoctl, input, 7, new ulong[6], 6, out _);
        if (status < 0)
        {
            error = $"The SMU rejected command 0x{message:X2} (HRESULT 0x{(uint)status:X8}).";
            return false;
        }

        error = null;
        return true;
    }

    private static bool TryReadTable(
        PawnIoExecute execute,
        IntPtr handle,
        out ulong[] table,
        out string? error)
    {
        table = [];
        for (var pass = 0; pass < 2; pass++)
        {
            var status = execute(handle, UpdatePmTableIoctl, [], 0, [], 0, out _);
            if (status < 0)
            {
                error = $"The SMU power table could not be refreshed (HRESULT 0x{(uint)status:X8}).";
                return false;
            }

            Thread.Sleep(pass == 0 ? 100 : 200);
        }

        var output = new ulong[PmTableWords];
        var read = execute(handle, ReadPmTableIoctl, [], 0, output, PmTableWords, out var written);
        if (read < 0 || written.ToUInt64() < 3)
        {
            error = $"The SMU power table could not be read (HRESULT 0x{(uint)read:X8}).";
            return false;
        }

        table = output;
        error = null;
        return true;
    }

    private CpuPowerLimitReport BuildReport(CpuPowerLimit current) => new(
        CpuPowerLimitAvailability.Available,
        null,
        current.Pl1Watts,
        current.Pl2Watts,
        _firmware.Pl1Watts,
        _firmware.Pl2Watts,
        CpuPowerLimit.MaximumWatts,
        false);

    private static string? FindPawnIoLibrary()
    {
        foreach (var root in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                 })
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                continue;
            }

            var candidate = Path.Combine(root, "PawnIO", LibraryFileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
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
        Marshal.GetDelegateForFunctionPointer<TDelegate>(NativeLibrary.GetExport(library, name));

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    internal readonly record struct SmuCommands(uint Stapm, uint Fast, uint Slow);

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
