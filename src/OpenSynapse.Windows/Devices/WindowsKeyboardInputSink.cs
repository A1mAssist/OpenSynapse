using System.ComponentModel;
using System.Runtime.InteropServices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

/// <summary>
/// Sends MappingEngine keyboard outputs through the documented SendInput API.
/// The extra-info tag lets a future low-level hook ignore our own injections.
/// </summary>
public sealed class WindowsKeyboardInputSink
{
    internal static readonly nuint InjectionTag = unchecked((nuint)0x4F534D4150505554UL);

    private readonly Func<Input[], int, SendInputResult> _sendInput;
    private readonly object _sync = new();

    public WindowsKeyboardInputSink()
        : this(SendNativeInput)
    {
    }

    internal WindowsKeyboardInputSink(Func<Input[], int, SendInputResult> sendInput)
    {
        _sendInput = sendInput ?? throw new ArgumentNullException(nameof(sendInput));
    }

    public void Send(IReadOnlyList<BladeMappingOutputEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        if (events.Count == 0)
        {
            return;
        }

        var inputs = events.Select(CreateInput).ToArray();
        lock (_sync)
        {
            var result = _sendInput(inputs, Marshal.SizeOf<Input>());
            if (result.Sent == inputs.Length)
            {
                return;
            }

            var rollback = inputs
                .Take((int)Math.Min(result.Sent, (uint)inputs.Length))
                .Where(input => (input.Data.Keyboard.Flags & KeyUpFlag) == 0)
                .Reverse()
                .Select(CreateKeyUp)
                .ToArray();
            var rollbackResult = rollback.Length == 0
                ? new SendInputResult(0, 0)
                : _sendInput(rollback, Marshal.SizeOf<Input>());
            var original = new Win32Exception(
                result.Error,
                $"SendInput 只发送了 {result.Sent}/{inputs.Length} 个映射事件。");
            if (rollbackResult.Sent != rollback.Length)
            {
                throw new AggregateException(
                    original,
                    new Win32Exception(
                        rollbackResult.Error,
                        $"SendInput 回滚只发送了 {rollbackResult.Sent}/{rollback.Length} 个释放事件。"));
            }

            throw original;
        }
    }

    private static Input CreateInput(BladeMappingOutputEvent value)
    {
        if (value.ScanCode is < 0 or > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        var flags = ScanCodeFlag | (value.Extended ? ExtendedKeyFlag : 0u);
        if (!value.IsDown)
        {
            flags |= KeyUpFlag;
        }

        return new()
        {
            Type = InputKeyboard,
            Data = new InputUnion
            {
                Keyboard = new KeyboardInput
                {
                    ScanCode = (ushort)value.ScanCode,
                    Flags = flags,
                    ExtraInfo = InjectionTag,
                },
            },
        };
    }

    private static Input CreateKeyUp(Input value)
    {
        value.Data.Keyboard.Flags |= KeyUpFlag;
        return value;
    }

    private static SendInputResult SendNativeInput(Input[] inputs, int size)
    {
        var sent = SendInput((uint)inputs.Length, inputs, size);
        return new(sent, Marshal.GetLastWin32Error());
    }

    private const uint InputKeyboard = 1;
    private const uint ScanCodeFlag = 0x0008;
    private const uint ExtendedKeyFlag = 0x0001;
    private const uint KeyUpFlag = 0x0002;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint inputCount, Input[] inputs, int inputSize);

    internal readonly record struct SendInputResult(uint Sent, int Error);

    [StructLayout(LayoutKind.Sequential)]
    internal struct Input
    {
        internal uint Type;
        internal InputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct InputUnion
    {
        [FieldOffset(0)] internal KeyboardInput Keyboard;
        [FieldOffset(0)] internal MouseInput Mouse;
        [FieldOffset(0)] internal HardwareInput Hardware;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct KeyboardInput
    {
        internal ushort VirtualKey;
        internal ushort ScanCode;
        internal uint Flags;
        internal uint Time;
        internal nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MouseInput
    {
        internal int X;
        internal int Y;
        internal uint MouseData;
        internal uint Flags;
        internal uint Time;
        internal nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct HardwareInput
    {
        internal uint Message;
        internal ushort ParameterLow;
        internal ushort ParameterHigh;
    }
}
