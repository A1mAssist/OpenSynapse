using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Channels;

namespace OpenSynapse.Windows.Lighting;

internal sealed class WindowsKeyboardLightingAdapter : ILightingInputAdapter
{
    internal const uint InjectedFlag = 0x10;
    internal const uint KeyUpFlag = 0x80;
    private const int WhKeyboardLowLevel = 13;
    private const uint WmQuit = 0x0012;
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(20);
    private static readonly IReadOnlyDictionary<uint, (int Row, int Column)> ScanCodeMap =
        CreateScanCodeMap();

    private readonly Channel<QuickLightingKeyEvent> _events = Channel.CreateBounded<QuickLightingKeyEvent>(
        new BoundedChannelOptions(64)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.DropOldest,
        });
    private readonly Dictionary<uint, TimeSpan> _lastByKey = [];
    private readonly HookProc _callback;
    private readonly Stopwatch _clock = new();
    private TaskCompletionSource _started = NewCompletion();
    private TaskCompletionSource _stopped = NewCompletion();
    private Thread? _thread;
    private nint _hook;
    private uint _threadId;
    private int _stopRequested;
    private int _disposed;

    internal WindowsKeyboardLightingAdapter() => _callback = HookCallback;

    public async ValueTask StartAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (_thread is null)
        {
            _thread = new Thread(RunMessageLoop)
            {
                IsBackground = true,
                Name = "OpenSynapse keyboard lighting input",
            };
            _thread.Start();
        }

        await _started.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask StopAsync()
    {
        if (Interlocked.Exchange(ref _stopRequested, 1) == 0)
        {
            var threadId = Volatile.Read(ref _threadId);
            if (threadId != 0)
            {
                _ = PostThreadMessage(threadId, WmQuit, 0, 0);
            }
        }

        if (_thread is not null)
        {
            await _stopped.Task.ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await StopAsync().ConfigureAwait(false);
    }

    internal void DrainTo(ICollection<QuickLightingKeyEvent> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        while (_events.Reader.TryRead(out var item))
        {
            destination.Add(item);
        }
    }

    internal bool TryTranslate(
        uint scanCode,
        uint flags,
        TimeSpan at,
        out QuickLightingKeyEvent keyEvent)
    {
        keyEvent = default;
        if ((flags & (InjectedFlag | KeyUpFlag)) != 0 ||
            !ScanCodeMap.TryGetValue(scanCode, out var position) ||
            !BladeLightingLayout.TryGetDevicePosition(
                position.Row, position.Column, out _, out _))
        {
            return false;
        }

        if (_lastByKey.TryGetValue(scanCode, out var previous) && at - previous < Debounce)
        {
            return false;
        }

        _lastByKey[scanCode] = at;
        keyEvent = new QuickLightingKeyEvent(position.Row, position.Column, at);
        return true;
    }

    private void RunMessageLoop()
    {
        try
        {
            _threadId = GetCurrentThreadId();
            _clock.Restart();
            _hook = SetWindowsHookEx(WhKeyboardLowLevel, _callback, GetModuleHandle(null), 0);
            if (_hook == 0)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            _started.TrySetResult();
            while (GetMessage(out var message, 0, 0, 0) > 0)
            {
                _ = TranslateMessage(in message);
                _ = DispatchMessage(in message);
            }
        }
        catch (Exception exception)
        {
            _started.TrySetException(exception);
        }
        finally
        {
            if (_hook != 0)
            {
                _ = UnhookWindowsHookEx(_hook);
                _hook = 0;
            }
            _events.Writer.TryComplete();
            _stopped.TrySetResult();
        }
    }

    private nint HookCallback(int code, nuint wParam, nint lParam)
    {
        if (code >= 0)
        {
            var data = Marshal.PtrToStructure<KeyboardHookData>(lParam);
            if (TryTranslate(data.ScanCode, data.Flags, _clock.Elapsed, out var keyEvent))
            {
                _events.Writer.TryWrite(keyEvent);
            }
        }

        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    private static IReadOnlyDictionary<uint, (int Row, int Column)> CreateScanCodeMap()
    {
        var map = new Dictionary<uint, (int, int)>
        {
            [0x01] = (0, 0),
            [0x29] = (1, 0), [0x02] = (1, 1), [0x03] = (1, 2), [0x04] = (1, 3),
            [0x05] = (1, 4), [0x06] = (1, 5), [0x07] = (1, 6), [0x08] = (1, 7),
            [0x09] = (1, 8), [0x0A] = (1, 9), [0x0B] = (1, 10), [0x0C] = (1, 11),
            [0x0D] = (1, 12), [0x0E] = (1, 14),
            [0x0F] = (2, 0), [0x10] = (2, 1), [0x11] = (2, 2), [0x12] = (2, 3),
            [0x13] = (2, 4), [0x14] = (2, 5), [0x15] = (2, 6), [0x16] = (2, 7),
            [0x17] = (2, 8), [0x18] = (2, 9), [0x19] = (2, 10), [0x1A] = (2, 11),
            [0x1B] = (2, 12), [0x2B] = (2, 13),
            [0x3A] = (3, 0), [0x1E] = (3, 1), [0x1F] = (3, 2), [0x20] = (3, 3),
            [0x21] = (3, 4), [0x22] = (3, 5), [0x23] = (3, 6), [0x24] = (3, 7),
            [0x25] = (3, 8), [0x26] = (3, 9), [0x27] = (3, 10), [0x28] = (3, 11),
            [0x1C] = (3, 14),
            [0x2A] = (4, 0), [0x2C] = (4, 1), [0x2D] = (4, 2), [0x2E] = (4, 3),
            [0x2F] = (4, 4), [0x30] = (4, 5), [0x31] = (4, 6), [0x32] = (4, 7),
            [0x33] = (4, 8), [0x34] = (4, 9), [0x35] = (4, 10), [0x36] = (4, 14),
            [0x1D] = (5, 0), [0x38] = (5, 2), [0x39] = (5, 6),
        };
        for (uint scanCode = 0x3B; scanCode <= 0x44; scanCode++)
        {
            map[scanCode] = (0, checked((int)(scanCode - 0x3B + 2)));
        }
        return map;
    }

    private static TaskCompletionSource NewCompletion() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private delegate nint HookProc(int code, nuint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct KeyboardHookData
    {
        public readonly uint VirtualKey;
        public readonly uint ScanCode;
        public readonly uint Flags;
        public readonly uint Time;
        public readonly nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativeMessage
    {
        public readonly nint Window;
        public readonly uint Message;
        public readonly nuint WParam;
        public readonly nint LParam;
        public readonly uint Time;
        public readonly int X;
        public readonly int Y;
        public readonly uint Private;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookEx(int hookId, HookProc callback, nint module, uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(nint hook);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hook, int code, nuint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetMessage(out NativeMessage message, nint window, uint minimum, uint maximum);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage(in NativeMessage message);

    [DllImport("user32.dll")]
    private static extern nint DispatchMessage(in NativeMessage message);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessage(uint threadId, uint message, nuint wParam, nint lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? moduleName);
}
