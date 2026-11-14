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
    /// The editor's generic 1900..5400 range is wider than some models accept. Clamp to
    /// the model-wide floor so a curve can still reach the lowest speed the EC supports;
    /// the fixed slider keeps its stricter per-mode minimum.
    /// </summary>
    private BladeFanCurve ClampBladeFanCurve(BladeFanCurve curve)
    {
        var range = BladeModelCapabilities.FanCurveTargetRangeFor(
            _blade._bladeProductId, CurrentBladePerformanceMode);
        return curve.ClampFanSpeedRange(range.MinimumRpm, range.MaximumRpm);
    }
}
