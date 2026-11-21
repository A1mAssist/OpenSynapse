using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

internal sealed partial class BladeTelemetryReader
{
    public async ValueTask<bool> SetBladeAutoSleepAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        var blade = FindWritableBlade(devices, CapabilityIds.AutoSleepSet, "auto sleep");
        var original = await ReadAutoSleepAsync(blade, cancellationToken);
        if (original == enabled)
        {
            return original;
        }

        var profile = ProfileArgument(blade, CapabilityIds.AutoSleepGet);
        return await SetAndVerifyAsync(
            blade,
            CapabilityIds.AutoSleepSet,
            original,
            target: enabled,
            value => [profile, value ? (byte)1 : (byte)0],
            ReadAutoSleepAsync,
            value => value == enabled,
            "Auto sleep",
            cancellationToken);
    }

    public async ValueTask<BladeTimeToDimTelemetry> SetBladeTimeToDimAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        int seconds,
        CancellationToken cancellationToken = default)
    {
        if (seconds is < 1 or > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(seconds));
        }

        var blade = FindWritableBlade(devices, CapabilityIds.TimeToDimSet, "time to dim");
        var original = await ReadTimeToDimAsync(blade, cancellationToken);
        // The UI only chooses the delay, so the write keeps the firmware's own
        // percentage and enables the timeout the user selected.
        var target = original with { Enabled = true, Seconds = seconds };
        if (target == original)
        {
            return original;
        }

        var profile = ProfileArgument(blade, CapabilityIds.TimeToDimGet);
        return await SetAndVerifyAsync(
            blade,
            CapabilityIds.TimeToDimSet,
            original,
            target,
            state => [profile, state.Enabled ? (byte)1 : (byte)0,
                (byte)(state.Seconds >> 8), (byte)(state.Seconds & 0xFF), state.Percentage],
            ReadTimeToDimAsync,
            state => state.Enabled == target.Enabled && state.Seconds == target.Seconds,
            "Time to dim",
            cancellationToken);
    }

    public async ValueTask<bool> SetBladeIndicatorLedAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        var blade = FindWritableBlade(devices, CapabilityIds.IndicatorLedSet, "indicator LED");
        var original = await ReadIndicatorLedAsync(blade, cancellationToken);
        if (original == enabled)
        {
            return original;
        }

        return await SetAndVerifyAsync(
            blade,
            CapabilityIds.IndicatorLedSet,
            original,
            target: enabled,
            value => [value ? (byte)1 : (byte)0],
            ReadIndicatorLedAsync,
            value => value == enabled,
            "Indicator LED",
            cancellationToken);
    }

    // Best effort: the Windows precision-touchpad toggle already succeeded, so a
    // missing or rejected EC status write must not roll that toggle back.
    public async ValueTask<bool> SetBladeTouchpadStatusAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        var blade = _resolver.FindReadyBlade(devices);
        if (blade is null ||
            !blade.Owns(CapabilityIds.TouchpadStatusSet) ||
            !blade.AllowsControlWrites)
        {
            return false;
        }

        try
        {
            var request = BladeSynapsePolicyProtocol.CreateSetTouchpadStatusRequest(enabled);
            var response = await _client.QueryBuiltRequestAsync(
                blade, CapabilityIds.TouchpadStatusSet, request, cancellationToken);
            if (response.Length != RazerFeatureReport.Length ||
                !RazerFeatureReport.IsAcceptedStatus(response[1]))
            {
                return false;
            }

            var expected = enabled ? (byte)0x01 : (byte)0x00;
            if (response[6] >= 3)
            {
                // Synapse reads the applied value back from the echoed
                // [profile, type, status] payload.
                return response[RazerFeatureReport.ArgumentsOffset + 2] == expected;
            }

            // Some firmware answers the set with an empty payload. Ask the EC for
            // the stored status so "synchronized" is not a guess.
            if (!blade.Owns(CapabilityIds.TouchpadStatusGet))
            {
                return true;
            }

            try
            {
                var readback = await _client.QueryBuiltRequestAsync(
                    blade,
                    CapabilityIds.TouchpadStatusGet,
                    BladeSynapsePolicyProtocol.CreateGetTouchpadStatusRequest(),
                    cancellationToken);
                return readback.Length == RazerFeatureReport.Length &&
                       readback[6] >= 3 &&
                       readback[RazerFeatureReport.ArgumentsOffset + 2] == expected;
            }
            catch (Exception readback) when (
                RazerCapabilityClient.IsExpectedHardwareException(readback) ||
                readback is OperationCanceledException)
            {
                return true;
            }
        }
        catch (Exception exception) when (
            RazerCapabilityClient.IsExpectedHardwareException(exception) ||
            exception is OperationCanceledException)
        {
            return false;
        }
    }

    public async ValueTask<BladeGpuCardDetectionTelemetry> SetBladeGpuCardDetectionAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        var blade = FindWritableBlade(
            devices, CapabilityIds.GpuCardDetectionSet, "GPU card detection");
        var original = await ReadGpuCardDetectionAsync(blade, cancellationToken);
        var target = original with { Enabled = enabled };
        if (target == original)
        {
            return original;
        }

        return await SetAndVerifyAsync(
            blade,
            CapabilityIds.GpuCardDetectionSet,
            original,
            target,
            state => [state.LedClassId, state.Enabled ? (byte)1 : (byte)0, state.Status],
            ReadGpuCardDetectionAsync,
            state => state.LedClassId == target.LedClassId && state.Enabled == target.Enabled,
            "GPU card detection",
            cancellationToken);
    }

    private ResolvedRazerDevice FindWritableBlade(
        IReadOnlyList<DeviceDescriptor> devices,
        string setCapability,
        string label)
    {
        var blade = _resolver.FindReadyBlade(devices)
            ?? throw new InvalidOperationException($"The Blade {label} control channel is unavailable.");
        EnsureControlWritesAdmitted(blade);
        if (!blade.Owns(setCapability))
        {
            throw new InvalidOperationException(
                $"The {label} control is not available on this Blade model.");
        }

        return blade;
    }

    private async Task<T> SetAndVerifyAsync<T>(
        ResolvedRazerDevice blade,
        string setCapability,
        T original,
        T target,
        Func<T, byte[]> encode,
        Func<ResolvedRazerDevice, CancellationToken, Task<T>> readAsync,
        Func<T, bool> matches,
        string label,
        CancellationToken cancellationToken)
    {
        try
        {
            await _client.QueryAsync(blade, setCapability, encode(target), cancellationToken);
            var actual = await readAsync(blade, cancellationToken);
            if (!matches(actual))
            {
                throw new InvalidOperationException(
                    $"{label} readback mismatch: wrote {target}, read {actual}.");
            }

            return actual;
        }
        catch (Exception exception) when (
            RazerCapabilityClient.IsExpectedHardwareException(exception) || exception is OperationCanceledException)
        {
            var restored = await TryRestoreAsync(blade, setCapability, original, encode, readAsync);
            var message = $"{label} update failed: {exception.Message} " +
                (restored
                    ? "The original value was restored."
                    : $"Original value restoration failed; check the {label} setting immediately.");
            if (exception is OperationCanceledException)
            {
                throw new OperationCanceledException(message, exception, cancellationToken);
            }

            throw new InvalidOperationException(message, exception);
        }
    }

    private async Task<bool> TryRestoreAsync<T>(
        ResolvedRazerDevice blade,
        string setCapability,
        T original,
        Func<T, byte[]> encode,
        Func<ResolvedRazerDevice, CancellationToken, Task<T>> readAsync)
    {
        try
        {
            await _client.QueryAsync(blade, setCapability, encode(original), CancellationToken.None);
            return Equals(await readAsync(blade, CancellationToken.None), original);
        }
        catch (Exception exception) when (RazerCapabilityClient.IsExpectedHardwareException(exception))
        {
            return false;
        }
    }

    private static byte ProfileArgument(ResolvedRazerDevice blade, string getCapability)
    {
        var arguments = blade.GetCapability(getCapability).Arguments;
        return arguments.Length > 0 ? arguments.Span[0] : (byte)0;
    }

    private async Task<bool> ReadAutoSleepAsync(
        ResolvedRazerDevice blade,
        CancellationToken cancellationToken) =>
        RazerHidMouseProtocol.ParseAutoSleepEnabled(
            await _client.QueryAsync(blade, CapabilityIds.AutoSleepGet, cancellationToken));

    private async Task<BladeTimeToDimTelemetry> ReadTimeToDimAsync(
        ResolvedRazerDevice blade,
        CancellationToken cancellationToken)
    {
        var state = RazerHidMouseProtocol.ParseTimeToDim(
            await _client.QueryAsync(blade, CapabilityIds.TimeToDimGet, cancellationToken));
        return new BladeTimeToDimTelemetry(state.Enabled, state.Seconds, state.Percentage);
    }

    private async Task<bool> ReadIndicatorLedAsync(
        ResolvedRazerDevice blade,
        CancellationToken cancellationToken) =>
        RazerHidMouseProtocol.ParseIndicatorLedConfiguration(
            await _client.QueryAsync(blade, CapabilityIds.IndicatorLedGet, cancellationToken)) != 0;

    private async Task<BladeGpuCardDetectionTelemetry> ReadGpuCardDetectionAsync(
        ResolvedRazerDevice blade,
        CancellationToken cancellationToken)
    {
        var state = RazerHidMouseProtocol.ParseGpuCardDetection(
            await _client.QueryAsync(blade, CapabilityIds.GpuCardDetectionGet, cancellationToken));
        return new BladeGpuCardDetectionTelemetry(state.LedClassId, state.Enabled, state.Status);
    }
}
