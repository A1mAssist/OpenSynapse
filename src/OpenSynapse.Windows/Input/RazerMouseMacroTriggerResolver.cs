using OpenSynapse.Core.Macros;

namespace OpenSynapse.Windows.Input;

/// <summary>
/// Resolves a mouse mapping button id to the trigger the macro runtime sees.
/// Naga side plates win over overlapping private-key slots 32 and 33; other
/// private keys still resolve through the catalog.
/// </summary>
public static class RazerMouseMacroTriggerResolver
{
    public static MacroInput? Resolve(ushort productId, byte buttonId, bool isNaga)
    {
        // The mapping page uses the standard button ids: 1 left, 2 right, 3 middle,
        // 4 back, 5 forward. Only 4/5 used to resolve, so a left-click macro could
        // never be bound from the key page.
        if (buttonId is >= 1 and <= 5)
        {
            return new MacroInput
            {
                Kind = MacroInputKind.MouseButton,
                MouseButton = buttonId,
            };
        }

        if (isNaga && RazerNagaSideButtonResolver.Resolve(buttonId) is { } naga)
        {
            return naga;
        }

        // The mapping page reports its own button id; the private key stream reports
        // the DKM code. Resolve through the per-product bundle map first.
        if (RazerSpecialKeyMap.TryResolve(productId, buttonId, out var razerKeyCode))
        {
            return new MacroInput
            {
                Kind = MacroInputKind.RazerKey,
                DeviceId = productId,
                RazerKeyCode = razerKeyCode,
            };
        }

        return RazerPrivateKeyCatalog.IsPrivate(productId, buttonId)
            ? new MacroInput
            {
                Kind = MacroInputKind.RazerKey,
                DeviceId = productId,
                RazerKeyCode = buttonId,
            }
            : null;
    }
}
