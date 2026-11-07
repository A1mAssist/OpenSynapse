using System.Runtime.InteropServices;
using OpenSynapse.Core.Macros;
using OpenSynapse.Windows.Macros;

namespace OpenSynapse.Windows.Input;

/// <summary>
/// Turns a keyboard mapping button id (USB HID usage) into the device-scoped
/// trigger the macro runtime sees on Raw Input. Numpad and PrintScreen share
/// non-extended scancodes with other keys, so those ambiguous ids stay unbound
/// instead of binding the wrong physical key.
/// </summary>
public static class RazerKeyboardMacroTriggerResolver
{
    private const uint MapVkScToVkEx = 3;

    public static MacroInput? Resolve(byte hidUsage, ushort productId)
    {
        if (hidUsage is 0 or >= 0xE0 || productId == 0)
        {
            return null;
        }

        if (!RazerHidUsageMapping.TryGetScancode(hidUsage, out var scancode) ||
            (scancode & 0xFF00) == 0xE100 ||
            RazerPrivateKeyCatalog.IsPrivate(productId, hidUsage))
        {
            return null;
        }

        var extended = scancode > 0xFF;
        if (!extended && (scancode == 0x37 || scancode is >= 0x47 and <= 0x53))
        {
            return null;
        }

        var scanCode = scancode & 0xFF;
        var virtualKey = MapVirtualKey((uint)scanCode, MapVkScToVkEx);
        if (virtualKey is 0 or > 0xFF)
        {
            return null;
        }

        return new MacroInput
        {
            Kind = MacroInputKind.DeviceButton,
            DeviceId = productId,
            VirtualKey = (int)virtualKey,
            ScanCode = scanCode,
            Extended = extended,
        };
    }

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint code, uint mapType);
}
