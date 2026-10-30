using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;

namespace OpenSynapse.App.ViewModels;

public sealed partial class FanWorkspace
{
    private BladeFanCurve? TryCreateCurve(BladeProfileSettings resolved)
    {
        if (resolved.FanCurve is not { } profile)
        {
            return null;
        }

        try
        {
            return profile.CreateCurve().ClampMaximumFanSpeed(
                BladeModelCapabilities.MaximumWritableFanTargetRpmFor(_blade._bladeProductId));
        }
        catch (ArgumentException)
        {
            // A hand-edited profile file can hold tiers the model rejects; fall back to the
            // default curve rather than blocking the card.
            return null;
        }
    }

    private BladeFanCurve DefaultBladeFanCurve() =>
        BladeFanCurve.CreateDefault().ClampMaximumFanSpeed(
            BladeModelCapabilities.MaximumWritableFanTargetRpmFor(_blade._bladeProductId));
}
