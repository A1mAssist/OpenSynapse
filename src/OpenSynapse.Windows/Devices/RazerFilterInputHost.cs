using System.ComponentModel;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using Microsoft.Win32.SafeHandles;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

/// <summary>
/// Product 710 keyboard input reader for the installed Razer filter driver.
/// This host does not discover the RZCONTROL path and is not wired to App startup.
/// </summary>
public sealed class RazerFilterInputHost : IAsyncDisposable
{
    internal const uint EnableInputHooks = 0x88883034;
    internal const uint EnableInputNotify = 0x88883038;

    private static readonly (ushort ScanCode, ushort Flag)[] Product710Hooks =
    [
        (0x13, 0), (0x14, 0), (0x19, 0), (0x2A, 0), (0x30, 0),
        (0x3B, 0), (0x3C, 0), (0x3D, 0), (0x3E, 0), (0x3F, 0),
        (0x40, 0), (0x41, 0), (0x42, 0), (0x43, 0), (0x44, 0),
        (0x48, 2), (0x49, 2), (0x4B, 2), (0x4D, 2), (0x50, 2),
        (0x51, 2), (0x57, 0), (0x58, 0),
    ];

    private readonly IRazerFilterDriverChannel _driver;
    private readonly Action<BladeMappingInputEvent> _eventHandler;
    private readonly CancellationTokenSource _stop = new();
    private readonly object _disposeSync = new();
    private readonly Channel<BladeMappingInputEvent> _events =
        Channel.CreateUnbounded<BladeMappingInputEvent>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });
    private readonly object _lifecycleGate = new();
    private readonly List<(ushort ScanCode, ushort Flag)> _installedHooks = [];
    private Task[] _readers = [];
    private Task? _consumer;
    private Task? _completion;
    private Task? _disposeTask;
    private bool _hooksEnabled;
    private bool _notifyEnabled;
    private bool _accepting;
    private bool _stopped;
    private bool _started;
    private bool _driverDisposed;
    private bool _disposed;

    public RazerFilterInputHost(
        string devicePath,
        Action<BladeMappingInputEvent> eventHandler)
        : this(new WindowsRazerFilterDriverChannel(devicePath), eventHandler)
    {
    }

    internal RazerFilterInputHost(
        IRazerFilterDriverChannel driver,
        Action<BladeMappingInputEvent> eventHandler)
    {
        _driver = driver ?? throw new ArgumentNullException(nameof(driver));
        _eventHandler = eventHandler ?? throw new ArgumentNullException(nameof(eventHandler));
    }

    public Task Completion => _completion ?? Task.CompletedTask;

    internal static IReadOnlyList<(ushort ScanCode, ushort Flag)> OfficialProduct710Hooks =>
        Product710Hooks;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started)
        {
            throw new InvalidOperationException("Razer filter input host is already started.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        _started = true;
        _accepting = true;
        var startedReaders = new List<Task>(2);
        try
        {
            _driver.PostRead(0);
            startedReaders.Add(ReadLoopAsync(0));
            _driver.PostRead(1);
            startedReaders.Add(ReadLoopAsync(1));
            EnsureCompletion(startedReaders);

            WriteEnabled(EnableInputHooks, true);
            _hooksEnabled = true;
            WriteEnabled(EnableInputNotify, true);
            _notifyEnabled = true;

            foreach (var hook in Product710Hooks)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _driver.WriteControl(
                    RazerFilterInputProtocol.SetInputHook,
                    RazerFilterInputProtocol.CreateKeyboardHook(hook.ScanCode, hook.Flag));
                _installedHooks.Add(hook);
            }

        }
        catch (Exception startupError)
        {
            var cleanupError = StopDriver();
            EnsureCompletion(startedReaders);
            cleanupError ??= await ObserveCompletionAsync().ConfigureAwait(false);
            DisposeDriver();
            _disposed = true;
            _stop.Dispose();
            if (cleanupError is not null)
            {
                throw new AggregateException(startupError, cleanupError);
            }
            ExceptionDispatchInfo.Capture(startupError).Throw();
            throw;
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_disposeSync)
        {
            return new ValueTask(_disposeTask ??= DisposeCoreAsync());
        }
    }

    private async Task DisposeCoreAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        var completionError = StopDriver();
        try
        {
            await Completion.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            completionError ??= exception;
        }
        finally
        {
            DisposeDriver();
            _stop.Dispose();
        }

        if (completionError is not null)
        {
            ExceptionDispatchInfo.Capture(completionError).Throw();
        }
    }

    private async Task ReadLoopAsync(int slot)
    {
        try
        {
            while (true)
            {
                var frame = await _driver.CompleteReadAsync(slot).ConfigureAwait(false);
                lock (_lifecycleGate)
                {
                    _stop.Token.ThrowIfCancellationRequested();

                    // Match the official double-buffer flow: re-arm this context before
                    // processing the completed frame.
                    _driver.PostRead(slot);
                }
                if (_accepting &&
                    RazerFilterInputProtocol.TryParseInputFrame(frame.Span, out var input))
                {
                    _events.Writer.TryWrite(input);
                }
            }
        }
        catch
        {
            StopDriver();
            throw;
        }
    }

    private async Task ConsumeAsync()
    {
        await foreach (var input in _events.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            if (_accepting)
            {
                _eventHandler(input);
            }
        }
    }

    private async Task SuperviseAsync(Task[] readers, Task consumer)
    {
        var allReaders = Task.WhenAll(readers);
        await Task.WhenAny(allReaders, consumer).ConfigureAwait(false);
        if (!_stop.IsCancellationRequested)
        {
            StopDriver();
        }

        Exception? error = null;
        try
        {
            await allReaders.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            error = exception;
        }

        _events.Writer.TryComplete(error);
        try
        {
            await consumer.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            error ??= exception;
        }

        if (error is not null)
        {
            ExceptionDispatchInfo.Capture(error).Throw();
        }
    }

    private Exception? StopDriver()
    {
        lock (_lifecycleGate)
        {
            if (_stopped)
            {
                return null;
            }

            _stopped = true;
            _accepting = false;
            _stop.Cancel();
            var errors = new List<Exception>();
            for (var index = _installedHooks.Count - 1; index >= 0; index--)
            {
                var hook = _installedHooks[index];
                TryControl(
                    () => _driver.WriteControl(
                        RazerFilterInputProtocol.ClearInputHook,
                        RazerFilterInputProtocol.CreateKeyboardClearKey(hook.ScanCode, hook.Flag)),
                    errors);
            }
            _installedHooks.Clear();

            TryControl(
                () =>
                {
                    if (_notifyEnabled)
                    {
                        WriteEnabled(EnableInputNotify, false);
                        _notifyEnabled = false;
                    }
                },
                errors);
            TryControl(
                () =>
                {
                    if (_hooksEnabled)
                    {
                        WriteEnabled(EnableInputHooks, false);
                        _hooksEnabled = false;
                    }
                },
                errors);
            TryControl(_driver.CancelPendingReads, errors);
            var error = errors.Count == 0 ? null : new AggregateException(errors);
            _events.Writer.TryComplete(error);
            return error;
        }
    }

    private void WriteEnabled(uint controlCode, bool enabled) =>
        _driver.WriteControl(controlCode, BitConverter.GetBytes(enabled ? 1 : 0));

    private static void TryControl(Action action, List<Exception> errors)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            errors.Add(exception);
        }
    }

    private async Task<Exception?> ObserveCompletionAsync()
    {
        try
        {
            await Completion.ConfigureAwait(false);
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private void EnsureCompletion(IReadOnlyCollection<Task> readers)
    {
        if (_completion is not null)
        {
            return;
        }

        _readers = [.. readers];
        _consumer = ConsumeAsync();
        _completion = SuperviseAsync(_readers, _consumer);
    }

    private void DisposeDriver()
    {
        if (_driverDisposed)
        {
            return;
        }

        _driverDisposed = true;
        _driver.Dispose();
    }
}

internal interface IRazerFilterDriverChannel : IDisposable
{
    void PostRead(int slot);

    Task<ReadOnlyMemory<byte>> CompleteReadAsync(int slot);

    void WriteControl(uint controlCode, byte[] payload);

    void CancelPendingReads();
}

internal sealed class WindowsRazerFilterDriverChannel : IRazerFilterDriverChannel
{
    private const uint ShareReadWrite = 3;
    private const uint OpenExisting = 3;
    private const uint FileFlagOverlapped = 0x40000000;
    private const int ErrorIoPending = 997;
    private const int ErrorOperationAborted = 995;
    private const int ErrorNotFound = 1168;

    private readonly SafeFileHandle _handle;
    private readonly ReadContext[] _reads;
    private bool _disposed;

    internal WindowsRazerFilterDriverChannel(string devicePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(devicePath);
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
            throw new Win32Exception(error, "Could not open the Razer filter endpoint.");
        }

        _reads = [new ReadContext(), new ReadContext()];
    }

    public void PostRead(int slot)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var context = GetContext(slot);
        context.Prepare();
        if (!NativeMethods.DeviceIoControl(
                _handle,
                RazerFilterInputProtocol.ReadInput,
                IntPtr.Zero,
                0,
                context.Buffer,
                RazerFilterInputProtocol.InputFrameLength,
                out _,
                context.Overlapped))
        {
            var error = Marshal.GetLastWin32Error();
            if (error != ErrorIoPending)
            {
                throw new Win32Exception(error, "Could not post the Razer filter input read.");
            }
        }
    }

    public async Task<ReadOnlyMemory<byte>> CompleteReadAsync(int slot)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var context = GetContext(slot);
        await Task.Run(context.WaitHandle.WaitOne).ConfigureAwait(false);
        if (!NativeMethods.GetOverlappedResult(
                _handle,
                context.Overlapped,
                out var length,
                false))
        {
            var error = Marshal.GetLastWin32Error();
            if (error == ErrorOperationAborted)
            {
                throw new OperationCanceledException("Razer filter read was canceled.");
            }

            throw new Win32Exception(error, "Razer filter input read failed.");
        }

        var frame = new byte[checked((int)length)];
        Marshal.Copy(context.Buffer, frame, 0, frame.Length);
        return frame;
    }

    public void WriteControl(uint controlCode, byte[] payload)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(payload);
        using var completion = new OverlappedCompletion();
        var pin = GCHandle.Alloc(payload, GCHandleType.Pinned);
        try
        {
            if (NativeMethods.DeviceIoControl(
                    _handle,
                    controlCode,
                    pin.AddrOfPinnedObject(),
                    payload.Length,
                    IntPtr.Zero,
                    0,
                    out _,
                    completion.Overlapped))
            {
                return;
            }

            var error = Marshal.GetLastWin32Error();
            if (error != ErrorIoPending)
            {
                throw new Win32Exception(error, $"Razer filter IOCTL 0x{controlCode:X8} failed.");
            }

            completion.WaitHandle.WaitOne();
            if (!NativeMethods.GetOverlappedResult(
                    _handle,
                    completion.Overlapped,
                    out _,
                    false))
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    $"Razer filter IOCTL 0x{controlCode:X8} failed.");
            }
        }
        finally
        {
            pin.Free();
        }
    }

    public void CancelPendingReads()
    {
        if (_disposed)
        {
            return;
        }

        if (!NativeMethods.CancelIoEx(_handle, IntPtr.Zero))
        {
            var error = Marshal.GetLastWin32Error();
            if (error != ErrorNotFound)
            {
                throw new Win32Exception(error, "Could not cancel Razer filter reads.");
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        CancelIoNoThrow();
        foreach (var read in _reads)
        {
            read.Dispose();
        }
        _handle.Dispose();
    }

    private ReadContext GetContext(int slot) =>
        (uint)slot < (uint)_reads.Length
            ? _reads[slot]
            : throw new ArgumentOutOfRangeException(nameof(slot));

    private void CancelIoNoThrow()
    {
        if (!_handle.IsInvalid && !_handle.IsClosed)
        {
            NativeMethods.CancelIoEx(_handle, IntPtr.Zero);
        }
    }

    private sealed class ReadContext : OverlappedCompletion
    {
        internal ReadContext()
        {
            Buffer = Marshal.AllocHGlobal(RazerFilterInputProtocol.InputFrameLength);
        }

        internal IntPtr Buffer { get; }

        internal void Prepare()
        {
            WaitHandle.Reset();
            ResetOverlapped();
        }

        public override void Dispose()
        {
            Marshal.FreeHGlobal(Buffer);
            base.Dispose();
        }
    }

    private class OverlappedCompletion : IDisposable
    {
        private readonly EventWaitHandle _event = new(false, EventResetMode.AutoReset);
        private bool _disposed;

        internal OverlappedCompletion()
        {
            Overlapped = Marshal.AllocHGlobal(Marshal.SizeOf<NativeOverlappedData>());
            ResetOverlapped();
        }

        internal EventWaitHandle WaitHandle => _event;

        internal IntPtr Overlapped { get; }

        protected void ResetOverlapped()
        {
            Marshal.StructureToPtr(
                new NativeOverlappedData
                {
                    EventHandle = _event.SafeWaitHandle.DangerousGetHandle(),
                },
                Overlapped,
                false);
        }

        public virtual void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
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
