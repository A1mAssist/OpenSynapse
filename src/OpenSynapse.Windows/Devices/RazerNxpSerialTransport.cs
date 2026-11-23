using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

internal interface IRazerNxpSerialTransport : IAsyncDisposable
{
    Task<byte[]> QueryAsync(byte[] request, CancellationToken cancellationToken);
}

/// <summary>Native COM transport for the AudioNXPDevice serial bridge.</summary>
internal sealed class RazerNxpSerialTransport : IRazerNxpSerialTransport
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Ports = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _exchange = new(1, 1);
    private readonly SemaphoreSlim _port;
    private readonly Func<byte[], CancellationToken, byte[]> _runExchange;
    private bool _disposed;

    public RazerNxpSerialTransport(string devicePath)
        : this(devicePath, (request, token) => Exchange(devicePath, request, token)) { }

    internal RazerNxpSerialTransport(string devicePath, Func<byte[], CancellationToken, byte[]> runExchange)
    {
        _port = Ports.GetOrAdd(devicePath, _ => new SemaphoreSlim(1, 1));
        _runExchange = runExchange;
    }

    public async Task<byte[]> QueryAsync(byte[] request, CancellationToken cancellationToken)
    {
        await _exchange.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!await _port.WaitAsync(TimeSpan.FromSeconds(3), cancellationToken).ConfigureAwait(false))
                throw new IOException("NXP serial port is busy with another exchange.");
            try
            {
                return await Task.Run(() => _runExchange(request, cancellationToken), cancellationToken).ConfigureAwait(false);
            }
            finally { _port.Release(); }
        }
        finally { _exchange.Release(); }
    }

    private static byte[] Exchange(string devicePath, byte[] request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Close before releasing the shared gate, so polling and profile services can use this port.
        using var handle = OpenPort(devicePath);
        Check(PurgeComm(handle, 0x0008)); // Drop stale input before issuing a new request.
        Check(WriteFile(handle, request, request.Length, out var written, IntPtr.Zero));
        if (written != request.Length) throw new IOException("NXP serial write was incomplete.");
        var pending = new List<byte>();
        var buffer = new byte[258];
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromSeconds(2))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Check(ReadFile(handle, buffer, buffer.Length, out var count, IntPtr.Zero));
            pending.AddRange(buffer.AsSpan(0, count).ToArray());
            while (TryTakeEvent(pending, out var frame))
            {
                if (!RazerNxpHeadsetProtocol.MatchesResponse(request, frame)) continue;
                RazerNxpHeadsetProtocol.ValidateResponse(request, frame);
                return frame;
            }
        }
        throw new IOException("NXP serial command timed out without a matching response.");
    }

    internal static bool TryTakeEvent(List<byte> pending, out byte[] frame)
    {
        frame = [];
        if (pending.Count == 0) return false;
        // H4 event: 04, event code, parameter length, parameters. Commands use H4 type 01.
        if (pending[0] != 4) throw new InvalidDataException("Unexpected NXP H4 event packet type.");
        if (pending.Count < 3 || pending.Count < pending[2] + 3) return false;
        var length = pending[2] + 3;
        frame = pending.GetRange(0, length).ToArray();
        pending.RemoveRange(0, length);
        return true;
    }

    private static SafeFileHandle OpenPort(string path)
    {
        if (!RazerNxpSerialDiscovery.IsNxpInterface(path))
            throw new ArgumentException("An enumerated 1532:052C/052D COM interface is required.", nameof(path));
        var handle = CreateFileW(path, 0xC0000000, 0, IntPtr.Zero, 3, 0, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw new Win32Exception(error);
        }
        try
        {
            var dcb = new Dcb { Length = (uint)Marshal.SizeOf<Dcb>() };
            Check(GetCommState(handle, ref dcb));
            // Electron serial/index.js: 115200; serialport bindings: 8N1, no flow control.
            dcb.BaudRate = 115200;
            dcb.Flags = 0x1011; // binary, DTR enabled, RTS enabled; all flow control disabled.
            dcb.ByteSize = 8;
            dcb.Parity = 0;
            dcb.StopBits = 0;
            Check(SetCommState(handle, ref dcb));
            var timeouts = new CommTimeouts { ReadInterval = uint.MaxValue, ReadConstant = 50, WriteConstant = 500 };
            Check(SetCommTimeouts(handle, ref timeouts));
            return handle;
        }
        catch { handle.Dispose(); throw; }
    }

    public async ValueTask DisposeAsync()
    {
        await _exchange.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed) return;
            _disposed = true;
        }
        finally { _exchange.Release(); }
    }

    private static void Check(bool success)
    {
        if (!success) throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Dcb
    {
        public uint Length, BaudRate, Flags;
        public ushort Reserved, XonLimit, XoffLimit;
        public byte ByteSize, Parity, StopBits, XonChar, XoffChar, ErrorChar, EofChar, EvtChar;
        public ushort Reserved1;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CommTimeouts
    {
        public uint ReadInterval, ReadMultiplier, ReadConstant, WriteMultiplier, WriteConstant;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCommState(SafeFileHandle handle, ref Dcb dcb);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetCommState(SafeFileHandle handle, ref Dcb dcb);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetCommTimeouts(SafeFileHandle handle, ref CommTimeouts timeouts);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PurgeComm(SafeFileHandle handle, uint flags);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadFile(SafeFileHandle handle, [Out] byte[] buffer, int count, out int read, IntPtr overlapped);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WriteFile(SafeFileHandle handle, byte[] buffer, int count, out int written, IntPtr overlapped);
}
