using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Core.Sensors;

namespace OpenSynapse.App.ViewModels;

public sealed partial class MainViewModel
{
    public async Task ApplyBladeFixedFanAsync(
        BladeFanMode mode,
        int? targetRpm,
        CancellationToken cancellationToken = default) =>
        await ApplyBladeFanFixedAsync(mode, targetRpm, cancellationToken);

    /// <summary>
    /// Writes a fixed fan mode into the performance power profile, the same slot Max Fan and
    /// the fan curve use, so one power source cannot leave a stale curve behind.
    /// </summary>
    private async Task ApplyBladeFanFixedAsync(
        BladeFanMode mode,
        int? targetRpm,
        CancellationToken cancellationToken)
    {
        await RunDeviceOperationAsync(AppStrings.Text("Text_B436A1F2"), async () =>
        {
            var previous = _profile.Clone();
            var target = EditablePerformanceBladeProfile;
            target.FanCurve = null;
            target.FanMode = (byte)mode;
            target.FanTargetRpm = targetRpm;

            var blade = _deviceDescriptors.FirstOrDefault(device =>
                device.ProtocolFamily == DeviceProtocolFamilies.Blade && device.Access == DeviceAccessState.Available);
            if (IsSelectedPerformancePowerActive)
            {
                var result = await ApplyLoadedFanProfileAsync(blade, CurrentPowerSource, cancellationToken);
                if (result.Error is not null)
                {
                    _profile = previous;
                    throw new InvalidOperationException(result.Error);
                }

                if (!await SaveProfileAsync(cancellationToken))
                {
                    _profile = previous;
                    await ApplyLoadedFanProfileAsync(blade, CurrentPowerSource, cancellationToken);
                    throw new InvalidOperationException(AppStrings.Text("Text_3D32CBBD"));
                }

                RefreshBladePerformanceEditor();
                return;
            }

            if (!await SaveProfileAsync(cancellationToken))
            {
                _profile = previous;
                throw new InvalidOperationException(AppStrings.Text("Text_CC12A6F5"));
            }

            RefreshBladePerformanceEditor();
        }, cancellationToken);
    }

    public async Task ApplyBladeFanCurveAsync(
        BladeFanCurve curve,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(curve);
        await RunDeviceOperationAsync(AppStrings.Text("Text_0982EC10"), async () =>
        {
            var previous = _profile.Clone();

            // The curve lives in the same card as Max Fan, so it follows the performance
            // power profile. Writing the global slot would drop a per-power-source curve.
            var target = EditablePerformanceBladeProfile;
            target.FanCurve = BladeFanCurveProfile.FromCurve(curve);
            target.FanMode = null;
            target.FanTargetRpm = null;

            var blade = _deviceDescriptors.FirstOrDefault(device =>
                device.ProtocolFamily == DeviceProtocolFamilies.Blade && device.Access == DeviceAccessState.Available);
            if (IsSelectedPerformancePowerActive)
            {
                var result = await ApplyLoadedFanProfileAsync(blade, CurrentPowerSource, cancellationToken);
                if (result.Error is not null)
                {
                    _profile = previous;
                    throw new InvalidOperationException(result.Error);
                }

                if (!await SaveProfileAsync(cancellationToken))
                {
                    _profile = previous;
                    await ApplyLoadedFanProfileAsync(blade, CurrentPowerSource, cancellationToken);
                    throw new InvalidOperationException(AppStrings.Text("Text_BA5FB914"));
                }

                RefreshBladePerformanceEditor();
                return;
            }

            // Editing a power source the machine is not on: store it and let the existing
            // profile-change path push it when that source becomes active.
            if (!await SaveProfileAsync(cancellationToken))
            {
                _profile = previous;
                throw new InvalidOperationException(AppStrings.Text("Text_CC12A6F5"));
            }

            RefreshBladePerformanceEditor();
        }, cancellationToken);
    }

    public async Task StopBladeFanControlAsync() =>
        _ = await StopBladeFanControlAsync("explicit-stop");

    private async Task<string?> StopBladeFanControlAsync(string reason)
    {
        _bladeFanControlCompletion = null;
        _bladeFanControlFingerprint = string.Empty;
        if (!_bladeFanRuntime.IsRunning)
        {
            return null;
        }

        try
        {
            await _bladeFanRuntime.StopAsync().ConfigureAwait(false);
            return null;
        }
        catch (Exception exception) when (IsExpectedFanException(exception))
        {
            _diagnosticLog.TryWrite("blade-fan", $"{reason} restore failed: {exception}");
            return FormatOperationException(exception);
        }
    }

    private async Task ObserveBladeFanControlAsync(Task completion)
    {
        try
        {
            await completion.ConfigureAwait(false);
        }
        catch (Exception exception) when (IsExpectedFanException(exception))
        {
            if (ReferenceEquals(_bladeFanControlCompletion, completion))
            {
                _diagnosticLog.TryWrite("blade-fan", $"Fan control failed: {exception}");
                _bladeFanControlFingerprint = string.Empty;
                _bladeFanControlCompletion = null;
                try
                {
                    await _bladeFanRuntime.StopAsync().ConfigureAwait(false);
                }
                catch (Exception restoreException) when (IsExpectedFanException(restoreException))
                {
                    _diagnosticLog.TryWrite("blade-fan", $"Fan control recovery failed: {restoreException}");
                }
                RequestDeviceRefresh();
            }
        }
    }

    private static string CreateBladeFanFingerprint(
        BladeProfileSettings profile,
        string devicePath,
        PowerSourceKind powerState)
    {
        if (profile.FanCurve is { } curve)
        {
            var points = string.Join(",", curve.CpuPoints.Select(FormatPoint)) + "/" +
                string.Join(",", curve.GpuPoints.Select(FormatPoint));
            return $"curve\n{devicePath}\n{powerState}\n{curve.TemperatureMode}\n" +
                $"{curve.MinimumCpuTemperatureCelsius},{curve.MinimumGpuTemperatureCelsius}," +
                $"{curve.MinimumFanSpeedRpm}\n{points}";
        }

        return profile.FanMode is byte mode
            ? $"fixed\n{devicePath}\n{powerState}\n{mode}\n{profile.FanTargetRpm}"
            : string.Empty;

        static string FormatPoint(BladeFanCurvePoint point) =>
            $"{point.TemperatureCelsius}:{point.CpuFanSpeedRpm}:{point.GpuFanSpeedRpm}";
    }
}