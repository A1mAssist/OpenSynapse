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
    private const uint LlkhfExtended = 0x01;
    private const uint LlkhfInjected = 0x10;
    private const uint LlMhInjected = 0x01;
    private const ushort XButton1 = 0x0001;
    private const ushort XButton2 = 0x0002;

    private readonly Func<MacroInputEvent, bool> _handler;
    private readonly LowLevelKeyboardProc _keyboardProc;
    private readonly LowLevelMouseProc _mouseProc;
    private nint _keyboardHook;
    private nint _mouseHook;
    private bool _started;
    private bool _disposed;

    public MacroInputHook(Func<MacroInputEvent, bool> handler)
    {
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        _keyboardProc = KeyboardHook;
        _mouseProc = MouseHook;
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started)
        {
            throw new InvalidOperationException("Macro input hook is already running.");
        }

        var module = GetModuleHandle(null);
        _keyboardHook = SetWindowsHookEx(WhKeyboardLl, _keyboardProc, module, 0);
        if (_keyboardHook == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not install the keyboard macro hook.");
        }

        try
        {
            _mouseHook = SetWindowsHookEx(WhMouseLl, _mouseProc, module, 0);
            if (_mouseHook == 0)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not install the mouse macro hook.");
            }
        }
        catch
        {
            UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = 0;
            throw;
        }

        _started = true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_keyboardHook != 0)
        {
            UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = 0;
        }
        if (_mouseHook != 0)
        {
            UnhookWindowsHookEx(_mouseHook);
            _mouseHook = 0;
        }
        _started = false;
    }

    private nint KeyboardHook(int code, nint wParam, nint lParam)
    {
        if (code < 0)
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

            var input = new MacroInput
            {
                Kind = MacroInputKind.Keyboard,
                VirtualKey = checked((int)data.VirtualKey),
                ScanCode = checked((int)data.ScanCode),
                Extended = (data.Flags & LlkhfExtended) != 0,
            };
            var handled = _handler(new MacroInputEvent(
                input,
                isDown,
                Environment.TickCount64));
            return handled ? 1 : CallNextHookEx(0, code, wParam, lParam);
        }
        catch
        {
            return CallNextHookEx(0, code, wParam, lParam);
        }
    }

    private nint MouseHook(int code, nint wParam, nint lParam)
    {
        if (code < 0)
        {
            return CallNextHookEx(0, code, wParam, lParam);
        }

        try
        {
            var message = unchecked((uint)wParam);
            if (message == WmMouseMove)
            {
                return CallNextHookEx(0, code, wParam, lParam);
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
                var wheelHandled = _handler(new MacroInputEvent(
                    wheelInput,
                    false,
                    Environment.TickCount64));
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
            var handled = _handler(new MacroInputEvent(
                input,
                isDown,
                Environment.TickCount64));
            return handled ? 1 : CallNextHookEx(0, code, wParam, lParam);
        }
        catch
        {
            return CallNextHookEx(0, code, wParam, lParam);
        }
    }

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

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? moduleName);
}
