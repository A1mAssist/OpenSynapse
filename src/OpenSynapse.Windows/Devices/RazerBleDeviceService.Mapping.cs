using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed partial class RazerBleDeviceService
{
    private readonly SemaphoreSlim _mappingGate = new(1, 1);
    private readonly AsyncLocal<byte?> _mappingSessionProfile = new();
    private byte? _requestedMappingProfile;

    public Task RunMappingModeSessionAsync(Func<CancellationToken, Task> action,
        CancellationToken cancellationToken = default) =>
        RunMappingModeSessionAsync(async token =>
        {
            await action(token).ConfigureAwait(true);
            return true;
        }, cancellationToken);

    private async Task<T> RunMappingModeSessionAsync<T>(Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken)
    {
        // Preserve the caller's UI context for the shared mapping view model's callback.
        if ((!_requiresMappingMode && !IsKeyboard) || _mappingSessionProfile.Value.HasValue)
            return await action(cancellationToken).ConfigureAwait(true);
        if (!await _mappingGate.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(true))
            throw new TimeoutException("The BLE mapping operation queue timed out.");

        (byte Mode, byte Parameter)? original = null;
        var restore = false;
        Exception? failure = null;
        try
        {
            if (IsKeyboard)
            {
                _mappingSessionProfile.Value = MappingProfileId;
                return await action(cancellationToken).ConfigureAwait(true);
            }
            original = await ReadMappingDeviceModeAsync(cancellationToken).ConfigureAwait(true);
            // 00B4 setupDevice enters [3,0] before the OBM engine reads any profile assignments.
            if (original.Value != (3, 0))
            {
                restore = true;
                await SetMappingDeviceModeAsync(3, 0, cancellationToken).ConfigureAwait(true);
            }
            var profiles = await ReadProfilesAsync(cancellationToken).ConfigureAwait(true);
            var activeProfile = profiles.ActiveId is > 0 &&
                (profiles.Ids.Count == 0 || profiles.Ids.Contains(profiles.ActiveId.Value))
                ? profiles.ActiveId : null;
            var profileId = _requestedMappingProfile ?? activeProfile ??
                (profiles.Ids.Count > 0 ? profiles.Ids[0] : (byte?)null);
            if (profileId is not { } selected || selected == 0 ||
                (profiles.Ids.Count > 0 ? !profiles.Ids.Contains(selected) : profiles.ActiveId != selected))
                throw new InvalidOperationException("The BLE mouse did not confirm the requested onboard mapping profile.");
            _mappingSessionProfile.Value = selected;
            _diagnostic?.Invoke($"ble mapping session: originalMode={original.Value.Mode},{original.Value.Parameter} " +
                $"mode=3,0 profile={selected} available=[{string.Join(",", profiles.Ids)}]");
            return await action(cancellationToken).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            failure = exception;
            throw;
        }
        finally
        {
            try
            {
                if (restore && original is { } previous)
                {
                    using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    try
                    {
                        await SetMappingDeviceModeAsync(previous.Mode, previous.Parameter, cleanup.Token).ConfigureAwait(true);
                    }
                    catch (Exception exception)
                    {
                        _diagnostic?.Invoke($"ble mapping mode restoration failed: {exception.Message}");
                        throw new IOException("The BLE mapping operation could not restore the original device mode.",
                            failure is null ? exception : new AggregateException(failure, exception));
                    }
                }
            }
            finally
            {
                _mappingSessionProfile.Value = null;
                _mappingGate.Release();
            }
        }
    }

    private async Task<(byte Mode, byte Parameter)> ReadMappingDeviceModeAsync(CancellationToken token)
    {
        var response = await _session.SendAsync(RazerBleProtocol30.CreateGetDeviceMode,
            "get mapping device mode", token).ConfigureAwait(false);
        EnsureSuccess(response, "get mapping device mode");
        if (response.Payload.Count < 2)
            throw new InvalidOperationException("The BLE device mode reply does not contain mode and parameter.");
        return (response.Payload[0], response.Payload[1]);
    }

    private async Task SetMappingDeviceModeAsync(byte mode, byte parameter, CancellationToken token)
    {
        var response = await _session.SendAsync(transaction => RazerBleProtocol30.CreateSetDeviceMode(transaction, mode, parameter),
            "set mapping device mode", token).ConfigureAwait(false);
        EnsureSuccess(response, "set mapping device mode");
        if (await ReadMappingDeviceModeAsync(token).ConfigureAwait(false) != (mode, parameter))
            throw new InvalidOperationException("The BLE mouse did not confirm the mapping device mode.");
    }
}
