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

        if (_blade._bladeMaxFanEnabled)
        {
            // HyperBoost owns the fan explicitly on models that support it; a manual
            // curve must not silently remove its thermal safeguard.
            if (CurrentBladePerformanceMode == BladePerformanceMode.Hyperboost)
            {
                return;
            }

            // Editing an inactive power profile must not change the fan owner that is
            // currently active on the device.
            if (_isSelectedPerformancePowerActive())
            {
                await _disableMaxFanAsync(cancellationToken);
            }
        }

        // A manual curve owns the target even when the profile is being edited from
        // another power source, so do not leave a stored Max Fan bit behind.
        target.MaxFanMode = (byte)BladeMaxFanMode.Disabled;
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
