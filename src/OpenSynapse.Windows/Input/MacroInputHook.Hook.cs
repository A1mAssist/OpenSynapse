using System.ComponentModel;
using System.Runtime.InteropServices;
using OpenSynapse.Core.Macros;

namespace OpenSynapse.Windows.Input;

public sealed partial class MacroInputHook : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WhMouseLl = 14;
    private const uint WmKeyDown = 0x0100;
    private const uint WmKeyUp = 0x0101;
    private const uint WmSysKeyDown = 0x0104;
    private const uint WmSysKeyUp = 0x0105;
    private const uint WmMouseMove = 0x0200;
    private const uint WmMouseWheel = 0x020A;
    private const uint WmMouseHWheel = 0x020E;
    private const uint WmLButtonDown = 0x0201;
    private const uint WmLButtonUp = 0x0202;
    private const uint WmRButtonDown = 0x0204;
    private const uint WmRButtonUp = 0x0205;
    private const uint WmMButtonDown = 0x0207;
    private const uint WmMButtonUp = 0x0208;
    private const uint WmXButtonDown = 0x020B;
    private const uint WmXButtonUp = 0x020C;
    private const uint WmQuit = 0x0012;
    private const uint WmTimer = 0x0113;
    private const uint LlkhfExtended = 0x01;
    private const uint LlkhfInjected = 0x10;
    private const uint LlMhInjected = 0x01;
    private const ushort XButton1 = 0x0001;
    private const ushort XButton2 = 0x0002;

    private const int MouseMoveThrottleMilliseconds = 16;
    private const int ActivityThrottleMilliseconds = 2_000;
    private const nuint MouseMoveTimerId = 1;
    private const int HookStartTimeoutMilliseconds = 10_000;
    private const int HookStopTimeoutMilliseconds = 250;
    private const int ErrorTimeout = 1460;

    private const int VkShift = 0x10;
    private const int VkControl = 0x11;
    private const int VkMenu = 0x12;
    private const int VkLShift = 0xA0;
    private const int VkRShift = 0xA1;
    private const int VkLControl = 0xA2;
    private const int VkRControl = 0xA3;
    private const int VkLMenu = 0xA4;
    private const int VkRMenu = 0xA5;
    private const int VkLWin = 0x5B;
    private const int VkRWin = 0x5C;

    private const byte LeftShiftBit = 1 << 0;
    private const byte RightShiftBit = 1 << 1;
    private const byte LeftControlBit = 1 << 2;
    private const byte RightControlBit = 1 << 3;
    private const byte LeftAltBit = 1 << 4;
    private const byte RightAltBit = 1 << 5;
    private const byte LeftWindowsBit = 1 << 6;
    private const byte RightWindowsBit = 1 << 7;
    private const byte ShiftBits = LeftShiftBit | RightShiftBit;
    private const byte ControlBits = LeftControlBit | RightControlBit;
    private const byte AltBits = LeftAltBit | RightAltBit;
    private const byte WindowsBits = LeftWindowsBit | RightWindowsBit;

    private static readonly MacroInput MouseMovePlaceholder = new();

    private readonly Func<MacroInputEvent, bool> _handler;
    private readonly Action<string>? _log;
    private readonly LowLevelKeyboardProc _keyboardProc;
    private readonly LowLevelMouseProc _mouseProc;
    private readonly ManualResetEventSlim _ready = new(false);
    private readonly object _gate = new();
    private Thread? _thread;
    private uint _threadId;
    private nint _keyboardHook;
    private nint _mouseHook;
    private RazerDeviceButtonInput? _razerDeviceButtons;
    private RazerPrivateKeyInput? _razerPrivateKeys;
    private Exception? _startupError;
    private volatile bool _captureMouseMoves;
    private byte _modifierBits;
    private long _lastMouseMoveTimestamp;
    private long _lastActivityTimestamp;
    private bool _mouseMoveTimerActive;
    private bool _hasPendingMouseMove;
    private int _pendingMouseMoveX;
    private int _pendingMouseMoveY;
    private bool _started;
    private volatile bool _disposed;

    public MacroInputHook(Func<MacroInputEvent, bool> handler, Action<string>? log = null)
    {
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        _log = log;
        _keyboardProc = KeyboardHook;
        _mouseProc = MouseHook;
    }

    private void Trace(string message)
    {
        _log?.Invoke(message);
    }

    internal Func<bool>? IsPlaybackActive { get; set; }

    // Raised before the move is filtered for recording: any movement means the
    // device is awake again after idle sleep.
    internal Action? Activity { get; set; }

    private bool PlaybackActive() => IsPlaybackActive?.Invoke() == true;

    public void Start()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_started)
            {
                throw new InvalidOperationException("Macro input hook is already running.");
            }

            _ready.Reset();
            _startupError = null;
            var thread = new Thread(HookThreadMain)
            {
                IsBackground = true,
                Name = "OpenSynapse macro input hook",
            };
            thread.SetApartmentState(ApartmentState.STA);
            _thread = thread;
            thread.Start();

            if (!_ready.Wait(HookStartTimeoutMilliseconds))
            {
                PostThreadMessage(_threadId, WmQuit, 0, 0);
                thread.Join(TimeSpan.FromSeconds(1));
                _thread = null;
                throw new Win32Exception(ErrorTimeout, "The macro input hook thread did not start in time.");
            }

            if (_startupError is { } error)
            {
                _thread = null;
                thread.Join(TimeSpan.FromSeconds(1));
                if (error is Win32Exception win32)
                {
                    throw win32;
                }

                throw new Win32Exception(0, error.Message);
            }

            _started = true;
        }
    }

    public void Dispose()
    {
        Thread? thread;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _started = false;
            thread = _thread;
            _thread = null;
            if (thread is not null && _threadId != 0)
            {
                PostThreadMessage(_threadId, WmQuit, 0, 0);
            }
        }

        if (thread is null || thread == Thread.CurrentThread)
        {
            return;
        }

        if (!thread.Join(TimeSpan.FromMilliseconds(HookStopTimeoutMilliseconds)))
        {
            UnhookBoth();
        }
    }

    private void HookThreadMain()
    {
        _threadId = GetCurrentThreadId();
        nint keyboardHook = 0;
        nint mouseHook = 0;
        try
        {
            // Force the message queue into existence so Dispose can post WM_QUIT before the loop starts.
            PeekMessage(out _, 0, 0, 0, 0);
            _modifierBits = ReadModifierBits();
            var razerDeviceButtons = new RazerDeviceButtonInput(HandleRazerDeviceButton, _log);
            try
            {
                razerDeviceButtons.Start();
                _razerDeviceButtons = razerDeviceButtons;
            }
            catch
            {
                // Raw Input only adds device-scoped triggers. A failure here must
                // not disable the keyboard and mouse hooks that already work.
                razerDeviceButtons.Dispose();
            }

            var razerPrivateKeys = new RazerPrivateKeyInput(HandleRazerDeviceButton, _log);
            try
            {
                razerPrivateKeys.Start();
                _razerPrivateKeys = razerPrivateKeys;
            }
            catch
            {
                razerPrivateKeys.Dispose();
            }

            var module = GetModuleHandle(null);
            keyboardHook = SetWindowsHookEx(WhKeyboardLl, _keyboardProc, module, 0);
            if (keyboardHook == 0)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not install the keyboard macro hook.");
            }

            mouseHook = SetWindowsHookEx(WhMouseLl, _mouseProc, module, 0);
            if (mouseHook == 0)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not install the mouse macro hook.");
            }

            _keyboardHook = keyboardHook;
            _mouseHook = mouseHook;
            _ready.Set();

            while (GetMessage(out var message, 0, 0, 0) > 0)
            {
                if (message.Message == WmTimer && message.WParam == MouseMoveTimerId)
                {
                    FlushMouseMove();
                    continue;
                }

                // Raw Input arrives as WM_INPUT on the hidden window. Low-level
                // hooks fire inside GetMessage, but window messages still need
                // to be dispatched to reach the window procedure.
                _ = DispatchMessage(in message);
            }
        }
        catch (Exception exception)
        {
            _startupError = exception;
            _ready.Set();
        }
        finally
        {
            _razerDeviceButtons?.Dispose();
            _razerDeviceButtons = null;
            _razerPrivateKeys?.Dispose();
            _razerPrivateKeys = null;
            StopMouseMoveTimer();
            _hasPendingMouseMove = false;
            if (keyboardHook != 0)
            {
                UnhookWindowsHookEx(keyboardHook);
            }
            if (mouseHook != 0)
            {
                UnhookWindowsHookEx(mouseHook);
            }

            _keyboardHook = 0;
            _mouseHook = 0;
        }
    }

    private void UnhookBoth()
    {
        var keyboardHook = Interlocked.Exchange(ref _keyboardHook, 0);
        if (keyboardHook != 0)
        {
            UnhookWindowsHookEx(keyboardHook);
        }

        var mouseHook = Interlocked.Exchange(ref _mouseHook, 0);
        if (mouseHook != 0)
        {
            UnhookWindowsHookEx(mouseHook);
        }
    }

    private nint KeyboardHook(int code, nint wParam, nint lParam)
    {
        if (code < 0 || _disposed)
        {
            return CallNextHookEx(0, code, wParam, lParam);
        }

        try
        {
            var data = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);
            var injected = (data.Flags & LlkhfInjected) != 0;
            var ours = data.ExtraInfo == WindowsMacroInputSink.InjectionTag;
            var playing = PlaybackActive();
            // The driver re-injects the keys it rewrites; dropping every injected
            // key hides those. Only a playback can be our own untagged output.
            if (ours || (injected && playing))
            {
                if (MacroInputTrace.Enabled)
                {
                    Trace(
                        $"hook keyboard ignored injected vk=0x{data.VirtualKey:X2} " +
                        $"scan=0x{data.ScanCode:X2} flags=0x{data.Flags:X} " +
                        $"extra=0x{data.ExtraInfo:X} msgExtra=0x{GetMessageExtraInfo():X} " +
                        $"ours={ours} playing={playing}");
                }
                return CallNextHookEx(0, code, wParam, lParam);
            }

            var message = unchecked((uint)wParam);
            var isDown = message is WmKeyDown or WmSysKeyDown;
            if (!isDown && message is not (WmKeyUp or WmSysKeyUp))
            {
                return CallNextHookEx(0, code, wParam, lParam);
            }

            if (MacroInputTrace.Enabled)
            {
                Trace(
                    $"hook keyboard vk=0x{data.VirtualKey:X2} scan=0x{data.ScanCode:X2} " +
                    $"down={isDown} flags=0x{data.Flags:X}");
            }
            var virtualKey = checked((int)data.VirtualKey);
            var input = new MacroInput
            {
                Kind = MacroInputKind.Keyboard,
                VirtualKey = virtualKey,
                ScanCode = checked((int)data.ScanCode),
                Extended = (data.Flags & LlkhfExtended) != 0,
            };

            // The event carries the modifier state from before its own key is applied.
            var value = CreateEvent(input, isDown);
            UpdateModifierState(virtualKey, isDown);
            return _handler(value) ? 1 : CallNextHookEx(0, code, wParam, lParam);
        }
        catch
        {
            return CallNextHookEx(0, code, wParam, lParam);
        }
    }

    private nint MouseHook(int code, nint wParam, nint lParam)
    {
        if (code < 0 || _disposed)
        {
            return CallNextHookEx(0, code, wParam, lParam);
        }

        try
        {
            var message = unchecked((uint)wParam);
            if (message == WmMouseMove)
            {
                var now = Environment.TickCount64;
                if (now - _lastActivityTimestamp >= ActivityThrottleMilliseconds)
                {
                    _lastActivityTimestamp = now;
                    Activity?.Invoke();
                }
                return HandleMouseMove(code, wParam, lParam);
            }

            var data = Marshal.PtrToStructure<MsLlHookStruct>(lParam);
            var playing = PlaybackActive();
            if (data.ExtraInfo == WindowsMacroInputSink.InjectionTag)
            {
                if (MacroInputTrace.Enabled)
                {
                    Trace(
                        $"hook mouse ignored own injection flags=0x{data.Flags:X} " +
                        $"extra=0x{data.ExtraInfo:X} msgExtra=0x{GetMessageExtraInfo():X} " +
                        $"data=0x{data.MouseData:X}");
                }
                return CallNextHookEx(0, code, wParam, lParam);
            }

            if (message is WmMouseWheel or WmMouseHWheel)
            {
                if ((data.Flags & LlMhInjected) != 0 && playing)
                {
                    if (MacroInputTrace.Enabled)
                    {
                        Trace(
                            $"hook wheel ignored injected delta={unchecked((short)(data.MouseData >> 16))} " +
                            $"flags=0x{data.Flags:X} extra=0x{data.ExtraInfo:X} " +
                            $"msgExtra=0x{GetMessageExtraInfo():X} playing={playing}");
                    }
                    return CallNextHookEx(0, code, wParam, lParam);
                }
                var delta = unchecked((short)(data.MouseData >> 16));
                if (delta == 0)
                {
                    return CallNextHookEx(0, code, wParam, lParam);
                }

                var wheelInput = new MacroInput
                {
                    Kind = MacroInputKind.MouseWheel,
                    WheelDelta = delta,
                    WheelHorizontal = message == WmMouseHWheel,
                };
                var wheelHandled = _handler(CreateEvent(wheelInput, isDown: false, data.X, data.Y));
                return wheelHandled ? 1 : CallNextHookEx(0, code, wParam, lParam);
            }

            var (isDown, button) = message switch
            {
                WmLButtonDown => (true, 1),
                WmLButtonUp => (false, 1),
                WmRButtonDown => (true, 2),
                WmRButtonUp => (false, 2),
                WmMButtonDown => (true, 3),
                WmMButtonUp => (false, 3),
                WmXButtonDown => (true, GetXButton(data.MouseData)),
                WmXButtonUp => (false, GetXButton(data.MouseData)),
                _ => (false, 0),
            };
            var accepted = button != 0 &&
                MacroMouseInputPolicy.AcceptsMouseButton(
                    data.Flags, data.ExtraInfo, button, playing);
            if (MacroInputTrace.Enabled)
            {
                Trace(
                    $"hook mouse button={button} down={isDown} flags=0x{data.Flags:X} " +
                    $"extra=0x{data.ExtraInfo:X} msgExtra=0x{GetMessageExtraInfo():X} " +
                    $"playing={playing} accepted={accepted}");
            }
            if (!accepted)
            {
                return CallNextHookEx(0, code, wParam, lParam);
            }

            var input = new MacroInput
            {
                Kind = MacroInputKind.MouseButton,
                MouseButton = button,
            };
            var handled = _handler(CreateEvent(input, isDown, data.X, data.Y));
            return handled ? 1 : CallNextHookEx(0, code, wParam, lParam);
        }
        catch
        {
            return CallNextHookEx(0, code, wParam, lParam);
        }
    }

    private delegate nint LowLevelKeyboardProc(int code, nint wParam, nint lParam);
    private delegate nint LowLevelMouseProc(int code, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KbdLlHookStruct
    {
        internal uint VirtualKey;
        internal uint ScanCode;
        internal uint Flags;
        internal uint Time;
        internal nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct MsLlHookStruct
    {
        internal readonly int X;
        internal readonly int Y;
        internal readonly uint MouseData;
        internal readonly uint Flags;
        internal readonly uint Time;
        internal readonly nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        internal int X;
        internal int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        internal nint Window;
        internal uint Message;
        internal nuint WParam;
        internal nint LParam;
        internal uint Time;
        internal int X;
        internal int Y;
        internal uint Private;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookEx(
        int hookId,
        Delegate callback,
        nint module,
        uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(nint hook);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(
        nint hook,
        int code,
        nint wParam,
        nint lParam);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out Msg message, nint window, uint filterMin, uint filterMax);

    [DllImport("user32.dll")]
    private static extern nint DispatchMessage(in Msg message);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekMessage(
        out Msg message,
        nint window,
        uint filterMin,
        uint filterMax,
        uint removeMessage);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessage(uint threadId, uint message, nuint wParam, nint lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, out Rect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(nint window, out Rect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClientToScreen(nint window, ref Point point);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll")]
    private static extern nint GetMessageExtraInfo();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nuint SetTimer(nint window, nuint timerId, uint elapse, nint timerProc);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool KillTimer(nint window, nuint timerId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? moduleName);
}
