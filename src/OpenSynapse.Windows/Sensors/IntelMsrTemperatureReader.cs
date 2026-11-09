using System.Runtime.InteropServices;
using OpenSynapse.Windows.Devices;

namespace OpenSynapse.Windows.Sensors;

/// <summary>
/// Reads the Intel package temperature through the PawnIO IntelMSR module that the
/// CPU power wall already ships. A missing driver or module degrades to null so ACPI
/// can still provide a reading.
/// </summary>
internal sealed class IntelMsrTemperatureReader : IDisposable
{
    private const uint PackageThermStatusMsr = 0x1B1;
    private const uint TemperatureTargetMsr = 0x1A2;
    private const int DefaultTjMaxCelsius = 100;

    private static readonly string PawnIoLibraryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        "PawnIO",
        "PawnIOLib.dll");

    private static readonly string ModulePath =
        Path.Combine(AppContext.BaseDirectory, "IntelMSR.bin");

    private readonly object _gate = new();
    private IntPtr _library;
    private IntPtr _handle;
    private PawnIoExecute? _execute;
    private PawnIoClose? _close;
    private bool _unavailable;
    private bool _disposed;

    internal double? ReadPackageTemperatureCelsius()
    {
        lock (_gate)
        {
            if (_disposed || _unavailable)
            {
                return null;
            }

            try
            {
                EnsureOpen();
                if (_execute is null || _handle == IntPtr.Zero)
                {
                    return null;
                }

                return DecodePackageTemperature(
                    ReadMsr(_execute, _handle, PackageThermStatusMsr),
                    ReadMsr(_execute, _handle, TemperatureTargetMsr));
            }
            catch (Exception exception) when (
                exception is DllNotFoundException or EntryPointNotFoundException or
                BadImageFormatException or IOException or UnauthorizedAccessException or
                InvalidOperationException)
            {
                Close();
                _unavailable = true;
                return null;
            }
        }
    }

    /// <summary>
    /// IA32_PACKAGE_THERM_STATUS reports the distance from TjMax in bits 22:16;
    /// IA32_TEMPERATURE_TARGET carries TjMax in bits 23:16.
    /// </summary>
    internal static double? DecodePackageTemperature(ulong status, ulong target)
    {
        if ((status & 0x8000_0000UL) == 0)
        {
            return null;
        }

        var delta = (int)((status >> 16) & 0x7F);
        var tjMax = (int)((target >> 16) & 0xFF);
        if (tjMax is < 50 or > 125)
        {
            tjMax = DefaultTjMaxCelsius;
        }

        var celsius = tjMax - delta;
        return celsius is >= 1 and <= 125 ? celsius : null;
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
            Close();
        }
    }

    private void EnsureOpen()
    {
        if (_handle != IntPtr.Zero)
        {
            return;
        }

        if (CpuVendor.IsAmd(out _) ||
            !File.Exists(PawnIoLibraryPath) ||
            !File.Exists(ModulePath))
        {
            _unavailable = true;
            return;
        }

        _library = NativeLibrary.Load(PawnIoLibraryPath);
        var open = GetDelegate<PawnIoOpen>(_library, "pawnio_open");
        var load = GetDelegate<PawnIoLoad>(_library, "pawnio_load");
        _execute = GetDelegate<PawnIoExecute>(_library, "pawnio_execute");
        _close = GetDelegate<PawnIoClose>(_library, "pawnio_close");

        if (open(out var handle) < 0)
        {
            throw new InvalidOperationException("PawnIO could not open a CPU temperature executor.");
        }

        _handle = handle;
        if (LoadModule(load, _handle, File.ReadAllBytes(ModulePath)) < 0)
        {
            throw new InvalidOperationException("IntelMSR.bin rejected this CPU.");
        }
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
        if (result < 0 || returned.ToUInt64() < 1)
        {
            throw new InvalidOperationException(
                $"Reading MSR 0x{msr:X3} failed (HRESULT 0x{(uint)result:X8}).");
        }

        return output[0];
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

    private void Close()
    {
        if (_handle != IntPtr.Zero && _close is not null)
        {
            _close(_handle);
        }

        _handle = IntPtr.Zero;
        _close = null;
        _execute = null;
        if (_library != IntPtr.Zero)
        {
            NativeLibrary.Free(_library);
            _library = IntPtr.Zero;
        }
    }

    private static TDelegate GetDelegate<TDelegate>(IntPtr library, string name)
        where TDelegate : Delegate =>
        Marshal.GetDelegateForFunctionPointer<TDelegate>(
            NativeLibrary.GetExport(library, name));

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
