using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace OpenSynapse.Windows.Devices;

public sealed class WindowsTouchpadController
{
    private const string StatusKeyPath =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\PrecisionTouchPad\Status";
    private const string EnabledValueName = "Enabled";
    private const int DefaultReadbackAttempts = 10;
    private static readonly TimeSpan DefaultReadbackInterval = TimeSpan.FromMilliseconds(50);

    private readonly Func<object?> _readEnabledValue;
    private readonly Func<Input[], int, SendInputResult> _sendInput;
    private readonly Action<TimeSpan> _delay;
    private readonly int _readbackAttempts;
    private readonly TimeSpan _readbackInterval;

    public WindowsTouchpadController()
        : this(
            ReadEnabledValue,
            SendNativeInput,
            Thread.Sleep,
            DefaultReadbackAttempts,
            DefaultReadbackInterval)
    {
    }

    internal WindowsTouchpadController(
        Func<object?> readEnabledValue,
        Func<Input[], int, SendInputResult> sendInput,
        Action<TimeSpan> delay,
        int readbackAttempts = DefaultReadbackAttempts,
        TimeSpan? readbackInterval = null)
    {
        ArgumentNullException.ThrowIfNull(readEnabledValue);
        ArgumentNullException.ThrowIfNull(sendInput);
        ArgumentNullException.ThrowIfNull(delay);
        ArgumentOutOfRangeException.ThrowIfLessThan(readbackAttempts, 1);

        _readEnabledValue = readEnabledValue;
        _sendInput = sendInput;
        _delay = delay;
        _readbackAttempts = readbackAttempts;
        _readbackInterval = readbackInterval ?? DefaultReadbackInterval;
    }

    public bool? GetEnabled()
    {
        try
        {
            return _readEnabledValue() is int value ? value != 0 : null;
        }
        catch
        {
            return null;
        }
    }

    public bool ToggleVerified()
    {
        var before = GetEnabled();
        if (before is null)
        {
            throw new InvalidOperationException("无法读取 Windows 精确式触摸板状态；未发送切换输入。");
        }

        var inputs = BuildToggleInputs();
        var result = _sendInput(inputs, Marshal.SizeOf<Input>());
        if (result.Sent != inputs.Length)
        {
            throw new Win32Exception(
                result.Error,
                $"SendInput 只发送了 {result.Sent}/{inputs.Length} 个触摸板切换事件（Win32 错误 {result.Error}）。");
        }

        for (var attempt = 0; attempt < _readbackAttempts; attempt++)
        {
            var after = GetEnabled();
            if (after is not null && after != before)
            {
                return after.Value;
            }

            if (attempt + 1 < _readbackAttempts)
            {
                _delay(_readbackInterval);
            }
        }

        throw new InvalidOperationException(
            $"触摸板切换后回读未变化或不可用；原状态为 {(before.Value ? "启用" : "禁用")}。");
    }

    internal static Input[] BuildToggleInputs()
    {
        const ushort virtualKeyF24 = 0x87;
        const ushort virtualKeyControl = 0x11;
        const ushort virtualKeyLeftWindows = 0x5B;
        const uint extended = 0x0001;
        const uint keyUp = 0x0002;

        return
        [
            CreateKeyboardInput(virtualKeyF24, extended),
            CreateKeyboardInput(virtualKeyControl, extended),
            CreateKeyboardInput(virtualKeyLeftWindows, extended),
            CreateKeyboardInput(virtualKeyF24, extended),
            CreateKeyboardInput(virtualKeyF24, extended | keyUp),
            CreateKeyboardInput(virtualKeyControl, extended | keyUp),
            CreateKeyboardInput(virtualKeyLeftWindows, extended | keyUp),
            CreateKeyboardInput(virtualKeyF24, extended | keyUp),
        ];
    }

    private static object? ReadEnabledValue()
    {
        using var key = Registry.CurrentUser.OpenSubKey(StatusKeyPath, writable: false);
        return key is not null && key.GetValueKind(EnabledValueName) == RegistryValueKind.DWord
            ? key.GetValue(EnabledValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames)
            : null;
    }

    private static Input CreateKeyboardInput(ushort virtualKey, uint flags) => new()
    {
        Type = 1,
        Data = new InputUnion
        {
            Keyboard = new KeyboardInput
            {
                VirtualKey = virtualKey,
                ScanCode = virtualKey,
                Flags = flags,
            },
        },
    };

    private static SendInputResult SendNativeInput(Input[] inputs, int size)
    {
        var sent = SendInput((uint)inputs.Length, inputs, size);
        return new(sent, Marshal.GetLastWin32Error());
    }

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
        [FieldOffset(0)] internal MouseInput Mouse;
        [FieldOffset(0)] internal KeyboardInput Keyboard;
        [FieldOffset(0)] internal HardwareInput Hardware;
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
    internal struct KeyboardInput
    {
        internal ushort VirtualKey;
        internal ushort ScanCode;
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
