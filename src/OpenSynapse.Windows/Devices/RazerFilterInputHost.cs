using System.ComponentModel;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using Microsoft.Win32.SafeHandles;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

/// <summary>
/// Input reader for the installed Razer filter driver. Hooks belong to the
/// selected device endpoint and only suppress the inputs the caller registers.
/// </summary>
public sealed class RazerFilterInputHost : IAsyncDisposable
{
    internal const uint EnableInputHooks = 0x88883034;
    internal const uint EnableInputNotify = 0x88883038;

    private readonly IRazerFilterDriverChannel _driver;
    private readonly Action<BladeMappingInputEvent> _eventHandler;
    private readonly (ushort ScanCode, ushort Flag)[] _hooks;
    private readonly uint[] _mouseHooks;
    private readonly CancellationTokenSource _stop = new();
    private readonly object _disposeSync = new();
    private readonly Channel<BladeMappingInputEvent> _events =
        Channel.CreateUnbounded<BladeMappingInputEvent>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true,
        });
    private readonly object _lifecycleGate = new();
    private readonly List<(ushort ScanCode, ushort Flag)> _installedHooks = [];
    private readonly List<uint> _installedMouseHooks = [];
    private readonly HashSet<(ushort ScanCode, bool Extended)> _submittedKeyboardKeys = [];
    private readonly Task<ReadOnlyMemory<byte>>?[] _pendingReads = new Task<ReadOnlyMemory<byte>>?[2];
    private Task _reader = Task.CompletedTask;
    private Task? _completion;
    private Task? _disposeTask;
    private bool _hooksEnabled;
    private bool _notifyEnabled;
    private bool _accepting;
    private ushort _activeConsumerUsage;
    private bool _stopped;
    private bool _started;
    private bool _driverDisposed;
    private bool _disposed;

    public RazerFilterInputHost(
        string devicePath,
        IReadOnlyList<(ushort ScanCode, ushort Flag)> hooks,
        Action<BladeMappingInputEvent> eventHandler,
        IReadOnlyList<uint>? mouseHooks = null)
        : this(new WindowsRazerFilterDriverChannel(devicePath), hooks, eventHandler, mouseHooks)
    {
    }

    internal RazerFilterInputHost(
        IRazerFilterDriverChannel driver,
        IReadOnlyList<(ushort ScanCode, ushort Flag)> hooks,
        Action<BladeMappingInputEvent> eventHandler,
        IReadOnlyList<uint>? mouseHooks = null)
    {
        _driver = driver ?? throw new ArgumentNullException(nameof(driver));
        ArgumentNullException.ThrowIfNull(hooks);
        _hooks = hooks.Count > 0
            ? [.. hooks]
            : throw new ArgumentException("At least one input hook is required.", nameof(hooks));
        _eventHandler = eventHandler ?? throw new ArgumentNullException(nameof(eventHandler));
        _mouseHooks = mouseHooks is null ? [] : [.. mouseHooks.Distinct()];
        foreach (var buttons in _mouseHooks)
        {
            _ = RazerFilterInputProtocol.CreateMouseHook(buttons);
        }
    }

    public Task Completion => _completion ?? Task.CompletedTask;

    public void SendConsumerUsage(ushort usage)
    {
        lock (_lifecycleGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_started || _stopped)
            {
                throw new InvalidOperationException("Razer filter input host is not running.");
            }

            var previous = _activeConsumerUsage;
            if (usage != 0)
            {
                _activeConsumerUsage = usage;
            }
            try
            {
                _driver.WriteControl(
                    RazerFilterInputProtocol.SubmitInput,
                    RazerFilterInputProtocol.CreateConsumerInput(usage));
                _activeConsumerUsage = usage;
            }
            catch
            {
                if (usage == 0)
                {
                    _activeConsumerUsage = previous;
                }
                throw;
            }
        }
    }

    public void SendKeyboardInput(BladeMappingInputEvent input)
    {
        if (input.Kind != BladeMappingInputKind.Keyboard ||
            input.Code is < 0 or > ushort.MaxValue)
        {
            throw new ArgumentException("Only a valid keyboard input can be submitted.", nameof(input));
        }

        lock (_lifecycleGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_started || _stopped)
            {
                throw new InvalidOperationException("Razer filter input host is not running.");
            }

            var key = (checked((ushort)input.Code), input.Extended);
            _driver.WriteControl(
                RazerFilterInputProtocol.SubmitInput,
                RazerFilterInputProtocol.CreateKeyboardInput(
                    key.Item1,
                    input.IsDown,
                    key.Extended));
            if (input.IsDown)
            {
                _submittedKeyboardKeys.Add(key);
            }
            else
            {
                _submittedKeyboardKeys.Remove(key);
            }
        }
    }

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
        try
        {
            for (var slot = 0; slot < _pendingReads.Length; slot++)
            {
                _driver.PostRead(slot);
                _pendingReads[slot] = _driver.CompleteReadAsync(slot);
            }
            _reader = ReadLoopAsync();
            EnsureCompletion();

            lock (_lifecycleGate)
            {
                // A failed reader must not clear hooks while startup installs more.
                _stop.Token.ThrowIfCancellationRequested();
                WriteEnabled(EnableInputHooks, true);
                _hooksEnabled = true;
                WriteEnabled(EnableInputNotify, true);
                _notifyEnabled = true;

                foreach (var hook in _hooks)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    _driver.WriteControl(
                        RazerFilterInputProtocol.SetInputHook,
                        RazerFilterInputProtocol.CreateKeyboardHook(hook.ScanCode, hook.Flag));
                    _installedHooks.Add(hook);
                }
                foreach (var buttons in _mouseHooks)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    _driver.WriteControl(
                        RazerFilterInputProtocol.SetInputHook,
                        RazerFilterInputProtocol.CreateMouseHook(buttons));
                    _installedMouseHooks.Add(buttons);
                }
            }
        }
        catch (Exception startupError)
        {
            var cleanupError = StopDriver();
            EnsureCompletion();
            var completionError = await ObserveCompletionAsync().ConfigureAwait(false);
            cleanupError ??= completionError;
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

    private async Task ReadLoopAsync()
    {
        try
        {
            for (var slot = 0; ; slot = (slot + 1) % _pendingReads.Length)
            {
                var frame = await _pendingReads[slot]!.ConfigureAwait(false);
                lock (_lifecycleGate)
                {
                    _stop.Token.ThrowIfCancellationRequested();

                    // Re-arm before delivery, but consume in posting order to preserve key edges.
                    _driver.PostRead(slot);
                    _pendingReads[slot] = _driver.CompleteReadAsync(slot);
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

    private async Task SuperviseAsync(Task reader, Task consumer)
    {
        await Task.WhenAny(reader, consumer).ConfigureAwait(false);
        if (!_stop.IsCancellationRequested)
        {
            StopDriver();
        }

        Exception? error = null;
        try
        {
            await reader.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            error = exception;
        }

        // Cancellation must finish for both native buffers before the driver can be disposed.
        foreach (var pendingRead in _pendingReads)
        {
            if (pendingRead is null) continue;
            try { await pendingRead.ConfigureAwait(false); }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
            catch (Exception exception) { error ??= exception; }
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
            foreach (var key in _submittedKeyboardKeys)
            {
                TryControl(
                    () => _driver.WriteControl(
                        RazerFilterInputProtocol.SubmitInput,
                        RazerFilterInputProtocol.CreateKeyboardInput(
                            key.Item1,
                            isDown: false,
                            key.Item2)),
                    errors);
            }
            _submittedKeyboardKeys.Clear();
            if (_activeConsumerUsage != 0)
            {
                TryControl(
                    () => _driver.WriteControl(
                        RazerFilterInputProtocol.SubmitInput,
                        RazerFilterInputProtocol.CreateConsumerInput(0)),
                    errors);
                _activeConsumerUsage = 0;
            }
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
            for (var index = _installedMouseHooks.Count - 1; index >= 0; index--)
            {
                var buttons = _installedMouseHooks[index];
                TryControl(
                    () => _driver.WriteControl(
                        RazerFilterInputProtocol.ClearInputHook,
                        RazerFilterInputProtocol.CreateMouseClearKey(buttons)),
                    errors);
            }
            _installedMouseHooks.Clear();

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

    private void EnsureCompletion()
    {
        if (_completion is not null)
        {
            return;
        }

        _completion = SuperviseAsync(_reader, ConsumeAsync());
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
    private readonly bool _legacyKeypad;
    private readonly int _frameLength;
    private bool _disposed;

    internal WindowsRazerFilterDriverChannel(string devicePath, bool legacyKeypad = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(devicePath);
        _legacyKeypad = legacyKeypad;
        _frameLength = legacyKeypad ? 64 : RazerFilterInputProtocol.InputFrameLength;
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

        _reads = Enumerable.Range(0, legacyKeypad ? RazerLegacyKeypadInputHost.ReadSlots : 2)
            .Select(_ => new ReadContext(_frameLength)).ToArray();
    }

    public void PostRead(int slot)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var context = GetContext(slot);
        context.Prepare();
        if (!NativeMethods.DeviceIoControl(
                _handle,
                _legacyKeypad ? 0x88883008 : RazerFilterInputProtocol.ReadInput,
                _legacyKeypad ? context.Buffer : IntPtr.Zero,
                _legacyKeypad ? _frameLength : 0,
                context.Buffer,
                _frameLength,
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

        if (length > _frameLength) throw new InvalidDataException("The Razer input read exceeded its buffer.");
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

            RazerFilterControlCompletion.Complete(
                completion.WaitHandle,
                () => NativeMethods.GetOverlappedResult(_handle, completion.Overlapped, out _, false),
                () => _ = NativeMethods.CancelIoEx(_handle, completion.Overlapped),
                controlCode);
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
        private readonly byte[] _empty;
        internal ReadContext(int length)
        {
            _empty = new byte[length];
            Buffer = Marshal.AllocHGlobal(length);
        }

        internal IntPtr Buffer { get; }

        internal void Prepare()
        {
            Marshal.Copy(_empty, 0, Buffer, _empty.Length);
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
