using System.Runtime.InteropServices;
using OpenSynapse.Core.Devices;

namespace OpenSynapse.Windows.Devices;

/// <summary>
/// One PawnIO executor with RyzenSMU.bin loaded. The package power wall and the Curve
/// Optimizer both talk to the SMU mailbox through this, so the P/Invoke surface and the
/// per-family register discovery exist once.
/// </summary>
internal sealed class RyzenSmuSession : IDisposable
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

    private readonly IntPtr _library;
    private readonly IntPtr _handle;
    private readonly PawnIoExecute _execute;
    private readonly PawnIoClose _close;
    private bool _disposed;

    private RyzenSmuSession(
        IntPtr library,
        IntPtr handle,
        PawnIoExecute execute,
        PawnIoClose close,
        ulong codeName)
    {
        _library = library;
        _handle = handle;
        _execute = execute;
        _close = close;
        CodeName = codeName;
    }

    public ulong CodeName { get; }

    /// <summary>
    /// Loads RyzenSMU.bin, resolves the CPU family and snapshots the PM table location.
    /// Only access-denied is worth retrying through the machine service.
    /// </summary>
    public static CpuPowerLimitAvailability TryOpen(
        out RyzenSmuSession? session,
        out string? detail)
    {
        session = null;

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

            session = new RyzenSmuSession(library, handle, execute, close, codeName[0]);
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

    public bool TrySend(uint message, uint argument, out string? error)
    {
        ThrowIfDisposed();
        var input = new ulong[7];
        input[0] = message;
        input[1] = argument;
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

    public bool TryReadPowerTable(out ulong[] table, out string? error)
    {
        ThrowIfDisposed();
        table = [];
        for (var pass = 0; pass < 2; pass++)
        {
            var status = _execute(_handle, UpdatePmTableIoctl, [], 0, [], 0, out _);
            if (status < 0)
            {
                error = $"The SMU power table could not be refreshed (HRESULT 0x{(uint)status:X8}).";
                return false;
            }

            Thread.Sleep(pass == 0 ? 100 : 200);
        }

        var output = new ulong[PmTableWords];
        var read = _execute(_handle, ReadPmTableIoctl, [], 0, output, PmTableWords, out var written);
        if (read < 0 || written.ToUInt64() < 3)
        {
            error = $"The SMU power table could not be read (HRESULT 0x{(uint)read:X8}).";
            return false;
        }

        table = output;
        error = null;
        return true;
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
