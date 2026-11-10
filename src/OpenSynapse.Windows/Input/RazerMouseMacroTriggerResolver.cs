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
        if (buttonId is 4 or 5)
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
