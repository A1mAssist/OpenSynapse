namespace OpenSynapse.Windows.Protocols;

public static class BladeRecoveryCoordinator
{
    public static async Task RecoverAsync(
        BladeRecoveryMarker marker,
        IReadOnlyList<BladeRecoverySyntheticKey> keys) =>
        await RecoverAsync(marker, keys, BladeRecoveryFilterRestorer.DisableAndClear,
            path => BladeRecoveryDeviceRestorer.RestoreNormalModeAsync(new RazerFeatureTransport(), path),
            values => BladeRecoveryDeviceRestorer.ReleaseSyntheticKeys(values)).ConfigureAwait(false);

    internal static async Task RecoverAsync(
        BladeRecoveryMarker marker,
        IReadOnlyList<BladeRecoverySyntheticKey> keys,
        Action<string> restoreFilter,
        Func<string, Task> restoreNormalMode,
        Action<IReadOnlyList<BladeRecoverySyntheticKey>> releaseKeys)
    {
        ArgumentNullException.ThrowIfNull(marker);
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentNullException.ThrowIfNull(restoreFilter);
        ArgumentNullException.ThrowIfNull(restoreNormalMode);
        ArgumentNullException.ThrowIfNull(releaseKeys);
        var errors = new List<Exception>();
        try { restoreFilter(marker.FilterDevicePath); } catch (Exception exception) { errors.Add(exception); }
        try { await restoreNormalMode(marker.DevicePath).ConfigureAwait(false); } catch (Exception exception) { errors.Add(exception); }
        try { releaseKeys(keys); } catch (Exception exception) { errors.Add(exception); }
        if (errors.Count != 0) throw new AggregateException("Blade recovery was incomplete.", errors);
    }
}
