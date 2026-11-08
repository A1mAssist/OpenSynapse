using OpenSynapse.Core.Macros;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHidMappingRowViewModel
{
    internal void ApplyDecodedMapping(RazerHidDecodedMapping decoded, int localMacroIndex = -1)
    {
        if (decoded.Group == RazerHidMappingGroup.Macro &&
            (!CanUseOnboardMacro || localMacroIndex < 0))
        {
            return;
        }

        GroupIndex = (int)decoded.Group;
        if (decoded.OptionIndex >= 0)
        {
            OptionIndex = decoded.OptionIndex;
        }
        ModifierIndex = decoded.ModifierIndex;
        if (decoded.TurboRate is { } turbo)
        {
            TurboRate = turbo;
        }
        if (decoded.ProfileId is { } profileId)
        {
            ProfileId = profileId;
        }
        if (decoded.DpiClutchX is { } clutchX)
        {
            DpiClutchX = clutchX;
        }
        if (decoded.DpiClutchY is { } clutchY)
        {
            DpiClutchY = clutchY;
        }
        if (decoded.Group != RazerHidMappingGroup.Macro)
        {
            return;
        }

        MacroTargetIndex = (int)MacroExecutionTarget.Onboard;
        if (localMacroIndex >= 0)
        {
            OptionIndex = localMacroIndex;
        }
        if (decoded.MacroPlayback is { } playback)
        {
            MacroPlaybackIndex = (int)playback;
        }
        if (decoded.MacroRepeatCount is { } repeat)
        {
            MacroRepeatCount = repeat;
        }
        if (decoded.MacroSlot is not { } slot)
        {
            return;
        }

        for (var index = 0; index < MacroSlots.Count; index++)
        {
            if (MacroSlots[index].Id == slot)
            {
                SelectedMacroSlotIndex = index;
                break;
            }
        }
    }
}
