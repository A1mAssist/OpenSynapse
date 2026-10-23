using System.ComponentModel;
using System.Runtime.InteropServices;
using OpenSynapse.Core.Macros;

namespace OpenSynapse.Windows.Input;

public sealed class WindowsMacroInputSink
{
    internal static readonly nuint InjectionTag = unchecked((nuint)0x4F534D4143524FUL);

    private const uint InputKeyboard = 1;
    private const uint InputMouse = 0;
    private const uint KeyEventExtended = 0x0001;
    private const uint KeyEventKeyUp = 0x0002;
    private const uint KeyEventUnicode = 0x0004;
    private const uint KeyEventScanCode = 0x0008;
    private const uint MouseEventMove = 0x0001;
    private const uint MouseEventLeftDown = 0x0002;
    private const uint MouseEventLeftUp = 0x0004;
    private const uint MouseEventRightDown = 0x0008;
    private const uint MouseEventRightUp = 0x0010;
    private const uint MouseEventMiddleDown = 0x0020;
    private const uint MouseEventMiddleUp = 0x0040;
    private const uint MouseEventXDown = 0x0080;
    private const uint MouseEventXUp = 0x0100;
    private const uint MouseEventWheel = 0x0800;
    private const uint MouseEventHWheel = 0x1000;
    private const uint MouseEventVirtualDesk = 0x4000;
    private const uint MouseEventAbsolute = 0x8000;

    private const int SmXVirtualScreen = 76;
    private const int SmYVirtualScreen = 77;
    private const int SmCxVirtualScreen = 78;
    private const int SmCyVirtualScreen = 79;

    private const int TextChunkLength = 256;

    private const int VkLShift = 0xA0;
    private const int VkRShift = 0xA1;
    private const int VkLControl = 0xA2;
    private const int VkRControl = 0xA3;
    private const int VkLMenu = 0xA4;
    private const int VkRMenu = 0xA5;
    private const int VkLWin = 0x5B;
    private const int VkRWin = 0x5C;

    private readonly object _gate = new();

    public void Send(MacroStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        if (step.Kind == MacroStepKind.Delay)
        {
            return;
        }

        var input = CreateInput(step);
        lock (_gate)
        {
            var sent = SendInput(1, [input], Marshal.SizeOf<Input>());
            if (sent == 1)
            {
                return;
            }

            throw new Win32Exception(Marshal.GetLastWin32Error(), "SendInput could not send a macro event.");
        }
    }

    public void SendText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0)
        {
            return;
        }

        lock (_gate)
        {
            for (var offset = 0; offset < text.Length; offset += TextChunkLength)
            {
                var length = Math.Min(TextChunkLength, text.Length - offset);
                var inputs = new Input[length * 2];
                for (var index = 0; index < length; index++)
                {
                    var codeUnit = text[offset + index];
                    inputs[index * 2] = CreateUnicodeInput(codeUnit, isDown: true);
                    inputs[index * 2 + 1] = CreateUnicodeInput(codeUnit, isDown: false);
                }

                SendAll(inputs);
            }
        }
    }

    public void SendMouseMove(MacroMouseMoveMode mode, int x, int y)
    {
        lock (_gate)
        {
            var (flags, moveX, moveY) = mode switch
            {
                MacroMouseMoveMode.Absolute => (
                    MouseEventMove | MouseEventAbsolute | MouseEventVirtualDesk,
                    NormalizeScreenX(x),
                    NormalizeScreenY(y)),
                MacroMouseMoveMode.Relative => (MouseEventMove, x, y),
                MacroMouseMoveMode.ForegroundRelative => ResolveForegroundMove(x, y),
                _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported mouse move mode."),
            };

            var input = new Input
            {
                Type = InputMouse,
                Data = new InputUnion
                {
                    Mouse = new MouseInput
                    {
                        X = moveX,
                        Y = moveY,
                        Flags = flags,
                        ExtraInfo = InjectionTag,
                    },
                },
            };
            if (SendInput(1, [input], Marshal.SizeOf<Input>()) != 1)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "SendInput could not move the mouse.");
            }
        }
    }

    public static MacroModifierKeys GetModifierState()
    {
        var modifiers = MacroModifierKeys.None;
        if (IsKeyDown(VkLShift) || IsKeyDown(VkRShift))
        {
            modifiers |= MacroModifierKeys.Shift;
        }
        if (IsKeyDown(VkLControl) || IsKeyDown(VkRControl))
        {
            modifiers |= MacroModifierKeys.Control;
        }
        if (IsKeyDown(VkLMenu) || IsKeyDown(VkRMenu))
        {
            modifiers |= MacroModifierKeys.Alt;
        }
        if (IsKeyDown(VkLWin) || IsKeyDown(VkRWin))
        {
            modifiers |= MacroModifierKeys.Windows;
        }
        return modifiers;
    }

    public static IReadOnlyList<MacroInput> GetPressedModifierInputs()
    {
        var inputs = new List<MacroInput>(4);
        AddIfPressed(inputs, VkLShift, extended: false);
        AddIfPressed(inputs, VkRShift, extended: false);
        AddIfPressed(inputs, VkLControl, extended: false);
        AddIfPressed(inputs, VkRControl, extended: true);
        AddIfPressed(inputs, VkLMenu, extended: false);
        AddIfPressed(inputs, VkRMenu, extended: true);
        AddIfPressed(inputs, VkLWin, extended: true);
        AddIfPressed(inputs, VkRWin, extended: true);
        return inputs;
    }

    private static void AddIfPressed(
        List<MacroInput> inputs,
        int virtualKey,
        bool extended)
    {
        if (IsKeyDown(virtualKey))
        {
            inputs.Add(new MacroInput
            {
                Kind = MacroInputKind.Keyboard,
                VirtualKey = virtualKey,
                Extended = extended,
            });
        }
    }

    private static Input CreateInput(MacroStep step)
    {
        var input = step.Input ?? throw new InvalidOperationException("Macro input is missing.");
        if (input.Kind == MacroInputKind.MouseWheel)
        {
            return CreateMouseWheelInput(input);
        }
        if (input.Kind == MacroInputKind.MouseButton)
        {
            return CreateMouseInput(input, step.IsDown);
        }

        var flags = (input.Extended ? KeyEventExtended : 0u) |
            (step.IsDown ? 0u : KeyEventKeyUp);
        ushort virtualKey = 0;
        ushort scanCode = 0;
        if (input.ScanCode > 0)
        {
            flags |= KeyEventScanCode;
            scanCode = checked((ushort)input.ScanCode);
        }
        else
        {
            virtualKey = checked((ushort)input.VirtualKey);
        }

        return new Input
        {
            Type = InputKeyboard,
            Data = new InputUnion
            {
                Keyboard = new KeyboardInput
                {
                    VirtualKey = virtualKey,
                    ScanCode = scanCode,
                    Flags = flags,
                    ExtraInfo = InjectionTag,
                },
            },
        };
    }

    private static Input CreateUnicodeInput(char codeUnit, bool isDown) => new()
    {
        Type = InputKeyboard,
        Data = new InputUnion
        {
            Keyboard = new KeyboardInput
            {
                ScanCode = codeUnit,
                Flags = KeyEventUnicode | (isDown ? 0u : KeyEventKeyUp),
                ExtraInfo = InjectionTag,
            },
        },
    };

    private static Input CreateMouseWheelInput(MacroInput input) => new()
    {
        Type = InputMouse,
        Data = new InputUnion
        {
            Mouse = new MouseInput
            {
                MouseData = unchecked((uint)(input.WheelDelta << 16)),
                Flags = input.WheelHorizontal ? MouseEventHWheel : MouseEventWheel,
                ExtraInfo = InjectionTag,
            },
        },
    };

    private static Input CreateMouseInput(MacroInput input, bool isDown)
    {
        var (flags, mouseData) = input.MouseButton switch
        {
            1 => (isDown ? MouseEventLeftDown : MouseEventLeftUp, 0u),
            2 => (isDown ? MouseEventRightDown : MouseEventRightUp, 0u),
            3 => (isDown ? MouseEventMiddleDown : MouseEventMiddleUp, 0u),
            4 => (isDown ? MouseEventXDown : MouseEventXUp, 1u),
            5 => (isDown ? MouseEventXDown : MouseEventXUp, 2u),
            _ => throw new ArgumentOutOfRangeException(nameof(input), "Unsupported mouse button."),
        };
        return new Input
        {
            Type = InputMouse,
            Data = new InputUnion
            {
                Mouse = new MouseInput
                {
                    MouseData = mouseData,
                    Flags = flags,
                    ExtraInfo = InjectionTag,
                },
            },
        };
    }

    private static void SendAll(Input[] inputs)
    {
        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
        if (sent != (uint)inputs.Length)
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                $"SendInput sent {sent} of {inputs.Length} macro events.");
        }
    }

    private static (uint Flags, int X, int Y) ResolveForegroundMove(int x, int y)
    {
        var window = GetForegroundWindow();
        if (window == 0 ||
            !GetClientRect(window, out var client) ||
            client.Right <= client.Left ||
            client.Bottom <= client.Top)
        {
            return (MouseEventMove, 0, 0);
        }

        var origin = default(Point);
        if (!ClientToScreen(window, ref origin))
        {
            return (MouseEventMove, 0, 0);
        }

        var width = client.Right - client.Left;
        var height = client.Bottom - client.Top;
        var screenX = origin.X + (int)((long)Math.Clamp(x, 0, 10_000) * (width - 1) / 10_000);
        var screenY = origin.Y + (int)((long)Math.Clamp(y, 0, 10_000) * (height - 1) / 10_000);
        return (
            MouseEventMove | MouseEventAbsolute | MouseEventVirtualDesk,
            NormalizeScreenX(screenX),
            NormalizeScreenY(screenY));
    }

    private static int NormalizeScreenX(int x)
    {
        var left = GetSystemMetrics(SmXVirtualScreen);
        var width = GetSystemMetrics(SmCxVirtualScreen);
        return width <= 1
            ? 0
            : (int)Math.Clamp(((long)x - left) * 65_535 / (width - 1), 0, 65_535);
    }

    private static int NormalizeScreenY(int y)
    {
        var top = GetSystemMetrics(SmYVirtualScreen);
        var height = GetSystemMetrics(SmCyVirtualScreen);
        return height <= 1
            ? 0
            : (int)Math.Clamp(((long)y - top) * 65_535 / (height - 1), 0, 65_535);
    }

    private static bool IsKeyDown(int virtualKey) => (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint inputCount, Input[] inputs, int inputSize);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(nint window, out Rect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClientToScreen(nint window, ref Point point);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        internal int X;
        internal int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        internal uint Type;
        internal InputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] internal KeyboardInput Keyboard;
        [FieldOffset(0)] internal MouseInput Mouse;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        internal ushort VirtualKey;
        internal ushort ScanCode;
        internal uint Flags;
        internal uint Time;
        internal nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        internal int X;
        internal int Y;
        internal uint MouseData;
        internal uint Flags;
        internal uint Time;
        internal nuint ExtraInfo;
    }
}
