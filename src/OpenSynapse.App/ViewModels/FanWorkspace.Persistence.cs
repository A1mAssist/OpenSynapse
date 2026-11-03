using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;

namespace OpenSynapse.App.ViewModels;

public sealed partial class FanWorkspace
{
    private async Task PersistBladeFanEditAsync(BladeFanProfileEdit edit, DeviceDescriptor? blade,
        CancellationToken cancellationToken, string activeFailureMessage)
    {
        var active = _isSelectedPerformancePowerActive();
        var power = _currentPowerSource();
        var document = _getProfile();
        var profileName = document.ActiveProfileName;
        var previousOwner = _bladeFanControlFingerprint;
        var hadRunningControl = _bladeFanControlCompletion is { IsCompleted: false };
        try
        {
            if (active)
            {
                var applied = await ApplyProfileAsync(blade, power, cancellationToken);
                if (applied.Error is not null) throw new InvalidOperationException(applied.Error);
            }
            await edit.SaveAsync(_saveProfileAsync, cancellationToken,
                active ? activeFailureMessage : AppStrings.Text("Text_CC12A6F5"));
        }
        catch (Exception failure)
        {
            edit.Restore();
            _refreshBladePerformanceEditor();
            if (active)
            {
                // A cancelled save must still release its newly started control. Only a
                // control that was actually running may be restarted, not a stored fixed target.
                var restoreError = await StopAsync("fan-edit-failed");
                if (restoreError is null && hadRunningControl && blade is not null &&
                    ReferenceEquals(document, _getProfile()) && document.ActiveProfileName == profileName &&
                    _currentPowerSource() == power)
                {
                    var restored = ProfileResolver.Resolve(document, blade, power).Blade;
                    if (BladeFanProfileFingerprint.Create(restored, blade.Id, power, profileName) == previousOwner)
                        restoreError = (await ApplyProfileAsync(blade, power, CancellationToken.None)).Error;
                }
                _requestDeviceRefresh();
                if (restoreError is not null)
                    throw new AggregateException(failure, new InvalidOperationException(restoreError));
            }
            throw;
        }
    }
}
