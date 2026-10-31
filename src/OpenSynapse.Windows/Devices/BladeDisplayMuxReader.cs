using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Displays;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

internal static class BladeDisplayMuxReader
{
    public static async ValueTask<BladeDisplayMuxStatus> ReadAsync(
        RazerDeviceResolver resolver,
        RazerCapabilityClient client,
        IReadOnlyList<DeviceDescriptor> devices,
        CancellationToken cancellationToken)
    {
        var blade = resolver.FindReadyBlade(devices);
        if (blade is null)
        {
            return Unsupported(BladeDisplayMuxBlockReason.NoDevice);
        }

        if (ResolveCapabilities(blade) is not { } mux)
        {
            return Unsupported(BladeDisplayMuxBlockReason.MissingCapability);
        }

        try
        {
            var allowDds = AllowsDds();
            var capabilities = BladeDisplayMuxModes.AdmittedCapabilities(
                await ReadCapabilitiesAsync(client, blade, mux.Capabilities, cancellationToken),
                allowDds);
            var mode = await ReadModeAsync(client, blade, mux.Get, cancellationToken);
            if (!BladeDisplayMuxModes.CanSwitch(capabilities, allowDds))
            {
                return new BladeDisplayMuxStatus(
                    IsSupported: false,
                    CanChange: false,
                    Mode: mode,
                    BlockReason: BladeDisplayMuxBlockReason.MissingCapability,
                    Capabilities: capabilities);
            }

            return new BladeDisplayMuxStatus(
                IsSupported: true,
                CanChange: allowDds || mode != BladeDisplayMuxMode.Dds,
                Mode: mode,
                BlockReason: BladeDisplayMuxBlockReason.None,
                Capabilities: capabilities);
        }
        catch (Exception exception) when (RazerCapabilityClient.IsExpectedHardwareException(exception))
        {
            return Unsupported(BladeDisplayMuxBlockReason.ReadFailed);
        }
    }

    public static async ValueTask<BladeDisplayMuxSwitchResult> SetAsync(
        RazerDeviceResolver resolver,
        RazerCapabilityClient client,
        IReadOnlyList<DeviceDescriptor> devices,
        BladeDisplayMuxMode mode,
        CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        var blade = resolver.FindReadyBlade(devices);
        if (blade is null)
        {
            return new BladeDisplayMuxSwitchResult(BladeDisplayMuxSwitchOutcome.NoDevice);
        }

        if (ResolveCapabilities(blade) is not { } mux)
        {
            return new BladeDisplayMuxSwitchResult(BladeDisplayMuxSwitchOutcome.Unsupported);
        }

        var allowDds = AllowsDds();
        if (mode == BladeDisplayMuxMode.Dds && !allowDds)
        {
            return new BladeDisplayMuxSwitchResult(BladeDisplayMuxSwitchOutcome.Unsupported);
        }

        var capabilities = BladeDisplayMuxModes.AdmittedCapabilities(
            await ReadCapabilitiesAsync(client, blade, mux.Capabilities, cancellationToken),
            allowDds);
        if (!BladeDisplayMuxModes.CanSwitch(capabilities, allowDds) ||
            !BladeDisplayMuxModes.Supports(capabilities, mode, allowDds))
        {
            return new BladeDisplayMuxSwitchResult(BladeDisplayMuxSwitchOutcome.Unsupported);
        }

        var original = await ReadModeAsync(client, blade, mux.Get, cancellationToken);
        if (original == BladeDisplayMuxMode.Dds && !allowDds)
        {
            return new BladeDisplayMuxSwitchResult(BladeDisplayMuxSwitchOutcome.Unsupported);
        }
        if (original == mode)
        {
            return new BladeDisplayMuxSwitchResult(BladeDisplayMuxSwitchOutcome.Success);
        }

        try
        {
            await client.QueryAsync(blade, mux.Set, new byte[] { 0x00, (byte)mode }, cancellationToken);
            var actual = await ReadModeAsync(client, blade, mux.Get, cancellationToken);
            if (actual != mode)
            {
                throw new InvalidOperationException(
                    $"Display MUX readback mismatch: wrote {mode}, read {actual}.");
            }

            return new BladeDisplayMuxSwitchResult(BladeDisplayMuxSwitchOutcome.RestartRequired);
        }
        catch (Exception exception) when (
            RazerCapabilityClient.IsExpectedHardwareException(exception) || exception is OperationCanceledException)
        {
            var restored = await TryRestoreAsync(client, blade, original, mux);
            if (exception is OperationCanceledException)
            {
                throw new OperationCanceledException(
                    "Display MUX change was cancelled.", exception, cancellationToken);
            }

            return new BladeDisplayMuxSwitchResult(
                restored ? BladeDisplayMuxSwitchOutcome.Restored : BladeDisplayMuxSwitchOutcome.RestoreFailed);
        }
    }

    private static async Task<BladeDisplayMuxMode> ReadModeAsync(
        RazerCapabilityClient client,
        ResolvedRazerDevice blade,
        string capabilityId,
        CancellationToken cancellationToken)
    {
        var request = RazerCapabilityClient.CreateRequest(blade, capabilityId);
        var response = await client.QueryAsync(blade, capabilityId, cancellationToken);
        return BladeDisplayMuxProtocol.ParseMode(response, request);
    }

    private static async Task<BladeDisplayMuxCapabilities> ReadCapabilitiesAsync(
        RazerCapabilityClient client,
        ResolvedRazerDevice blade,
        string capabilityId,
        CancellationToken cancellationToken)
    {
        var request = RazerCapabilityClient.CreateRequest(blade, capabilityId);
        var response = await client.QueryAsync(blade, capabilityId, cancellationToken);
        return BladeDisplayMuxProtocol.ParseCapabilities(response, request);
    }

    private static async Task<bool> TryRestoreAsync(
        RazerCapabilityClient client,
        ResolvedRazerDevice blade,
        BladeDisplayMuxMode original,
        (string Get, string Set, string Capabilities) mux)
    {
        try
        {
            await client.QueryAsync(
                blade, mux.Set, new byte[] { 0x00, (byte)original }, CancellationToken.None);
            return await ReadModeAsync(client, blade, mux.Get, CancellationToken.None) == original;
        }
        catch (Exception exception) when (RazerCapabilityClient.IsExpectedHardwareException(exception))
        {
            return false;
        }
    }

    private static (string Get, string Set, string Capabilities)? ResolveCapabilities(
        ResolvedRazerDevice blade) =>
        HasCapabilities(
            blade, CapabilityIds.DisplayMuxGet, CapabilityIds.DisplayMuxSet,
            CapabilityIds.DisplayMuxCapabilitiesGet)
            ? (CapabilityIds.DisplayMuxGet, CapabilityIds.DisplayMuxSet,
                CapabilityIds.DisplayMuxCapabilitiesGet)
            : HasCapabilities(
                blade, CapabilityIds.LegacyDisplayMuxGet, CapabilityIds.LegacyDisplayMuxSet,
                CapabilityIds.LegacyDisplayMuxCapabilitiesGet)
                ? (CapabilityIds.LegacyDisplayMuxGet, CapabilityIds.LegacyDisplayMuxSet,
                    CapabilityIds.LegacyDisplayMuxCapabilitiesGet)
                : null;

    private static bool HasCapabilities(
        ResolvedRazerDevice blade,
        string get,
        string set,
        string capabilities) =>
        blade.Owns(get) && blade.Owns(set) && blade.Owns(capabilities);

    private static bool AllowsDds() =>
        // DDS=2 has no hardware-verified write/persistence/restore path yet.
        false;

    private static BladeDisplayMuxStatus Unsupported(BladeDisplayMuxBlockReason reason) =>
        new(
            IsSupported: false,
            CanChange: false,
            Mode: BladeDisplayMuxMode.Hybrid,
            BlockReason: reason,
            Capabilities: BladeDisplayMuxCapabilities.None);
}
