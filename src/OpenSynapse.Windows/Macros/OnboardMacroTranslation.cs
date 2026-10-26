using System.Runtime.InteropServices;
using OpenSynapse.Core.Macros;

namespace OpenSynapse.Windows.Macros;

/// <summary>
/// Turns local macro steps into the firmware item stream. Steps the onboard format
/// cannot hold (text, commands, nested loops, absolute mouse paths) are rejected
/// instead of being dropped silently.
/// </summary>
public static class OnboardMacroTranslation
{
    public static IReadOnlyList<OnboardMacroItem> FromSteps(IReadOnlyList<MacroStep> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        var items = new List<OnboardMacroItem>(steps.Count * 2);
        foreach (var step in steps)
        {
            switch (step.Kind)
            {
                case MacroStepKind.Input when step.Input is { } input:
                    AppendInput(items, input, step);
                    break;

                case MacroStepKind.Delay:
                    items.Add(new OnboardMacroItem(OnboardMacroItemKind.Delay)
                    {
                        DelayMilliseconds = step.DelayMilliseconds,
                    });
                    break;

                default:
                    throw new InvalidOperationException(
                        $"Onboard macros store keys, mouse buttons, wheel notches and delays; {step.Kind} cannot be written.");
            }
        }

        if (items.Count == 0)
        {
            throw new InvalidOperationException("The selected macro has nothing an onboard slot can hold.");
        }

        return items;
    }

    private static void AppendInput(List<OnboardMacroItem> items, MacroInput input, MacroStep step)
    {
        switch (input.Kind)
        {
            case MacroInputKind.Keyboard:
                if (!step.IsDown)
                {
                    items.Add(Key(input, released: true));
                    return;
                }

                items.Add(Key(input, released: false));
                AppendAutoRelease(items, step, () => Key(input, released: true));
                return;

            case MacroInputKind.MouseButton:
                if (!step.IsDown)
                {
                    items.Add(Button(input, pressed: false));
                    return;
                }

                items.Add(Button(input, pressed: true));
                AppendAutoRelease(items, step, () => Button(input, pressed: false));
                return;

            case MacroInputKind.MouseWheel:
                items.Add(new OnboardMacroItem(OnboardMacroItemKind.Wheel)
                {
                    WheelDelta = input.WheelDelta,
                });
                return;

            default:
                throw new InvalidOperationException($"Unsupported macro input {input.Kind}.");
        }
    }

    private static void AppendAutoRelease(
        List<OnboardMacroItem> items,
        MacroStep step,
        Func<OnboardMacroItem> release)
    {
        if (!step.AutoRelease)
        {
            return;
        }

        if (step.HoldMilliseconds > 0)
        {
            items.Add(new OnboardMacroItem(OnboardMacroItemKind.Delay)
            {
                DelayMilliseconds = step.HoldMilliseconds,
            });
        }

        items.Add(release());
    }

    private static OnboardMacroItem Key(MacroInput input, bool released) =>
        new(OnboardMacroItemKind.Key)
        {
            Scancode = ResolveScancode(input),
            Released = released,
        };

    private static OnboardMacroItem Button(MacroInput input, bool pressed) =>
        new(OnboardMacroItemKind.MouseButton)
        {
            MouseButton = input.MouseButton,
            MousePressed = pressed,
        };
    private static int ResolveScancode(MacroInput input)
    {
        if (input.ScanCode > 0)
        {
            return input.ScanCode;
        }

        var scan = MapVirtualKey((uint)input.VirtualKey, MapVkVirtualToScan);
        if (scan == 0)
        {
            throw new InvalidOperationException(
                $"Key 0x{input.VirtualKey:X2} has no scan code to store on the device.");
        }

        return (int)scan | (input.Extended ? 0xE000 : 0);
    }

    private const uint MapVkVirtualToScan = 0;

    [DllImport("user32.dll", EntryPoint = "MapVirtualKeyW")]
    private static extern uint MapVirtualKey(uint code, uint mapType);
}

