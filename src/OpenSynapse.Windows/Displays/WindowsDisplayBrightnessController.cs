using System.ComponentModel;
using System.Runtime.InteropServices;
using Windows.Graphics.Display;

namespace OpenSynapse.Windows.Displays;

public sealed class WindowsDisplayBrightnessController
{
    private const double Step = 0.1;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<double> StepAsync(
        bool increase,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var brightness = BrightnessOverride.GetDefaultForSystem();
            if (!brightness.IsSupported)
            {
                SendSystemBrightnessKey(increase);
                return double.NaN;
            }

            var target = CalculateStep(brightness.BrightnessLevel, increase);
            brightness.SetBrightnessLevel(target, DisplayBrightnessOverrideOptions.None);
            brightness.StartOverride();
            brightness.StopOverride();
            if (!await BrightnessOverride.SaveForSystemAsync(brightness))
            {
                throw new InvalidOperationException("Windows 拒绝保存内置屏亮度。");
            }

            cancellationToken.ThrowIfCancellationRequested();
            return target;
        }
        catch (COMException)
        {
            // OLED panels commonly reject BrightnessOverride while still accepting
            // the Windows display-brightness media keys.
            SendSystemBrightnessKey(increase);
            return double.NaN;
        }
        finally
        {
            _gate.Release();
        }
    }

    internal static double CalculateStep(double current, bool increase)
    {
        if (!double.IsFinite(current))
        {
            throw new ArgumentOutOfRangeException(nameof(current));
        }

        return Math.Clamp(current + (increase ? Step : -Step), 0, 1);
    }

    private static void SendSystemBrightnessKey(bool increase)
    {
        var virtualKey = (ushort)(increase ? 0x89 : 0x88); // VK_DISPLAY_BRIGHTNESS_UP/DOWN
        var inputs = new[]
        {
            new Input { Type = InputKeyboard, Data = new InputUnion { Keyboard = new KeyboardInput { VirtualKey = virtualKey } } },
            new Input { Type = InputKeyboard, Data = new InputUnion { Keyboard = new KeyboardInput { VirtualKey = virtualKey, Flags = KeyUpFlag } } },
        };
        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
        if (sent != inputs.Length)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows 未接受显示亮度按键。");
        }
    }

    private const uint InputKeyboard = 1;
    private const uint KeyUpFlag = 0x0002;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint inputCount, Input[] inputs, int inputSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public KeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }
}
