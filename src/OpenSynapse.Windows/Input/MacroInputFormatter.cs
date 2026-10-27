using System.Runtime.InteropServices;
using System.Text;
using OpenSynapse.Core.Macros;

namespace OpenSynapse.Windows.Input;

public static class MacroInputFormatter
{
    public static string Describe(MacroInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Kind == MacroInputKind.MouseButton)
        {
            return input.MouseButton switch
            {
                1 => "Mouse 1",
                2 => "Mouse 2",
                3 => "Mouse 3",
                4 => "Mouse 4",
                5 => "Mouse 5",
                _ => "Mouse ?",
            };
        }

        if (input.Kind == MacroInputKind.NagaSideButton)
        {
            return $"Naga side {input.SideButton}";
        }

        if (input.Kind == MacroInputKind.DeviceButton)
        {
            var device = string.IsNullOrWhiteSpace(input.DeviceName)
                ? $"Razer {input.DeviceId:X4}"
                : input.DeviceName;
            return $"{device} · {DescribeKey(input)}";
        }

        if (input.Kind == MacroInputKind.RazerKey)
        {
            var device = string.IsNullOrWhiteSpace(input.DeviceName)
                ? $"Razer {input.DeviceId:X4}"
                : input.DeviceName;
            return $"{device} · {RazerPrivateKeyCatalog.Describe(
                (ushort)input.DeviceId,
                input.RazerKeyCode)}";
        }

        if (input.Kind == MacroInputKind.MouseWheel)
        {
            return input.WheelHorizontal
                ? $"Horizontal wheel {input.WheelDelta}"
                : $"Wheel {input.WheelDelta}";
        }

        return DescribeKey(input);
    }

    private static string DescribeKey(MacroInput input)
    {
        if (input.VirtualKey is <= 0 or > 0xFF)
        {
            return "Key ?";
        }

        var scanCode = input.ScanCode;
        if (scanCode <= 0)
        {
            scanCode = checked((int)MapVirtualKey((uint)input.VirtualKey, 0));
        }

        if (scanCode > 0)
        {
            var text = new StringBuilder(64);
            var lParam = (scanCode << 16) | (input.Extended ? 1 << 24 : 0);
            if (GetKeyNameText(lParam, text, text.Capacity) > 0)
            {
                return text.ToString();
            }
        }

        return $"VK 0x{input.VirtualKey:X2}";
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "MapVirtualKeyW")]
    private static extern uint MapVirtualKey(uint code, uint mapType);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetKeyNameText(int lParam, StringBuilder text, int size);
}
