using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed partial class RazerHidDeviceService
{
    private enum MappingReadFamily
    {
        Analog,
        Key,
        Button,
    }

    private readonly System.Collections.Concurrent.ConcurrentDictionary<(string Device, byte ButtonId), MappingReadFamily>
        _mappingReadFamilies = new();

    /// <summary>
    /// Mapping tables are keyed by the profile the device is actually on. Profile 1 is only a
    /// fallback: a user can delete it and keep working on a later profile.
    /// </summary>
    private async Task<byte> ResolveMappingProfileIdAsync(
        RazerHidDeviceConnection connection,
        CancellationToken cancellationToken)
    {
        // The active profile is firmware state: keys, Synapse or another host can
        // change it between calls, so every read/write resolves it again.
        var profileId = RazerHidMappingProtocol.ProfileId;
        if (connection.Capabilities.Contains(RazerHidBackendCapability.OnboardProfileRead))
        {
            try
            {
                profileId = await GetActiveOnboardProfileIdAsync(connection, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is RazerDeviceTimeoutException or
                RazerCommandRejectedException or NotSupportedException or InvalidOperationException)
            {
                WriteDiagnostic(connection.Definition.ProductId,
                    $"mapping.activeProfile read failed: {exception.Message}");
            }
        }

        // Profile ids are one-based; a zero answer means the read did not resolve a table.
        if (profileId == 0)
        {
            profileId = RazerHidMappingProtocol.ProfileId;
        }

        return profileId;
    }

    /// <summary>
    /// Exposes the cached active profile to the analog-key short commands, which
    /// use the same profile slot as the mapping table.
    /// </summary>
    public Task<byte> ResolveActiveProfileIdAsync(
        RazerHidDeviceConnection connection,
        CancellationToken cancellationToken = default) =>
        ResolveMappingProfileIdAsync(connection, cancellationToken);

    public async Task<RazerHidButtonAssignment> GetButtonAssignmentAsync(
        RazerHidDeviceConnection connection,
        byte buttonId,
        byte mode,
        CancellationToken cancellationToken = default)
    {
        var profileId = await ResolveMappingProfileIdAsync(connection, cancellationToken).ConfigureAwait(false);
        if (connection.Definition.ProductId == 0x0244)
        {
            return await GetTartarusAssignmentAsync(
                connection, profileId, buttonId, mode, cancellationToken).ConfigureAwait(false);
        }

        if (UsesMultiKeyAssignments(connection))
        {
            return RazerHidMappingProtocol.ParseMultiKeyAssignment(
                await ExecuteAsync(connection,
                    RazerHidMappingProtocol.GetMultiKeyAssignment(profileId, buttonId, mode),
                    cancellationToken).ConfigureAwait(false),
                buttonId);
        }

        if (UsesAnalogKeyAssignments(connection))
        {
            return RazerHidMappingProtocol.ParseAnalogAssignment(await ExecuteAsync(connection,
                RazerHidMappingProtocol.GetAnalogKeyAssignment(profileId, buttonId, mode), cancellationToken)
                .ConfigureAwait(false));
        }

        var request = UsesKeyAssignments(connection)
            ? RazerHidMappingProtocol.GetKeyAssignment(profileId, buttonId, mode)
            : RazerHidMappingProtocol.GetAssignment(profileId, buttonId, mode);
        return RazerHidMappingProtocol.ParseAssignment(
            await ExecuteAsync(connection, request, cancellationToken).ConfigureAwait(false));
    }

    // Keyboards act on the key table; the button table accepts writes but never
    // drives a key press. Analog keyboards keep a separate table with the actuation band.
    // Synapse's OBM engine makes the same split.
    private static bool UsesKeyAssignments(RazerHidDeviceConnection connection) =>
        connection.Definition.Category == DeviceCategory.Keyboard;

    // 0266/0282 Huntsman analog and 0244 Tartarus Pro declare the analogV1 OBM engine.
    // Their key table carries the actuation band through 0x92/0x12.
    private static bool UsesAnalogKeyAssignments(RazerHidDeviceConnection connection) =>
        connection.Definition.ProductId is 0x0266 or 0x0282;

    // Huntsman V3 Pro firmware rejects 0x8D for modifiers and navigation keys. Synapse
    // reads and writes those keyboards through the 0x9E/0x1E multi-key table instead.
    private static bool UsesMultiKeyAssignments(RazerHidDeviceConnection connection) =>
        connection.Definition.ProductId is 0x02A6 or 0x02A7 or 0x02B0 or 0x02CF;

    // The Tartarus Pro mixes three tables: analog side-pad keys answer 0x92,
    // digital thumb keys answer the key table, and the wheel/D-pad answer the
    // button table. The firmware rejects the wrong family with status 0x05, so
    // negotiate per id instead of picking one command for the whole product.
    private static MappingReadFamily[] TartarusFamilies(byte buttonId) =>
        buttonId is >= 0x40 and <= 0x4B
            ? [MappingReadFamily.Analog, MappingReadFamily.Key, MappingReadFamily.Button]
            : [MappingReadFamily.Button, MappingReadFamily.Key, MappingReadFamily.Analog];

    private async Task<RazerHidButtonAssignment> GetTartarusAssignmentAsync(
        RazerHidDeviceConnection connection,
        byte profileId,
        byte buttonId,
        byte mode,
        CancellationToken cancellationToken)
    {
        var cacheKey = (connection.DevicePath ?? connection.InstanceId, buttonId);
        var preferred = _mappingReadFamilies.TryGetValue(cacheKey, out var cached)
            ? cached
            : TartarusFamilies(buttonId)[0];
        var order = new[] { preferred }
            .Concat(TartarusFamilies(buttonId).Where(family => family != preferred));

        Exception? last = null;
        foreach (var family in order)
        {
            try
            {
                var request = family switch
                {
                    MappingReadFamily.Analog =>
                        RazerHidMappingProtocol.GetAnalogKeyAssignment(profileId, buttonId, mode),
                    MappingReadFamily.Key =>
                        RazerHidMappingProtocol.GetKeyAssignment(profileId, buttonId, mode),
                    _ => RazerHidMappingProtocol.GetAssignment(profileId, buttonId, mode),
                };
                var response = await ExecuteAsync(connection, request, cancellationToken)
                    .ConfigureAwait(false);
                var assignment = family == MappingReadFamily.Analog
                    ? RazerHidMappingProtocol.ParseAnalogAssignment(response)
                    : RazerHidMappingProtocol.ParseAssignment(response);
                _mappingReadFamilies[cacheKey] = family;
                return assignment;
            }
            catch (Exception exception) when (IsUnsupportedMappingCommand(exception))
            {
                last = exception;
            }
        }

        throw last ?? new NotSupportedException(
            $"Tartarus Pro key 0x{buttonId:X2} did not answer any mapping table.");
    }

    private static bool IsUnsupportedMappingCommand(Exception exception) =>
        exception is NotSupportedException or RazerCommandRejectedException;

    public async Task SetButtonAssignmentAsync(
        RazerHidDeviceConnection connection,
        byte buttonId,
        byte mode,
        byte functionId,
        IReadOnlyList<byte> data,
        CancellationToken cancellationToken = default)
    {
        // Synapse flips a wireless mouse back to driver mode before touching onboard
        // mappings; in normal mode the firmware accepts the frame but keeps the old
        // assignment, which looked like "this device refuses edits".
        if (connection.Capabilities.Contains(RazerHidBackendCapability.DeviceModeWrite))
        {
            await EnsureDriverModeAsync(connection, cancellationToken).ConfigureAwait(false);
        }

        // A bare modifier is [bit, 0] on NKRO products and [0, usage] elsewhere; the device
        // also decides what the read-back comparison must expect.
        data = RazerHidMappingActions.NormalizeKeyData(
            functionId, data, connection.Definition.ProductId);

        var profileId = await ResolveMappingProfileIdAsync(connection, cancellationToken).ConfigureAwait(false);
        if (connection.Definition.ProductId == 0x0244)
        {
            await SetTartarusAssignmentAsync(
                connection, profileId, buttonId, mode, functionId, data, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        RazerHidRequest request;
        if (UsesMultiKeyAssignments(connection))
        {
            request = RazerHidMappingProtocol.SetMultiKeyAssignment(
                profileId, buttonId, mode, functionId, data.ToArray());
        }
        else if (UsesAnalogKeyAssignments(connection))
        {
            // Preserve the key's actuation band; changing it would alter how the key feels.
            var current = await GetButtonAssignmentAsync(connection, buttonId, mode, cancellationToken)
                .ConfigureAwait(false);
            request = RazerHidMappingProtocol.SetAnalogKeyAssignment(profileId, buttonId, mode,
                current.ThresholdL, current.ThresholdH, functionId, data.ToArray());
        }
        else
        {
            request = UsesKeyAssignments(connection)
                ? RazerHidMappingProtocol.SetKeyAssignment(profileId, buttonId, mode, functionId, data.ToArray())
                : RazerHidMappingProtocol.SetAssignment(profileId, buttonId, mode, functionId, data.ToArray());
        }
        await ExecuteWithoutResultAsync(connection, request, cancellationToken).ConfigureAwait(false);
        var applied = await GetButtonAssignmentAsync(connection, buttonId, mode, cancellationToken)
            .ConfigureAwait(false);
        if (applied.FunctionId != functionId || !applied.Data.SequenceEqual(data))
        {
            throw new InvalidOperationException(
                "The device kept a different button assignment. Reconnect it over its 2.4 GHz " +
                "receiver or USB cable, then retry; Bluetooth links cannot write onboard mappings.");
        }
    }

    private async Task SetTartarusAssignmentAsync(
        RazerHidDeviceConnection connection,
        byte profileId,
        byte buttonId,
        byte mode,
        byte functionId,
        IReadOnlyList<byte> data,
        CancellationToken cancellationToken)
    {
        var cacheKey = (connection.DevicePath ?? connection.InstanceId, buttonId);
        var preferred = _mappingReadFamilies.TryGetValue(cacheKey, out var cached)
            ? cached
            : TartarusFamilies(buttonId)[0];
        var order = new[] { preferred }
            .Concat(TartarusFamilies(buttonId).Where(family => family != preferred));

        Exception? last = null;
        foreach (var family in order)
        {
            try
            {
                RazerHidRequest request;
                if (family == MappingReadFamily.Analog)
                {
                    // Preserve the key's actuation band; changing it would alter how the key feels.
                    var current = await GetTartarusAssignmentAsync(
                            connection, profileId, buttonId, mode, cancellationToken)
                        .ConfigureAwait(false);
                    request = RazerHidMappingProtocol.SetAnalogKeyAssignment(
                        profileId, buttonId, mode,
                        current.ThresholdL, current.ThresholdH, functionId, data.ToArray());
                }
                else
                {
                    request = family == MappingReadFamily.Key
                        ? RazerHidMappingProtocol.SetKeyAssignment(
                            profileId, buttonId, mode, functionId, data.ToArray())
                        : RazerHidMappingProtocol.SetAssignment(
                            profileId, buttonId, mode, functionId, data.ToArray());
                }

                await ExecuteWithoutResultAsync(connection, request, cancellationToken)
                    .ConfigureAwait(false);
                var applied = await GetTartarusAssignmentAsync(
                        connection, profileId, buttonId, mode, cancellationToken)
                    .ConfigureAwait(false);
                if (applied.FunctionId == functionId && applied.Data.SequenceEqual(data))
                {
                    _mappingReadFamilies[cacheKey] = family;
                    return;
                }

                last = new InvalidOperationException(
                    "The device kept a different button assignment. Reconnect it over its 2.4 GHz " +
                    "receiver or USB cable, then retry; Bluetooth links cannot write onboard mappings.");
            }
            catch (Exception exception) when (IsUnsupportedMappingCommand(exception))
            {
                last = exception;
            }
        }

        throw last ?? new NotSupportedException(
            $"Tartarus Pro key 0x{buttonId:X2} rejected every mapping table.");
    }

    private async Task<bool> ProbeKeyAssignmentAsync(
        RazerHidDeviceDefinition definition,
        string devicePath,
        CancellationToken cancellationToken)
    {
        var analog = definition.ProductId is 0x0266 or 0x0282;
        var multi = definition.ProductId is 0x02A6 or 0x02A7 or 0x02B0 or 0x02CF;
        // 022B keypads map the first art key to SPAD_NUM_1, firmware key id 64 (0x40).
        // 0244 additionally answers the digital wheel/thumb rows only through 0x8C.
        (MappingReadFamily Family, byte Id)[] probes = definition.ProductId switch
        {
            0x0244 =>
            [
                (MappingReadFamily.Analog, 0x40),
                (MappingReadFamily.Analog, 0x02),
                (MappingReadFamily.Button, 0x03),
                (MappingReadFamily.Button, 0x09),
            ],
            0x022B => [(MappingReadFamily.Analog, 0x40), (MappingReadFamily.Analog, 0x02)],
            _ => [(analog ? MappingReadFamily.Analog : MappingReadFamily.Key, (byte)0x02)],
        };

        // A deleted profile 1 must not hide the whole mapping page; ask which profile is live.
        byte activeProfile = 0;
        var activeRequest = RazerOnboardProfileProtocol.CreateGetActiveProfileIdRequest(MiddlewareTransactionId);
        _ = await ProbeMiddlewareAsync(definition.ProductId, devicePath, definition.DeviceWait,
            "mapping.activeProfile", activeRequest,
            response =>
            {
                activeProfile = RazerOnboardProfileProtocol.ParseProfileId(response, activeRequest);
                return true;
            }, cancellationToken).ConfigureAwait(false);
        byte[] profileIds = activeProfile != 0 && activeProfile != RazerHidMappingProtocol.ProfileId
            ? [activeProfile, RazerHidMappingProtocol.ProfileId]
            : [RazerHidMappingProtocol.ProfileId];

        foreach (var profileId in profileIds)
        foreach (var (family, keyId) in probes)
        {
            WriteDiagnostic(definition.ProductId,
                $"mapping probe profile={profileId} key=0x{keyId:X2} family={family}");
            var assignment = family switch
            {
                MappingReadFamily.Analog => RazerHidMappingProtocol.GetAnalogKeyAssignment(
                    profileId, keyId, RazerHidMappingProtocol.NormalMode),
                MappingReadFamily.Button => RazerHidMappingProtocol.GetAssignment(
                    profileId, keyId, RazerHidMappingProtocol.NormalMode),
                _ => multi
                    ? RazerHidMappingProtocol.GetMultiKeyAssignment(
                        profileId, keyId, RazerHidMappingProtocol.NormalMode)
                    : RazerHidMappingProtocol.GetKeyAssignment(
                        profileId, keyId, RazerHidMappingProtocol.NormalMode),
            };
            if (await ProbeMiddlewareAsync(definition.ProductId, devicePath, definition.DeviceWait,
                    "mapping.keyAssignment", assignment.Report,
                    response =>
                    {
                        if (family == MappingReadFamily.Analog)
                        {
                            _ = RazerHidMappingProtocol.ParseAnalogAssignment(response);
                        }
                        else if (family == MappingReadFamily.Button)
                        {
                            _ = RazerHidMappingProtocol.ParseAssignment(response);
                        }
                        else if (multi)
                        {
                            _ = RazerHidMappingProtocol.ParseMultiKeyAssignment(response, keyId);
                        }
                        else
                        {
                            _ = RazerHidMappingProtocol.ParseAssignment(response);
                        }
                        return true;
                    }, cancellationToken).ConfigureAwait(false))
            {
                return true;
            }
        }

        return false;
    }

    public async Task<IReadOnlyList<byte>> GetMappingButtonIdsAsync(
        RazerHidDeviceConnection connection,
        CancellationToken cancellationToken = default)
    {
        // Keyboards list their key table with 0x8F. The 0x84 button list also carries
        // modifier buttons that 0x8D rejects, so it cannot drive keyboard rows.
        if (connection.Definition.Category == DeviceCategory.Keyboard)
        {
            try
            {
                var profileId = await ResolveMappingProfileIdAsync(connection, cancellationToken)
                    .ConfigureAwait(false);
                var keyResponse = await ExecuteAsync(connection,
                    RazerHidMappingProtocol.GetKeyAssignmentList(
                        profileId, RazerHidMappingProtocol.NormalMode), cancellationToken)
                    .ConfigureAwait(false);
                var keyIds = RazerHidMappingProtocol.ParseKeyAssignmentList(keyResponse);
                if (keyIds.Count > 0)
                {
                    if (RazerHidMappingProtocol.IsValidKeyIdList(keyIds))
                    {
                        return AddKeypadWheelDirections(connection.Definition.ProductId, keyIds);
                    }

                    // 0x8F can answer with assignment records after a driver rebind.
                    // Returning empty lets the mapping page use the product key layout.
                    WriteDiagnostic(connection.Definition.ProductId,
                        $"mapping 0x8F returned an invalid key list: count={keyIds.Count}, " +
                        $"unique={keyIds.Distinct().Count()}, zeros={keyIds.Count(id => id == 0)}");
                    return [];
                }
            }
            catch (Exception exception) when (
                exception is NotSupportedException or RazerDeviceTimeoutException or RazerCommandRejectedException)
            {
                // Some firmware only answers the button list; keep the existing fallback.
            }
        }

        try
        {
            var response = await ExecuteAsync(connection,
                RazerHidMappingProtocol.GetButtonList(), cancellationToken).ConfigureAwait(false);
            // The middleware only builds a button list when the reply declares data; a
            // zero-length success means the firmware stores no table, not that the read failed.
            var ids = response[6] == 0 ? [] : RazerHidMappingProtocol.ParseButtonList(response);
            return AddKeypadWheelDirections(connection.Definition.ProductId, ids);
        }
        catch (Exception exception) when (exception is NotSupportedException or RazerDeviceTimeoutException)
        {
            // Razer's own UI never asks this GET for every product; some firmware only
            // answers the assignment table. The caller falls back to the product bundle ids.
            return [];
        }
    }

    private static IReadOnlyList<byte> AddKeypadWheelDirections(
        ushort productId,
        IReadOnlyList<byte> ids)
    {
        var extra = RazerKeypadKeyCatalog.ExtraButtonIds(productId);
        return extra.Count == 0 || ids.Count == 0
            ? ids
            : ids.Concat(extra).Distinct().Order().ToArray();
    }
}
