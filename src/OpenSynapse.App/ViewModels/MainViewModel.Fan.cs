using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;

namespace OpenSynapse.App.ViewModels;

public sealed partial class MainViewModel
{
    public async Task ApplyBladeFixedFanAsync(
        BladeFanMode mode,
        int? targetRpm,
        CancellationToken cancellationToken = default)
    {
        await RunDeviceOperationAsync(AppStrings.Text("Text_B436A1F2"), async () =>
        {
            var previous = _profile.Clone();
            _profile.Global.Blade.FanCurve = null;
            _profile.Global.Blade.FanMode = (byte)mode;
            _profile.Global.Blade.FanTargetRpm = targetRpm;
            var blade = _deviceDescriptors.FirstOrDefault(device =>
                device.ProtocolFamily == DeviceProtocolFamilies.Blade && device.Access == DeviceAccessState.Available);
            var result = await ApplyLoadedFanProfileAsync(
                blade,
                _powerSourceProvider.IsPluggedIn,
                cancellationToken);
            if (result.Error is not null)
            {
                _profile = previous;
                throw new InvalidOperationException(result.Error);
            }

            if (!await SaveProfileAsync(cancellationToken))
            {
                _profile = previous;
                await ApplyLoadedFanProfileAsync(
                    blade,
                    _powerSourceProvider.IsPluggedIn,
                    cancellationToken);
                throw new InvalidOperationException(AppStrings.Text("Text_3D32CBBD"));
            }
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
            _profile.Global.Blade.FanCurve = BladeFanCurveProfile.FromCurve(curve);
            _profile.Global.Blade.FanMode = null;
            _profile.Global.Blade.FanTargetRpm = null;
            var blade = _deviceDescriptors.FirstOrDefault(device =>
                device.ProtocolFamily == DeviceProtocolFamilies.Blade && device.Access == DeviceAccessState.Available);
            var result = await ApplyLoadedFanProfileAsync(
                blade,
                _powerSourceProvider.IsPluggedIn,
                cancellationToken);
            if (result.Error is not null)
            {
                _profile = previous;
                throw new InvalidOperationException(result.Error);
            }

            if (!await SaveProfileAsync(cancellationToken))
            {
                _profile = previous;
                await ApplyLoadedFanProfileAsync(
                    blade,
                    _powerSourceProvider.IsPluggedIn,
                    cancellationToken);
                throw new InvalidOperationException(AppStrings.Text("Text_BA5FB914"));
            }
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
        bool? powerState)
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
