using System.ComponentModel;
using System.Runtime.InteropServices;
using OpenSynapse.Core.Macros;

namespace OpenSynapse.Windows.Input;

public sealed class MacroInputHook : IDisposable
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
    private bool _mouseMoveTimerActive;
    private bool _hasPendingMouseMove;
    private int _pendingMouseMoveX;
    private int _pendingMouseMoveY;
    private bool _started;
    private volatile bool _disposed;

    public MacroInputHook(Func<MacroInputEvent, bool> handler)
    {
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        _keyboardProc = KeyboardHook;
        _mouseProc = MouseHook;
    }

    public bool CaptureMouseMoves
    {
        get => _captureMouseMoves;
        set => _captureMouseMoves = value;
    }

    public static bool SupportsPhysicalState(MacroInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return input.Kind switch
        {
            MacroInputKind.Keyboard => input.VirtualKey is > 0 and <= 0xFF,
            MacroInputKind.MouseButton => input.MouseButton is >= 1 and <= 5,
            _ => false,
        };
    }

    public static bool IsPhysicallyDown(MacroInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var virtualKey = input.Kind switch
        {
            MacroInputKind.Keyboard => input.VirtualKey,
            MacroInputKind.MouseButton => input.MouseButton switch
            {
                1 => 0x01, // VK_LBUTTON
                2 => 0x02, // VK_RBUTTON
                3 => 0x04, // VK_MBUTTON
                4 => 0x05, // VK_XBUTTON1
                5 => 0x06, // VK_XBUTTON2
                _ => 0,
            },
            _ => 0,
        };
        return virtualKey != 0 && (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
    }

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
            var razerDeviceButtons = new RazerDeviceButtonInput(HandleRazerDeviceButton);
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

            var razerPrivateKeys = new RazerPrivateKeyInput(HandleRazerDeviceButton);
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
            if ((data.Flags & LlkhfInjected) != 0 ||
                data.ExtraInfo == WindowsMacroInputSink.InjectionTag)
            {
                return CallNextHookEx(0, code, wParam, lParam);
            }

            var message = unchecked((uint)wParam);
            var isDown = message is WmKeyDown or WmSysKeyDown;
            if (!isDown && message is not (WmKeyUp or WmSysKeyUp))
            {
                return CallNextHookEx(0, code, wParam, lParam);
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
                return HandleMouseMove(code, wParam, lParam);
            }

            var data = Marshal.PtrToStructure<MsLlHookStruct>(lParam);
            if ((data.Flags & LlMhInjected) != 0 ||
                data.ExtraInfo == WindowsMacroInputSink.InjectionTag)
            {
                return CallNextHookEx(0, code, wParam, lParam);
            }

            if (message is WmMouseWheel or WmMouseHWheel)
            {
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
            if (button == 0)
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

    private void HandleRazerDeviceButton(MacroInput input, bool isDown)
    {
        // Raw Input cannot suppress the legacy key report, so the result is
        // intentionally ignored. The trigger itself is still delivered.
        _ = _handler(CreateEvent(input, isDown));
    }

    private nint HandleMouseMove(int code, nint wParam, nint lParam)
    {
        if (!_captureMouseMoves)
        {
            return CallNextHookEx(0, code, wParam, lParam);
        }

        var data = Marshal.PtrToStructure<MsLlHookStruct>(lParam);
        if ((data.Flags & LlMhInjected) != 0 ||
            data.ExtraInfo == WindowsMacroInputSink.InjectionTag)
        {
            return CallNextHookEx(0, code, wParam, lParam);
        }

        var now = Environment.TickCount64;
        if (now - _lastMouseMoveTimestamp < MouseMoveThrottleMilliseconds)
        {
            // Keep only the newest point and let the timer flush it once the throttle window ends.
            _pendingMouseMoveX = data.X;
            _pendingMouseMoveY = data.Y;
            _hasPendingMouseMove = true;
            if (!_mouseMoveTimerActive)
            {
                var remaining = (uint)Math.Max(1, _lastMouseMoveTimestamp + MouseMoveThrottleMilliseconds - now);
                _mouseMoveTimerActive = SetTimer(0, MouseMoveTimerId, remaining, 0) != 0;
            }
            return CallNextHookEx(0, code, wParam, lParam);
        }

        StopMouseMoveTimer();
        _hasPendingMouseMove = false;
        _lastMouseMoveTimestamp = now;
        _handler(CreateEvent(MouseMovePlaceholder, isDown: false, data.X, data.Y, isMouseMove: true));
        return CallNextHookEx(0, code, wParam, lParam);
    }

    private void FlushMouseMove()
    {
        StopMouseMoveTimer();
        if (!_hasPendingMouseMove || !_captureMouseMoves || _disposed)
        {
            _hasPendingMouseMove = false;
            return;
        }

        _hasPendingMouseMove = false;
        _lastMouseMoveTimestamp = Environment.TickCount64;
        try
        {
            _handler(CreateEvent(
                MouseMovePlaceholder,
                isDown: false,
                _pendingMouseMoveX,
                _pendingMouseMoveY,
                isMouseMove: true));
        }
        catch
        {
            // A handler failure must not stop the hook thread.
        }
    }

    private void StopMouseMoveTimer()
    {
        if (!_mouseMoveTimerActive)
        {
            return;
        }

        KillTimer(0, MouseMoveTimerId);
        _mouseMoveTimerActive = false;
    }

    private MacroInputEvent CreateEvent(
        MacroInput input,
        bool isDown,
        int x = 0,
        int y = 0,
        bool isMouseMove = false)
    {
        var windowLeft = 0;
        var windowTop = 0;
        var windowWidth = 0;
        var windowHeight = 0;
        var window = GetForegroundWindow();
        if (window != 0)
        {
            var origin = default(Point);
            if (GetClientRect(window, out var rect) &&
                ClientToScreen(window, ref origin))
            {
                windowLeft = origin.X;
                windowTop = origin.Y;
                windowWidth = rect.Right - rect.Left;
                windowHeight = rect.Bottom - rect.Top;
            }
        }

        return new MacroInputEvent(input, isDown, Environment.TickCount64)
        {
            IsMouseMove = isMouseMove,
            X = x,
            Y = y,
            WindowLeft = windowLeft,
            WindowTop = windowTop,
            WindowWidth = windowWidth,
            WindowHeight = windowHeight,
            Modifiers = CurrentModifiers,
        };
    }

    private MacroModifierKeys CurrentModifiers
    {
        get
        {
            var bits = _modifierBits;
            var modifiers = MacroModifierKeys.None;
            if ((bits & ShiftBits) != 0)
            {
                modifiers |= MacroModifierKeys.Shift;
            }
            if ((bits & ControlBits) != 0)
            {
                modifiers |= MacroModifierKeys.Control;
            }
            if ((bits & AltBits) != 0)
            {
                modifiers |= MacroModifierKeys.Alt;
            }
            if ((bits & WindowsBits) != 0)
            {
                modifiers |= MacroModifierKeys.Windows;
            }
            return modifiers;
        }
    }

    private void UpdateModifierState(int virtualKey, bool isDown)
    {
        // Legacy or remote input may report the generic modifier key; clear both sides to avoid a stuck flag.
        if (!isDown)
        {
            var group = virtualKey switch
            {
                VkShift => ShiftBits,
                VkControl => ControlBits,
                VkMenu => AltBits,
                _ => (byte)0,
            };
            if (group != 0)
            {
                _modifierBits &= (byte)~group;
                return;
            }
        }

        var bit = virtualKey switch
        {
            VkShift or VkLShift => LeftShiftBit,
            VkRShift => RightShiftBit,
            VkControl or VkLControl => LeftControlBit,
            VkRControl => RightControlBit,
            VkMenu or VkLMenu => LeftAltBit,
            VkRMenu => RightAltBit,
            VkLWin => LeftWindowsBit,
            VkRWin => RightWindowsBit,
            _ => (byte)0,
        };
        if (bit == 0)
        {
            return;
        }

        if (isDown)
        {
            _modifierBits |= bit;
        }
        else
        {
            _modifierBits &= (byte)~bit;
        }
    }

    private static byte ReadModifierBits()
    {
        byte bits = 0;
        if (IsKeyDown(VkLShift))
        {
            bits |= LeftShiftBit;
        }
        if (IsKeyDown(VkRShift))
        {
            bits |= RightShiftBit;
        }
        if (IsKeyDown(VkLControl))
        {
            bits |= LeftControlBit;
        }
        if (IsKeyDown(VkRControl))
        {
            bits |= RightControlBit;
        }
        if (IsKeyDown(VkLMenu))
        {
            bits |= LeftAltBit;
        }
        if (IsKeyDown(VkRMenu))
        {
            bits |= RightAltBit;
        }
        if (IsKeyDown(VkLWin))
        {
            bits |= LeftWindowsBit;
        }
        if (IsKeyDown(VkRWin))
        {
            bits |= RightWindowsBit;
        }
        return bits;
    }

    private static bool IsKeyDown(int virtualKey) => (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    private static int GetXButton(uint mouseData) =>
        unchecked((ushort)(mouseData >> 16)) switch
        {
            XButton1 => 4,
            XButton2 => 5,
            _ => 0,
        };

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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nuint SetTimer(nint window, nuint timerId, uint elapse, nint timerProc);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool KillTimer(nint window, nuint timerId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? moduleName);
}
