using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.ComponentModel;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed partial class RazerHidDeviceService
{
    private enum AssignmentTable
    {
        Key,
        Analog,
        Multi,
    }

    // The Tartarus Pro engine reads each input from whichever assignment table answers;
    // analog keys are not guaranteed to share a table with the wheel or D-pad. 0x8C is
    // the mouse button table and must never be tried for a keypad.
    private static readonly AssignmentTable[] KeypadAssignmentTables =
        [AssignmentTable.Analog, AssignmentTable.Key, AssignmentTable.Multi];

    private readonly ConcurrentDictionary<(string InstanceId, byte ButtonId), AssignmentTable>
        _assignmentTables = new();
    // Firmware that rejects the 00:84 mode read cannot be switched into driver mode,
    // so remember it instead of failing (and re-logging) every mapping operation.
    private readonly ConcurrentDictionary<string, bool> _mappingModeUnavailable =
        new(StringComparer.OrdinalIgnoreCase);
    // Tartarus firmware can answer the 00:84 mode read with a payload that cannot be
    // parsed, but still switches modes through 00:04. Remember that so later rows skip it.
    private readonly ConcurrentDictionary<string, bool> _mappingModeBlind =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string?> _legacyControlPaths =
        new(StringComparer.OrdinalIgnoreCase);

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

    public Task<RazerHidButtonAssignment> GetButtonAssignmentAsync(
        RazerHidDeviceConnection connection,
        byte buttonId,
        byte mode,
        CancellationToken cancellationToken = default) =>
        WithMappingModeAsync(connection,
            () => GetButtonAssignmentCoreAsync(connection, buttonId, mode, cancellationToken),
            cancellationToken);

    private async Task<RazerHidButtonAssignment> GetButtonAssignmentCoreAsync(
        RazerHidDeviceConnection connection,
        byte buttonId,
        byte mode,
        CancellationToken cancellationToken)
    {
        if (UsesLegacyKeypadProfile(connection))
        {
            return await GetLegacyAssignmentAsync(
                connection, buttonId, mode, cancellationToken).ConfigureAwait(false);
        }

        var profileId = await ResolveMappingProfileIdAsync(connection, cancellationToken).ConfigureAwait(false);
        if (UsesPerInputAssignmentTables(connection))
        {
            var (_, assignment) = await ReadKeypadAssignmentAsync(
                connection, profileId, buttonId, mode, cancellationToken).ConfigureAwait(false);
            return assignment;
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

    // 0266/0282 Huntsman analog read the key table with the actuation band through 0x92/0x12.
    private static bool UsesAnalogKeyAssignments(RazerHidDeviceConnection connection) =>
        connection.Definition.ProductId is 0x0266 or 0x0282;

    // Huntsman V3 Pro firmware rejects 0x8D for modifiers and navigation keys.
    // 02EB's keypad engine reads its assignment table through 0x9E as well.
    private static bool UsesMultiKeyAssignments(RazerHidDeviceConnection connection) =>
        connection.Definition.ProductId is 0x02A6 or 0x02A7 or 0x02B0 or 0x02CF or 0x02EB;

    // The Tartarus Pro OBM engine reads each input from the analog, key or multi-key
    // table, in that order; only the Huntsman V3 Pro family reads everything multi-key.
    private static bool UsesPerInputAssignmentTables(RazerHidDeviceConnection connection) =>
        connection.Definition.ProductId == 0x0244;

    // The five Synapse 2 keypads predate the per-key OBM tables: their 20 main
    // keys live in one P2 profile blob, not behind 0x8D/0x9E.
    private static bool UsesLegacyKeypadProfile(RazerHidDeviceConnection connection) =>
        UsesLegacyKeypadProfile(connection.Definition.ProductId);

    private static bool UsesLegacyKeypadProfile(ushort productId) =>
        productId is 0x0111 or 0x0113 or 0x0201 or 0x0207 or 0x0208;

    // Synapse switches these keyboards to driver mode before its mapping page. In normal
    // mode the Tartarus V2/Pro/02EB answers 0x05 and the Huntsman V3 Pro family answers
    // 0x01 busy for the OBM tables, which looks like a dead device.
    private static bool RequiresMappingDriverMode(RazerHidDeviceConnection connection) =>
        // The Basilisk V3 Pro pair accepts an OBM write in normal mode but keeps
        // applying the old assignment until the host switches to driver mode.
        connection.Definition.ProductId is 0x00AA or 0x00AB or
            0x022B or 0x0244 or 0x02A6 or 0x02A7 or 0x02B0 or 0x02CF or 0x02EB;

    // These devices reject the 00:84 mode read but still require driver mode for
    // their OBM and analog-key writes. Synapse writes 00:04 without a read-back.
    private static bool UsesBlindMappingDriverMode(RazerHidDeviceConnection connection) =>
        connection.Definition.ProductId is 0x022B or 0x0244 or 0x02A6 or 0x02A7 or
            0x02B0 or 0x02CF or 0x02EB;

    private async Task<(AssignmentTable Table, RazerHidButtonAssignment Assignment)>
        ReadKeypadAssignmentAsync(
            RazerHidDeviceConnection connection,
            byte profileId,
            byte buttonId,
            byte mode,
            CancellationToken cancellationToken)
    {
        var key = (connection.InstanceId, buttonId);
        if (_assignmentTables.TryGetValue(key, out var known))
        {
            try
            {
                return (known, await ReadAssignmentTableAsync(
                    connection, profileId, buttonId, mode, known, cancellationToken).ConfigureAwait(false));
            }
            catch (Exception exception) when (IsAssignmentTableMiss(exception))
            {
                // A firmware update or profile change can move an input to another table;
                // probe again instead of failing this row for the rest of the session.
                _assignmentTables.TryRemove(key, out _);
            }
        }

        Exception? lastError = null;
        foreach (var table in KeypadAssignmentTables)
        {
            try
            {
                var assignment = await ReadAssignmentTableAsync(
                    connection, profileId, buttonId, mode, table, cancellationToken).ConfigureAwait(false);
                _assignmentTables[key] = table;
                return (table, assignment);
            }
            catch (Exception exception) when (IsAssignmentTableMiss(exception))
            {
                lastError = exception;
            }
        }

        throw new NotSupportedException(
            $"Device 1532:{connection.Definition.ProductId:X4} rejected every assignment table " +
            $"for button 0x{buttonId:X2}: {lastError?.Message}");
    }

    private static bool IsAssignmentTableMiss(Exception exception) =>
        exception is NotSupportedException or RazerCommandRejectedException or
            RazerDeviceTimeoutException or InvalidDataException;

    private async Task<RazerHidButtonAssignment> ReadAssignmentTableAsync(
        RazerHidDeviceConnection connection,
        byte profileId,
        byte buttonId,
        byte mode,
        AssignmentTable table,
        CancellationToken cancellationToken)
    {
        var request = table switch
        {
            AssignmentTable.Analog => RazerHidMappingProtocol.GetAnalogKeyAssignment(profileId, buttonId, mode),
            AssignmentTable.Key => RazerHidMappingProtocol.GetKeyAssignment(profileId, buttonId, mode),
            _ => RazerHidMappingProtocol.GetMultiKeyAssignment(profileId, buttonId, mode),
        };
        var response = await ExecuteAsync(connection, request, cancellationToken).ConfigureAwait(false);
        return table switch
        {
            AssignmentTable.Analog => RazerHidMappingProtocol.ParseAnalogAssignment(response),
            AssignmentTable.Multi => RazerHidMappingProtocol.ParseMultiKeyAssignment(response, buttonId),
            _ => RazerHidMappingProtocol.ParseAssignment(response),
        };
    }

    // Huntsman V3 Pro runs the OBM engine with analogKeyboard=false: it reads the
    // multi-key table but writes through the normal 0x0D key command. 02EB's keypad
    // engine similarly reads 0x9E and writes single assignments through 0x0D.
    private static bool WritesMultiKeyThroughKeyTable(RazerHidDeviceConnection connection) =>
        connection.Definition.ProductId is 0x02A6 or 0x02A7 or 0x02B0 or 0x02CF or 0x02EB;

    public Task SetButtonAssignmentAsync(
        RazerHidDeviceConnection connection,
        byte buttonId,
        byte mode,
        byte functionId,
        IReadOnlyList<byte> data,
        CancellationToken cancellationToken = default) =>
        RunExclusiveAsync(connection, _ => SetButtonAssignmentCoreAsync(
            connection, buttonId, mode, functionId, data, cancellationToken), cancellationToken);

    private async Task SetButtonAssignmentCoreAsync(
        RazerHidDeviceConnection connection,
        byte buttonId,
        byte mode,
        byte functionId,
        IReadOnlyList<byte> data,
        CancellationToken cancellationToken = default)
    {
        if (UsesLegacyKeypadProfile(connection))
        {
            await SetLegacyAssignmentAsync(
                connection, buttonId, mode, functionId, data, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        // Synapse flips a wireless mouse back to driver mode before touching onboard
        // mappings; in normal mode the firmware accepts the frame but keeps the old
        // assignment, which looked like "this device refuses edits". The Tartarus Pro/02EB
        // take the scoped mapping-mode wrapper instead, which restores normal mode.
        if (!RequiresMappingDriverMode(connection) &&
            connection.Capabilities.Contains(RazerHidBackendCapability.DeviceModeWrite))
        {
            await EnsureDriverModeAsync(connection, cancellationToken).ConfigureAwait(false);
        }

        // A bare modifier is [bit, 0] on NKRO products and [0, usage] elsewhere; the device
        // also decides what the read-back comparison must expect.
        data = RazerHidMappingActions.NormalizeKeyData(
            functionId, data, connection.Definition.ProductId);

        await WithMappingModeAsync(connection, async () =>
        {
            var profileId = await ResolveMappingProfileIdAsync(connection, cancellationToken).ConfigureAwait(false);
            RazerHidRequest request;
            if (UsesPerInputAssignmentTables(connection))
            {
                var (table, current) = await ReadKeypadAssignmentAsync(
                    connection, profileId, buttonId, mode, cancellationToken).ConfigureAwait(false);
                request = table switch
                {
                    AssignmentTable.Analog => RazerHidMappingProtocol.SetAnalogKeyAssignment(
                        profileId, buttonId, mode, current.ThresholdL, current.ThresholdH,
                        functionId, data.ToArray()),
                    AssignmentTable.Key => RazerHidMappingProtocol.SetKeyAssignment(
                        profileId, buttonId, mode, functionId, data.ToArray()),
                    // Synapse writes a keypad key through 0x0D even when it reads 0x9E.
                    _ => RazerHidMappingProtocol.SetKeyAssignment(
                        profileId, buttonId, mode, functionId, data.ToArray()),
                };
            }
            else if (UsesMultiKeyAssignments(connection))
            {
                request = WritesMultiKeyThroughKeyTable(connection)
                    ? RazerHidMappingProtocol.SetKeyAssignment(
                        profileId, buttonId, mode, functionId, data.ToArray())
                    : RazerHidMappingProtocol.SetMultiKeyAssignment(
                        profileId, buttonId, mode, functionId, data.ToArray());
            }
            else if (UsesAnalogKeyAssignments(connection))
            {
                // Preserve the key's actuation band; changing it would alter how the key feels.
                var current = await GetButtonAssignmentCoreAsync(connection, buttonId, mode, cancellationToken)
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
            var applied = await GetButtonAssignmentCoreAsync(connection, buttonId, mode, cancellationToken)
                .ConfigureAwait(false);
            if (applied.FunctionId != functionId || !applied.Data.SequenceEqual(data))
            {
                throw new InvalidOperationException(
                    "The device kept a different button assignment. Reconnect it over its 2.4 GHz " +
                    "receiver or USB cable, then retry; Bluetooth links cannot write onboard mappings.");
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<RazerHidButtonAssignment> GetLegacyAssignmentAsync(
        RazerHidDeviceConnection connection,
        byte buttonId,
        byte mode,
        CancellationToken cancellationToken)
    {
        if (IsLegacyDpadDirection(buttonId))
        {
            return await GetLegacyDpadAsync(
                connection, buttonId, mode, cancellationToken).ConfigureAwait(false);
        }

        var records = await ReadLegacyProfileAsync(connection, cancellationToken)
            .ConfigureAwait(false);
        var record = records.FirstOrDefault(item => item.IsValid && item.InputId == buttonId);
        return record.InputId == buttonId
            ? RazerKeypadProfileCodec.ToAssignment(buttonId, mode, record)
            : new RazerHidButtonAssignment(buttonId, mode, RazerHidMappingFunctions.Off, []);
    }

    private async Task SetLegacyAssignmentAsync(
        RazerHidDeviceConnection connection,
        byte buttonId,
        byte mode,
        byte functionId,
        IReadOnlyList<byte> data,
        CancellationToken cancellationToken)
    {
        if (IsLegacyDpadDirection(buttonId))
        {
            await SetLegacyDpadAsync(
                connection, buttonId, mode, functionId, data, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        var records = (await ReadLegacyProfileAsync(connection, cancellationToken)
            .ConfigureAwait(false)).ToList();
        var next = RazerKeypadProfileCodec.FromAssignment(
            buttonId,
            new RazerHidButtonAssignment(buttonId, mode, functionId, data));
        var index = records.FindIndex(item => item.IsValid && item.InputId == buttonId);
        if (index >= 0)
        {
            records[index] = next;
        }
        else
        {
            records.Add(next);
        }

        await WriteLegacyProfileAsync(connection, records, cancellationToken).ConfigureAwait(false);
        var applied = await GetLegacyAssignmentAsync(
            connection, buttonId, mode, cancellationToken).ConfigureAwait(false);
        if (applied.FunctionId != functionId || !applied.Data.SequenceEqual(data))
        {
            throw new InvalidOperationException(
                $"Keypad input {buttonId} kept a different assignment after the P2 write.");
        }
    }

    private static bool IsLegacyDpadDirection(byte buttonId) =>
        buttonId is >= 0xF0 and <= 0xF8;

    private async Task<RazerHidButtonAssignment> GetLegacyDpadAsync(
        RazerHidDeviceConnection connection,
        byte buttonId,
        byte mode,
        CancellationToken cancellationToken)
    {
        var index = buttonId - 0xF0;
        var response = await ExecuteAsync(
                connection,
                RazerKeypadDpadProtocol.GetDirection(index),
                cancellationToken)
            .ConfigureAwait(false);
        return RazerKeypadDpadProtocol.ParseDirection(response, index, buttonId, mode);
    }

    private async Task SetLegacyDpadAsync(
        RazerHidDeviceConnection connection,
        byte buttonId,
        byte mode,
        byte functionId,
        IReadOnlyList<byte> data,
        CancellationToken cancellationToken)
    {
        var index = buttonId - 0xF0;
        await ExecuteWithoutResultAsync(
                connection,
                RazerKeypadDpadProtocol.SetDirection(index, functionId, data),
                cancellationToken)
            .ConfigureAwait(false);
        var applied = await GetLegacyDpadAsync(
            connection, buttonId, mode, cancellationToken).ConfigureAwait(false);
        if (applied.FunctionId != functionId || !applied.Data.SequenceEqual(data))
        {
            throw new InvalidOperationException(
                $"Keypad D-pad input {buttonId} kept a different assignment.");
        }
    }

    private async Task<IReadOnlyList<RazerKeypadProfileRecord>> ReadLegacyProfileAsync(
        RazerHidDeviceConnection connection,
        CancellationToken cancellationToken)
    {
        var path = await ResolveLegacyControlPathAsync(connection, cancellationToken)
            .ConfigureAwait(false);
        var countReply = await RazerFilterControlTransport.TransferAsync(
                path,
                RazerKeypadProfileProtocol.CreateReadPacket(0),
                sizeof(uint),
                cancellationToken)
            .ConfigureAwait(false);
        if (countReply.Length < sizeof(uint))
        {
            throw new InvalidDataException(
                $"The keypad profile count reply was {countReply.Length} bytes.");
        }

        var count = BinaryPrimitives.ReadUInt32LittleEndian(countReply);
        if (count == 0)
        {
            return [];
        }

        count = Math.Min(count, RazerKeypadProfileProtocol.MaxRecordsPerChunk);
        var outputLength = sizeof(uint) + (int)count * RazerKeypadProfileProtocol.RecordLength;
        var output = await RazerFilterControlTransport.TransferAsync(
                path,
                RazerKeypadProfileProtocol.CreateReadPacket(count),
                outputLength,
                cancellationToken)
            .ConfigureAwait(false);
        return RazerKeypadProfileProtocol.ParseReadResponse(output);
    }

    private async Task WriteLegacyProfileAsync(
        RazerHidDeviceConnection connection,
        IReadOnlyList<RazerKeypadProfileRecord> records,
        CancellationToken cancellationToken)
    {
        var path = await ResolveLegacyControlPathAsync(connection, cancellationToken)
            .ConfigureAwait(false);
        foreach (var (input, outputLength) in RazerKeypadProfileProtocol.CreateWriteChunks(records))
        {
            await RazerFilterControlTransport.TransferAsync(
                    path, input, outputLength, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task<string> ResolveLegacyControlPathAsync(
        RazerHidDeviceConnection connection,
        CancellationToken cancellationToken)
    {
        if (_legacyControlPaths.TryGetValue(connection.InstanceId, out var cached) &&
            cached is not null)
        {
            return cached;
        }

        var candidates = new List<string?>
        {
            await RazerFilterEndpointDiscovery
                .DiscoverAsync(connection.Definition.ProductId, cancellationToken)
                .ConfigureAwait(false),
            @"\\.\RzUdd",
            @"\\.\RzControl",
        };
        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            try
            {
                _ = await RazerFilterControlTransport.TransferAsync(
                        candidate,
                        RazerKeypadProfileProtocol.CreateReadPacket(0),
                        sizeof(uint),
                        cancellationToken)
                    .ConfigureAwait(false);
                _legacyControlPaths[connection.InstanceId] = candidate;
                return candidate;
            }
            catch (Exception exception) when (
                exception is Win32Exception or InvalidDataException or IOException)
            {
                WriteDiagnostic(connection.Definition.ProductId,
                    $"keypad profile transport {candidate} unavailable: {exception.Message}");
            }
        }

        _legacyControlPaths[connection.InstanceId] = null;
        throw new NotSupportedException(
            $"Device 1532:{connection.Definition.ProductId:X4} has no usable RZCONTROL " +
            "endpoint for its P2 keypad profile.");
    }

    private async Task<bool> ProbeKeyAssignmentAsync(
        RazerHidDeviceDefinition definition,
        string devicePath,
        CancellationToken cancellationToken)
    {
        // The five Synapse 2 keypads keep their mapping in a P2 profile blob.
        // Their HID tables reject 0x8D/0x9E, so the usual probe cannot see it.
        if (UsesLegacyKeypadProfile(definition.ProductId))
        {
            return true;
        }

        var analog = definition.ProductId is 0x0266 or 0x0282;
        var multi = definition.ProductId is 0x0244 or 0x02A6 or 0x02A7 or 0x02B0 or 0x02CF or 0x02EB;
        // 022B keypads map the first art key to firmware key id 64 (0x40).
        // 0244 reads the main pad and wheel/D-pad rows through the multi-key table.
        (bool Multi, byte Id)[] probes = definition.ProductId switch
        {
            0x0244 => [(true, 0x40), (true, 0x03)],
            0x02EB => [(true, 0x40), (true, 0x03)],
            0x022B =>
            [
                (false, 0x40),
                (false, 0x02),
            ],
            _ => [(multi, (byte)0x02)],
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
        foreach (var (useMulti, keyId) in probes)
        {
            WriteDiagnostic(definition.ProductId,
                $"mapping probe profile={profileId} key=0x{keyId:X2} multi={useMulti} analog={analog}");
            var assignment = useMulti
                ? RazerHidMappingProtocol.GetMultiKeyAssignment(
                    profileId, keyId, RazerHidMappingProtocol.NormalMode)
                : analog
                    ? RazerHidMappingProtocol.GetAnalogKeyAssignment(
                        profileId, keyId, RazerHidMappingProtocol.NormalMode)
                    : RazerHidMappingProtocol.GetKeyAssignment(
                        profileId, keyId, RazerHidMappingProtocol.NormalMode);
            if (await ProbeMiddlewareAsync(definition.ProductId, devicePath, definition.DeviceWait,
                    "mapping.keyAssignment", assignment.Report,
                    response =>
                    {
                        if (useMulti)
                        {
                            _ = RazerHidMappingProtocol.ParseMultiKeyAssignment(response, keyId);
                        }
                        else if (analog)
                        {
                            _ = RazerHidMappingProtocol.ParseAnalogAssignment(response);
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

    public Task<IReadOnlyList<byte>> GetMappingButtonIdsAsync(
        RazerHidDeviceConnection connection,
        CancellationToken cancellationToken = default) =>
        WithMappingModeAsync(connection,
            () => GetMappingButtonIdsCoreAsync(connection, cancellationToken),
            cancellationToken);

    private async Task<IReadOnlyList<byte>> GetMappingButtonIdsCoreAsync(
        RazerHidDeviceConnection connection,
        CancellationToken cancellationToken)
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
                        return AddExtraButtonIds(connection.Definition.ProductId, keyIds);
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
            return AddExtraButtonIds(connection.Definition.ProductId, ids);
        }
        catch (Exception exception) when (exception is NotSupportedException or RazerDeviceTimeoutException)
        {
            // Razer's own UI never asks this GET for every product; some firmware only
            // answers the assignment table. The caller falls back to the product bundle ids.
            return [];
        }
    }

    private static IReadOnlyList<byte> AddExtraButtonIds(
        ushort productId,
        IReadOnlyList<byte> ids)
    {
        var extra = RazerKeypadKeyCatalog.ExtraButtonIds(productId);
        if (extra.Count == 0)
        {
            extra = RazerKeyboardExtraKeyCatalog.ExtraButtonIds(productId);
        }
        // An empty firmware list means "use the product layout"; returning only the
        // extras here would hide the layout keys and leave the wheel/D-pad alone.
        return extra.Count == 0 || ids.Count == 0
            ? ids
            : ids.Concat(extra).Distinct().Order().ToArray();
    }

    /// <summary>
    /// The Tartarus Pro/02EB answer status 0x05 ("command not supported in this device mode")
    /// to every OBM read until the firmware is switched to driver mode. Synapse switches
    /// mode before its mapping page and restores normal mode on unload; each operation here
    /// does the same so the keypad is never left in driver mode.
    /// </summary>
    private async Task WithMappingModeAsync(
        RazerHidDeviceConnection connection,
        Func<Task> action,
        CancellationToken cancellationToken) =>
        await WithMappingModeAsync(connection, async () =>
        {
            await action().ConfigureAwait(false);
            return true;
        }, cancellationToken).ConfigureAwait(false);

    private async Task<T> WithMappingModeAsync<T>(
        RazerHidDeviceConnection connection,
        Func<Task<T>> action,
        CancellationToken cancellationToken)
    {
        if (!RequiresMappingDriverMode(connection))
        {
            return await action().ConfigureAwait(false);
        }

        if (_mappingModeUnavailable.ContainsKey(connection.InstanceId))
        {
            return await action().ConfigureAwait(false);
        }

        var blind = _mappingModeBlind.ContainsKey(connection.InstanceId);
        var original = RazerHidStandardProtocol.NormalDeviceMode;
        if (!blind)
        {
            try
            {
                original = await ReadMappingDeviceModeAsync(connection, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (
                exception is InvalidDataException or NotSupportedException or
                    RazerCommandRejectedException or RazerDeviceTimeoutException)
            {
                if (!UsesBlindMappingDriverMode(connection))
                {
                    // A firmware that rejects 00:84 may still answer its OBM tables in
                    // normal mode, so a mode read it does not implement must not take the
                    // whole mapping page down.
                    _mappingModeUnavailable.TryAdd(connection.InstanceId, true);
                    WriteDiagnostic(connection.Definition.ProductId,
                        $"mapping device mode unavailable: {exception.Message}");
                    return await action().ConfigureAwait(false);
                }

                // Tartarus V2/Pro and the Huntsman V3 Pro family need driver mode even when
                // their firmware does not implement the 00:84 read.
                blind = true;
                _mappingModeBlind.TryAdd(connection.InstanceId, true);
                WriteDiagnostic(connection.Definition.ProductId,
                    $"mapping device mode read unavailable; switching without read-back: {exception.Message}");
            }
        }

        var switched = blind || original == RazerHidStandardProtocol.NormalDeviceMode;
        // A device left in driver mode by an earlier crash must be restored too.
        var restore = switched || original == RazerHidStandardProtocol.DriverDeviceMode;
        try
        {
            if (switched)
            {
                if (!await SetMappingDeviceModeAsync(
                        connection, RazerHidStandardProtocol.DriverDeviceMode, cancellationToken,
                        verify: !blind)
                        .ConfigureAwait(false))
                {
                    WriteDiagnostic(connection.Definition.ProductId,
                        "mapping driver mode did not stick; the OBM tables will likely reject with 0x05");
                }
            }

            return await action().ConfigureAwait(false);
        }
        finally
        {
            // Restore even if the write or its read-back fails: the firmware may
            // already be in driver mode and must not be left there.
            if (restore)
            {
                try
                {
                    await SetMappingDeviceModeAsync(
                        connection, RazerHidStandardProtocol.NormalDeviceMode, CancellationToken.None,
                        verify: !blind)
                        .ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    WriteDiagnostic(connection.Definition.ProductId,
                        $"mapping device mode restore failed: {exception.Message}");
                }
            }
        }
    }

    private async Task<bool> SetMappingDeviceModeAsync(
        RazerHidDeviceConnection connection,
        byte mode,
        CancellationToken cancellationToken,
        bool verify = true)
    {
        var request = RazerHidStandardProtocol.SetDeviceMode(connection.Definition, mode);
        try
        {
            await ExecuteWithoutResultAsync(connection, request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is RazerDeviceTimeoutException or NotSupportedException)
        {
            // Some firmware applies driver mode without acknowledging 00:04; Synapse
            // ignores the same timeout. The read-back decides whether it took. A firmware
            // that rejects 00:04 still gets the mapping attempt in its current mode.
        }

        if (!verify)
        {
            WriteDiagnostic(connection.Definition.ProductId,
                $"mapping device mode 0x{mode:X2} requested without read-back");
            return true;
        }

        var after = await ReadMappingDeviceModeAsync(connection, cancellationToken).ConfigureAwait(false);
        WriteDiagnostic(connection.Definition.ProductId,
            $"mapping device mode 0x{mode:X2} requested, now 0x{after:X2}");
        return after == mode;
    }

    private async Task<byte> ReadMappingDeviceModeAsync(
        RazerHidDeviceConnection connection,
        CancellationToken cancellationToken) =>
        RazerHidStandardProtocol.ParseDeviceMode(await ExecuteAsync(connection,
            RazerHidStandardProtocol.GetDeviceMode(connection.Definition), cancellationToken)
            .ConfigureAwait(false));
}
