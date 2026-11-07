using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Threading;
using OpenSynapse.ChromaSdk.Protocol;

namespace OpenSynapse.ChromaSdk.Shim;

internal sealed class ChromaPipeClient : IDisposable
{
    private const int ConnectTimeoutMilliseconds = 500;
    private const int DisposeTimeoutMilliseconds = 500;

    private readonly NamedPipeClientStream _pipe;
    private readonly object _ioLock = new();
    private readonly object _pendingLock = new();
    private readonly Thread _writer;
    private ChromaSdkMessage? _pending;
    private bool _stop;
    private volatile bool _connected = true;

    private ChromaPipeClient(NamedPipeClientStream pipe, uint supportedDevices)
    {
        _pipe = pipe;
        SupportedDevices = supportedDevices;
        _writer = new Thread(WriteLoop)
        {
            IsBackground = true,
            Name = "OpenSynapse Chroma SDK pipe writer",
        };
        _writer.Start();
    }

    public uint SupportedDevices { get; private set; }

    public bool IsConnected
    {
        get
        {
            if (!_connected) return false;
            try
            {
                if (PeekNamedPipe(_pipe.SafePipeHandle.DangerousGetHandle(), IntPtr.Zero, 0, IntPtr.Zero, out _, IntPtr.Zero))
                {
                    return true;
                }
            }
            catch
            {
                // Disposed or invalid handles count as disconnected.
            }
            _connected = false;
            return false;
        }
    }

    public static int Connect(string title, uint supportedDevices, out ChromaPipeClient? client)
    {
        client = null;
        NamedPipeClientStream? pipe = null;
        try
        {
            pipe = new NamedPipeClientStream(".", ChromaSdkPipe.Name, PipeDirection.InOut, PipeOptions.None);
            pipe.Connect(ConnectTimeoutMilliseconds);
            ChromaSdkPipe.Write(pipe, new ChromaSdkHelloMessage(new ChromaSdkHello(
                ChromaSdkPipe.ProtocolVersion,
                Environment.ProcessId,
                title,
                supportedDevices)));

            if (ChromaSdkPipe.Read(pipe) is not ChromaSdkHelloAckMessage ack)
            {
                pipe.Dispose();
                return SdkResult.ServiceNotActive;
            }
            if (ack.Value.Result != SdkResult.Success)
            {
                pipe.Dispose();
                return ack.Value.Result;
            }

            client = new ChromaPipeClient(pipe, ack.Value.SupportedDevices);
            return SdkResult.Success;
        }
        catch
        {
            pipe?.Dispose();
            return SdkResult.ServiceNotActive;
        }
    }

    public bool Enqueue(ChromaSdkMessage message)
    {
        lock (_pendingLock)
        {
            if (!_connected) return false;
            _pending = message;
            Monitor.Pulse(_pendingLock);
            return true;
        }
    }

    public uint Query()
    {
        lock (_ioLock)
        {
            if (!_connected) throw new IOException("The Chroma SDK pipe is disconnected.");
            ChromaSdkPipe.Write(_pipe, new ChromaSdkQueryMessage());
            if (ChromaSdkPipe.Read(_pipe) is not ChromaSdkQueryAckMessage ack)
            {
                throw new InvalidDataException("Unexpected Chroma SDK query response.");
            }
            SupportedDevices = ack.Value.SupportedDevices;
            return SupportedDevices;
        }
    }

    public void Dispose()
    {
        lock (_pendingLock)
        {
            _stop = true;
            _pending = null;
            Monitor.PulseAll(_pendingLock);
        }

        _writer.Join(DisposeTimeoutMilliseconds);

        if (Monitor.TryEnter(_ioLock, DisposeTimeoutMilliseconds))
        {
            try
            {
                if (_connected)
                {
                    ChromaSdkPipe.Write(_pipe, new ChromaSdkByeMessage());
                }
            }
            catch
            {
                // The host may already be gone; closing the pipe is still required.
            }
            finally
            {
                Monitor.Exit(_ioLock);
            }
        }

        _connected = false;
        _pipe.Dispose();
    }

    private void WriteLoop()
    {
        while (true)
        {
            ChromaSdkMessage? message;
            lock (_pendingLock)
            {
                while (_pending is null && !_stop)
                {
                    Monitor.Wait(_pendingLock);
                }
                if (_pending is null && _stop) return;
                message = _pending!;
                _pending = null;
            }

            try
            {
                lock (_ioLock)
                {
                    if (!_connected) return;
                    ChromaSdkPipe.Write(_pipe, message);
                }
            }
            catch
            {
                _connected = false;
                return;
            }
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool PeekNamedPipe(
        IntPtr hNamedPipe,
        IntPtr lpBuffer,
        uint nBufferSize,
        IntPtr lpBytesRead,
        out uint lpTotalBytesAvail,
        IntPtr lpBytesLeftThisMessage);
}
