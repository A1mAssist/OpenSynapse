using System.Runtime.InteropServices;
using OpenSynapse.Core.Macros;
using OpenSynapse.Windows.Macros;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed partial class RazerKeypadMappingRuntime
{
    private static IEnumerable<MacroInput> CreateOutputs(RazerHidButtonAssignment assignment)
    {
        var data = assignment.Data;
        switch (assignment.FunctionId)
        {
            case RazerHidMappingFunctions.KeyCode:
                foreach (var input in KeyboardOutputs(data[0], data[1]))
                {
                    yield return input;
                }
                break;
            case RazerHidMappingFunctions.ButtonCode:
                yield return data[0] <= 5
                    ? new MacroInput { Kind = MacroInputKind.MouseButton, MouseButton = data[0] }
                    : new MacroInput
                    {
                        Kind = MacroInputKind.MouseWheel,
                        WheelHorizontal = data[0] is 0x34 or 0x35 or 0x68 or 0x69,
                        WheelDelta = data[0] is 0x09 or 0x35 or 0x69 ? 120 : -120,
                    };
                break;
            case RazerHidMappingFunctions.MediaKeys:
                yield return new MacroInput
                {
                    Kind = MacroInputKind.Keyboard,
                    VirtualKey = RazerKeypadInputCatalog.MediaVirtualKey((ushort)((data[0] << 8) | data[1])),
                    Extended = true,
                };
                break;
            case RazerHidMappingFunctions.Win8ShortcutsKey:
                var (modifiers, usage) = data[0] switch
                {
                    99 => ((byte)0x08, (byte)70),  // Win + Print Screen
                    100 => ((byte)0x08, (byte)10), // Win + G
                    101 => ((byte)0x08, (byte)43), // Win + Tab
                    _ => throw new NotSupportedException("Unknown Windows shortcut."),
                };
                foreach (var input in KeyboardOutputs(modifiers, usage))
                {
                    yield return input;
                }
                break;
        }
    }

    private static IEnumerable<MacroInput> KeyboardOutputs(byte modifiers, byte usage)
    {
        for (var bit = 0; bit < 8; bit++)
        {
            if ((modifiers & (1 << bit)) != 0)
            {
                yield return KeyboardInput((byte)(224 + bit));
            }
        }
        if (usage != 0)
        {
            yield return KeyboardInput(usage);
        }
    }

    private static MacroInput KeyboardInput(byte usage)
    {
        // Pause has an E1 sequence; the shared E0 scancode entry means Break.
        if (usage == 72)
        {
            return new MacroInput { Kind = MacroInputKind.Keyboard, VirtualKey = 0x13 };
        }
        if (!RazerHidUsageMapping.TryGetScancode(usage, out var code))
        {
            throw new NotSupportedException($"No Windows scancode exists for HID usage {usage}.");
        }
        return new MacroInput
        {
            Kind = MacroInputKind.Keyboard,
            ScanCode = code & 0xFF,
            Extended = (code & 0xFF00) == 0xE000,
            VirtualKey = (int)MapVirtualKey((uint)code, 3),
        };
    }

    [DllImport("user32.dll", EntryPoint = "MapVirtualKeyW")]
    private static extern uint MapVirtualKey(uint code, uint mapType);
}
