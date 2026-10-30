using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace OpenSynapse.Windows.Devices;

internal interface IRazerDkmControl
{
    bool TryRead(byte[] buffer, out string? error);
}

/// <summary>
/// Reads the Razer filter driver's 256-byte slot table. This type never sends
/// the DKM write command (0x88883104).
/// </summary>
internal static class RazerDkmTableReader
{
    internal const uint GetDkmTableControlCode = 0x88883108;
    internal const int TableLength = 0x100;

    internal static bool TryRead(
        string devicePath,
        out byte[] table,
        out string? error)
    {
        table = [];
        if (string.IsNullOrWhiteSpace(devicePath))
        {
            error = "The Razer filter endpoint path is empty.";
            return false;
        }

        try
        {
            using var control = new WindowsRazerDkmControl(devicePath);
            var buffer = new byte[TableLength];
            if (!TryRead(control, buffer, out error))
            {
                return false;
            }

            table = buffer;
            return true;
        }
        catch (Exception ex)
        {
            error = $"Could not read the Razer DKM table: {ex.Message}";
            return false;
        }
    }

    internal static bool TryRead(
        IRazerDkmControl control,
        byte[] table,
        out string? error)
    {
        error = null;
        if (control is null)
        {
            error = "The Razer DKM control is unavailable.";
            return false;
        }

        if (table.Length != TableLength)
        {
            error = $"The Razer DKM table buffer must be exactly {TableLength} (0x100) bytes.";
            return false;
        }

        try
        {
            if (!control.TryRead(table, out error))
            {
                error ??= "The Razer DKM table read failed.";
                return false;
            }

            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = $"Could not read the Razer DKM table: {ex.Message}";
            return false;
        }
    }
}

internal sealed class WindowsRazerDkmControl : IRazerDkmControl, IDisposable
{
    private const uint ShareReadWrite = 3;
    private const uint OpenExisting = 3;
    private const uint FileFlagOverlapped = 0x40000000;
    private const int ErrorIoPending = 997;
    private const int TimeoutMilliseconds = 200;

    private readonly SafeFileHandle _handle;

    internal WindowsRazerDkmControl(string devicePath)
    {
        _handle = NativeMethods.CreateFile(
            devicePath,
            0,
            ShareReadWrite,
            IntPtr.Zero,
            OpenExisting,
            FileFlagOverlapped,
            IntPtr.Zero);
        if (_handle.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            _handle.Dispose();
            throw new Win32Exception(
                error,
                "Could not open the Razer filter endpoint for the DKM table read.");
        }
    }

    public bool TryRead(byte[] buffer, out string? error)
    {
        error = null;
        if (buffer.Length != RazerDkmTableReader.TableLength)
        {
            error = $"The Razer DKM table buffer must be exactly {RazerDkmTableReader.TableLength} (0x100) bytes.";
            return false;
        }

        using var read = new PendingRead();
        try
        {
            if (!NativeMethods.DeviceIoControl(
                    _handle,
                    RazerDkmTableReader.GetDkmTableControlCode,
                    IntPtr.Zero,
                    0,
                    read.Buffer,
                    RazerDkmTableReader.TableLength,
                    out var returned,
                    read.Overlapped))
            {
                var win32Error = Marshal.GetLastWin32Error();
                if (win32Error != ErrorIoPending)
                {
                    error = FailureMessage(win32Error);
                    return false;
                }

                if (!read.Wait(TimeoutMilliseconds))
                {
                    NativeMethods.CancelIoEx(_handle, read.Overlapped);
                    read.Abandon();
                    error = $"The Razer DKM table read timed out after {TimeoutMilliseconds} ms.";
                    return false;
                }

                if (!NativeMethods.GetOverlappedResult(
                        _handle,
                        read.Overlapped,
                        out returned,
                        false))
                {
                    error = FailureMessage(Marshal.GetLastWin32Error());
                    return false;
                }
            }

            if (returned != RazerDkmTableReader.TableLength)
            {
                error =
                    $"Razer filter IOCTL 0x{RazerDkmTableReader.GetDkmTableControlCode:X8} " +
                    $"returned {returned} bytes instead of {RazerDkmTableReader.TableLength}.";
                return false;
            }

            Marshal.Copy(read.Buffer, buffer, 0, RazerDkmTableReader.TableLength);
            return true;
        }
        finally
        {
            read.Dispose();
        }
    }

    public void Dispose() => _handle.Dispose();

    private static string FailureMessage(int error) =>
        $"Razer filter IOCTL 0x{RazerDkmTableReader.GetDkmTableControlCode:X8} failed: " +
        new Win32Exception(error).Message;

    private sealed class PendingRead : IDisposable
    {
        private readonly EventWaitHandle _event = new(false, EventResetMode.AutoReset);
        private int _abandoned;
        private int _released;

        internal PendingRead()
        {
            Buffer = Marshal.AllocHGlobal(RazerDkmTableReader.TableLength);
            Overlapped = Marshal.AllocHGlobal(Marshal.SizeOf<NativeOverlappedData>());
            Marshal.StructureToPtr(
                new NativeOverlappedData
                {
                    EventHandle = _event.SafeWaitHandle.DangerousGetHandle(),
                },
                Overlapped,
                false);
        }

        internal IntPtr Buffer { get; }

        internal IntPtr Overlapped { get; }

        internal bool Wait(int timeoutMilliseconds) =>
            _event.WaitOne(timeoutMilliseconds);

        internal void Abandon()
        {
            Volatile.Write(ref _abandoned, 1);
            ThreadPool.RegisterWaitForSingleObject(
                _event,
                static (state, _) => ((PendingRead)state!).Release(),
                this,
                Timeout.Infinite,
                executeOnlyOnce: true);
        }

        public void Dispose()
        {
            if (Volatile.Read(ref _abandoned) == 0)
            {
                Release();
            }
        }

        private void Release()
        {
            if (Interlocked.Exchange(ref _released, 1) != 0)
            {
                return;
            }

            Marshal.FreeHGlobal(Buffer);
            Marshal.FreeHGlobal(Overlapped);
            _event.Dispose();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeOverlappedData
    {
        internal IntPtr Internal;
        internal IntPtr InternalHigh;
        internal uint Offset;
        internal uint OffsetHigh;
        internal IntPtr EventHandle;
    }

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern SafeFileHandle CreateFile(
            string fileName,
            uint desiredAccess,
            uint shareMode,
            IntPtr securityAttributes,
            uint creationDisposition,
            uint flagsAndAttributes,
            IntPtr templateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool DeviceIoControl(
            SafeFileHandle device,
            uint controlCode,
            IntPtr input,
            int inputLength,
            IntPtr output,
            int outputLength,
            out uint bytesReturned,
            IntPtr overlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetOverlappedResult(
            SafeFileHandle device,
            IntPtr overlapped,
            out uint bytesTransferred,
            [MarshalAs(UnmanagedType.Bool)] bool wait);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CancelIoEx(SafeFileHandle device, IntPtr overlapped);
    }
}
