using System.Runtime.InteropServices;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Macros;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Macros;

namespace OpenSynapse.Windows.Input;

/// <summary>
/// Turns a keyboard mapping button id (USB HID usage) into the trigger the macro
/// runtime sees. Typing keys stay <see cref="MacroInputKind.Keyboard"/> so the
/// low-level hook can swallow the physical key; a device-scoped trigger arrives
/// through Raw Input, which cannot suppress the keystroke. Numpad and PrintScreen
/// share non-extended scancodes with other keys, so those ambiguous ids stay
/// unbound instead of binding the wrong physical key.
/// </summary>
public static class RazerKeyboardMacroTriggerResolver
{
    private const uint MapVkScToVkEx = 3;

    public static MacroInput? Resolve(byte hidUsage, ushort productId)
    {
        // Mapping rows for the media cluster and macro column carry firmware key ids,
        // while the private report uses the product's DKM slot. Without this hop the
        // rows fall through to the HID table and never become software triggers.
        // Keep this ahead of the Huntsman conversion: its table starts at 1, so a
        // media id such as 200 would otherwise be rejected before it is translated.
        if (RazerKeyboardExtraKeyCatalog.ExtraButtonIds(productId).Contains(hidUsage))
        {
            if (ExtraKeyPrivateCode(hidUsage) is { } privateCode &&
                RazerPrivateKeyCatalog.IsPrivate(productId, privateCode))
            {
                return new MacroInput
                {
                    Kind = MacroInputKind.RazerKey,
                    DeviceId = productId,
                    RazerKeyCode = privateCode,
                };
            }

            // The id is a firmware key id, not a HID usage. Falling through would
            // bind a different physical key on products without a private code.
            return null;
        }

        // OBM keyboard mapping rows carry firmware key ids, not HID usages.
        if (RazerHuntsmanV3ProKeys.UsesFirmwareKeyIds(productId))
        {
            if (!RazerHuntsmanV3ProKeys.TryGetHidUsage(hidUsage, out var hidUsageFromFirmware))
            {
                return null;
            }

            hidUsage = hidUsageFromFirmware;
        }

        if (hidUsage is 0 or >= 0xE0 || productId == 0)
        {
            return null;
        }

        if (RazerPrivateKeyCatalog.IsPrivate(productId, hidUsage))
        {
            // Private keys never reach Raw Input; they arrive as RazerKey events.
            return new MacroInput
            {
                Kind = MacroInputKind.RazerKey,
                DeviceId = productId,
                RazerKeyCode = hidUsage,
            };
        }

        if (!RazerHidUsageMapping.TryGetScancode(hidUsage, out var scancode) ||
            (scancode & 0xFF00) == 0xE100)
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
            Kind = MacroInputKind.Keyboard,
            VirtualKey = (int)virtualKey,
            ScanCode = scanCode,
            Extended = extended,
        };
    }

    private static byte? ExtraKeyPrivateCode(byte id) => id switch
    {
        >= 160 and <= 164 => (byte)(id - 160 + 32),
        200 => 82,
        201 => 84,
        202 => 83,
        205 => 85,
        207 => 99,
        208 => 100,
        209 => 101,
        _ => null,
    };

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint code, uint mapType);
}
