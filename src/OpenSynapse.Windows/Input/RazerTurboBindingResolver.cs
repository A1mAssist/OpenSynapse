using OpenSynapse.Core.Macros;
using OpenSynapse.Core.Profiles;

namespace OpenSynapse.Windows.Input;

/// <summary>One host-side turbo action for a private key.</summary>
public readonly record struct RazerTurboBinding(int MouseButton, int IntervalMilliseconds);

/// <summary>
/// In driver mode the firmware stops applying its own mappings, so a TurboButton
/// row never runs. Resolve it from the saved mapping so the host can repeat it.
/// </summary>
public static class RazerTurboBindingResolver
{
    // Synapse exposes 20 presses per second as the maximum.
    public const int MaximumRate = 20;
    public const int MinimumIntervalMilliseconds = 1000 / MaximumRate;

    public static RazerTurboBinding? Resolve(
        IReadOnlyList<ButtonMappingProfile> mappings,
        ushort productId,
        int razerKeyCode,
        bool isNaga,
        bool hyperShift)
    {
        var mode = hyperShift ? (byte)1 : (byte)0;
        foreach (var row in mappings)
        {
            if ((row.Mode ?? 0) != mode ||
                !StringComparer.Ordinal.Equals(row.Group, "TurboButton") ||
                row.FunctionData is not { Count: >= 3 } ||
                RazerMouseMacroTriggerResolver.Resolve(productId, row.ButtonId, isNaga) is not
                    { Kind: MacroInputKind.RazerKey } trigger ||
                trigger.RazerKeyCode != razerKeyCode)
            {
                continue;
            }

            // shortcut: only mouse buttons 1..5 have a SendInput path; other
            // turbo targets keep working only while the firmware owns the mouse.
            var button = row.FunctionData[0];
            var interval = (row.FunctionData[1] << 8) | row.FunctionData[2];
            if (button is < 1 or > 5 || interval <= 0)
            {
                continue;
            }

            // Older profiles can store 20 ms (50/s). Clamp the host repeat
            // instead of rejecting the row or letting it outrun Synapse.
            return new RazerTurboBinding(
                button,
                Math.Max(interval, MinimumIntervalMilliseconds));
        }

        return null;
    }
}
