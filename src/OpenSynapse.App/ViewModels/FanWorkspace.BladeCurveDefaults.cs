using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;

namespace OpenSynapse.App.ViewModels;

public sealed partial class FanWorkspace
{
    /// <summary>Build and validate inside the existing device-operation error boundary.</summary>
    public async Task ApplyBladeFanCurveEditorAsync(CancellationToken cancellationToken = default)
    {
        if (BladeFanCurveVisibility != Microsoft.UI.Xaml.Visibility.Visible) return;
        await ApplyBladeFanCurveCoreAsync(() => BladeFanCurveEditor.BuildCurve(), cancellationToken);
    }

    private BladeFanCurve? TryCreateCurve(BladeProfileSettings resolved)
    {
        if (resolved.FanCurve is not { } profile)
        {
            return null;
        }

        try
        {
            return ClampBladeFanCurve(profile.CreateCurve());
        }
        catch (ArgumentException)
        {
            // A hand-edited profile file can hold tiers the model rejects; fall back to the
            // default curve rather than blocking the card.
            return null;
        }
    }

    private BladeFanCurve DefaultBladeFanCurve() =>
        ClampBladeFanCurve(BladeFanCurve.CreateDefault());

    /// <summary>
    /// The editor's generic 1900..5400 range is wider than some models and performance
    /// modes accept. A stored target below the EC's floor makes the target report go
    /// silent, so clamp both ends before the curve is shown, saved or written.
    /// </summary>
    private BladeFanCurve ClampBladeFanCurve(BladeFanCurve curve)
    {
        var range = BladeModelCapabilities.FanTargetRangeFor(
            _blade._bladeProductId, CurrentBladePerformanceMode);
        return curve.ClampFanSpeedRange(range.MinimumRpm, range.MaximumRpm);
    }
}
