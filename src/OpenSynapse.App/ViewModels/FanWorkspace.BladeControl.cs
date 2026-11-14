using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;

namespace OpenSynapse.App.ViewModels;

public sealed partial class FanWorkspace
{
    /// <summary>
    /// Max Fan and a manual target are two owners. A manual curve or fixed speed
    /// must release the Custom-mode Max Fan bit before it writes the target.
    /// </summary>
    private async Task DisableMaxFanForManualControlAsync(
        BladeProfileSettings target,
        CancellationToken cancellationToken)
    {
        if (!_blade.SupportsMaxFan)
        {
            return;
        }

        var profileEnabled = target.MaxFanMode == (byte)BladeMaxFanMode.Enabled;
        if (_blade._bladeMaxFanEnabled)
        {
            if (CurrentBladePerformanceMode != BladePerformanceMode.Custom)
            {
                // HyperBoost can require Max Fan for its raised power budget; do not
                // silently remove that thermal safeguard.
                return;
            }

            await _disableMaxFanAsync(cancellationToken);
            target.MaxFanMode = (byte)BladeMaxFanMode.Disabled;
        }
        else if (profileEnabled)
        {
            target.MaxFanMode = (byte)BladeMaxFanMode.Disabled;
        }
    }

    /// <summary>
    /// The EC resets a manual target when the performance mode changes. Re-send the
    /// curve's last target immediately instead of waiting for a temperature change.
    /// </summary>
    internal async Task ReapplyAfterPerformanceModeChangeAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _bladeFanRuntime.ReapplyAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsExpectedFanException(exception))
        {
            _diagnosticLog.TryWrite("blade-fan", $"performance-mode reapply failed: {exception}");
        }
    }
}
