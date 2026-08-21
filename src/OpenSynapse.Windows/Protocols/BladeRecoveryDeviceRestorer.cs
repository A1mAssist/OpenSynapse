using OpenSynapse.Windows.Devices;

namespace OpenSynapse.Windows.Protocols;

public static class BladeRecoveryDeviceRestorer
{
    public static async Task RestoreNormalModeAsync(
        IRazerFeatureTransport transport,
        string devicePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transport);
        await using var session = await transport.OpenSessionAsync(devicePath, cancellationToken).ConfigureAwait(false);
        await BladeAudioMuteRuntime.InitializeSessionAsync(session, cancellationToken).ConfigureAwait(false);
        await BladeAudioMuteRuntime.SetDeviceModeAsync(session, softwareMode: false, cancellationToken).ConfigureAwait(false);
    }

    public static IReadOnlyList<BladeMappingOutputEvent> CreateReleaseEvents(
        IEnumerable<BladeRecoverySyntheticKey> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        return keys.Distinct()
            .OrderBy(static key => key.ScanCode)
            .ThenBy(static key => key.Extended)
            .Select(static key => new BladeMappingOutputEvent(key.ScanCode, false, key.Extended))
            .ToArray();
    }

    public static void ReleaseSyntheticKeys(IEnumerable<BladeRecoverySyntheticKey> keys) =>
        new WindowsKeyboardInputSink().Send(CreateReleaseEvents(keys));
}
