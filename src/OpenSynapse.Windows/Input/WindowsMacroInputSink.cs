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
    private const uint KeyEventScanCode = 0x0008;
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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint inputCount, Input[] inputs, int inputSize);

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
