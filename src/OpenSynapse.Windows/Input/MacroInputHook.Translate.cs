using System.Runtime.InteropServices;
using OpenSynapse.Core.Macros;

namespace OpenSynapse.Windows.Input;

public sealed partial class MacroInputHook
{
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

    private void HandleRazerDeviceButton(MacroInput input, bool isDown)
    {
        if (MacroInputTrace.Enabled)
        {
            Trace($"hook device input={MacroInputFormatter.Describe(input)} down={isDown}");
        }
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
}
