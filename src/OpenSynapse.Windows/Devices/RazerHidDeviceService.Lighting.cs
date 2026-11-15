using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed partial class RazerHidDeviceService
{
    public async Task<byte> GetBrightnessAsync(
        RazerHidDeviceConnection connection,
        RazerHidStorage? storage = null,
        RazerHidLedZone? ledId = null,
        CancellationToken cancellationToken = default)
    {
        var selectedZone = ledId ?? (storage is null || storage == connection.Definition.DefaultStorage
            ? GetDefaultBrightnessZone(connection, write: false) : null);
        var zone = RazerHidLightingProtocol.ResolveBrightnessZone(connection.Definition, write: false,
            storage, selectedZone);
        RequireLightingZone(connection, zone, capability => capability.CanReadBrightness, "brightness read");
        if (connection.Definition.ProductId == LeviathanV2XProtocol.ProductId)
        {
            var region = await GetLeviathanRegionAsync(connection, cancellationToken).ConfigureAwait(false);
            var leviathanRequest = RazerHidStandardProtocol.Create(connection.Definition,
                "razer_chroma_extended_matrix_get_brightness", 0x03, 0x0F, 0x84,
                [(byte)RazerHidStorage.Temporary, region]);
            var leviathanArguments = RazerHidStandardProtocol.Arguments(await ExecuteAsync(connection,
                leviathanRequest, cancellationToken).ConfigureAwait(false), 3);
            return leviathanArguments[2];
        }

        const string extendedBrightnessRead = "razer_chroma_extended_matrix_get_brightness";
        var request = RazerHidLightingProtocol.GetBrightness(connection.Definition, storage, zone);
        byte[] response;
        try
        {
            response = await ExecuteAsync(connection, request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (
            (exception is NotSupportedException or RazerDeviceTimeoutException) &&
            zone == RazerHidLedZone.All &&
            request.BuilderName == extendedBrightnessRead)
        {
            // Some firmware answers the all-LED selector with busy/silent; a concrete zone still answers.
            var fallback = connection.LightingZones.Values
                .Where(item => item.Zone != RazerHidLedZone.All && item.CanReadBrightness)
                .MinBy(item => item.Zone);
            if (fallback is null) throw;
            request = RazerHidLightingProtocol.GetBrightness(connection.Definition, storage, fallback.Zone);
            response = await ExecuteAsync(connection, request, cancellationToken).ConfigureAwait(false);
        }
        var arguments = RazerHidStandardProtocol.Arguments(response,
            request.BuilderName == "razer_chroma_misc_get_dock_brightness" ? (byte)1 : (byte)2);
        return request.BuilderName switch
        {
            "razer_chroma_misc_get_dock_brightness" => arguments[0],
            "razer_chroma_misc_get_blade_brightness" => arguments[1],
            _ => arguments[2],
        };
    }

    public async Task SetBrightnessAsync(
        RazerHidDeviceConnection connection,
        byte brightness,
        RazerHidStorage? storage = null,
        RazerHidLedZone? ledId = null,
        CancellationToken cancellationToken = default)
    {
        var selectedZone = ledId ?? (storage is null || storage == connection.Definition.DefaultStorage
            ? GetDefaultBrightnessZone(connection, write: true) : null);
        var zone = RazerHidLightingProtocol.ResolveBrightnessZone(connection.Definition, write: true,
            storage, selectedZone);
        RequireLightingZone(connection, zone, capability => capability.CanWriteBrightness, "brightness write");
        if (connection.Definition.ProductId == LeviathanV2XProtocol.ProductId)
        {
            var region = await GetLeviathanRegionAsync(connection, cancellationToken).ConfigureAwait(false);
            await ExecuteWithoutResultAsync(connection, RazerHidStandardProtocol.Create(connection.Definition,
                "razer_chroma_extended_matrix_brightness", 0x03, 0x0F, 0x04,
                [(byte)RazerHidStorage.Temporary, region, brightness]), cancellationToken).ConfigureAwait(false);
            return;
        }

        await ExecuteWithoutResultAsync(connection,
            RazerHidLightingProtocol.SetBrightness(connection.Definition, storage, zone, brightness), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task SetLightingAsync(
        RazerHidDeviceConnection connection,
        RazerHidLightingSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (connection.Definition.ProductId == LeviathanV2XProtocol.ProductId)
        {
            // The soundbar's effect write needs the region it reports, not the enum's All
            // placeholder, so it bypasses the RazerHid zone resolution below.
            var region = await GetLeviathanRegionAsync(connection, cancellationToken).ConfigureAwait(false);
            await ExecuteWithoutResultAsync(connection,
                new RazerHidRequest("razer_chroma_extended_matrix_effect_leviathan",
                    LeviathanV2XProtocol.CreateEffect(region, settings.Effect, settings.Direction,
                        settings.Primary, settings.Secondary)),
                cancellationToken).ConfigureAwait(false);
            return;
        }

        var zone = RazerHidLightingProtocol.ResolveEffectZone(
            connection.Definition, settings.Effect, settings.Storage, settings.Zone);
        RequireLightingZone(connection, zone,
            capability => capability.LightingEffects.Contains(settings.Effect), "lighting effect");
        foreach (var request in RazerHidLightingProtocol.CreateEffectRequests(connection.Definition, settings))
        {
            await ExecuteWithoutResultAsync(connection, request, cancellationToken).ConfigureAwait(false);
        }
    }

    public Task SetCustomRowAsync(
        RazerHidDeviceConnection connection,
        byte row,
        byte startColumn,
        IReadOnlyList<RazerHidColor> colors,
        CancellationToken cancellationToken = default) =>
        ExecuteWithoutResultAsync(connection,
            RazerHidLightingProtocol.SetCustomRow(connection.Definition, row, startColumn, colors), cancellationToken);

    public async Task SetCustomFrameAsync(
        RazerHidDeviceConnection connection,
        IReadOnlyList<RazerHidColor> frame,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var dimensions = connection.Definition.MatrixDimensions ??
            throw new NotSupportedException("The RazerHid device does not expose matrix dimensions.");
        if (!connection.Capabilities.Contains(RazerHidBackendCapability.MatrixFrameWrite) ||
            frame.Count != dimensions.Rows * dimensions.Columns)
        {
            throw new NotSupportedException(
                $"RazerHid device 1532:{connection.Definition.ProductId:X4} does not support this matrix frame.");
        }

        for (var row = 0; row < dimensions.Rows; row++)
        {
            await SetCustomRowAsync(
                connection,
                checked((byte)row),
                0,
                frame.Skip(row * dimensions.Columns).Take(dimensions.Columns).ToArray(),
                cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Matrix rows only display while the device is on its custom-frame effect.
    /// A firmware effect otherwise keeps running and the row writes are ignored.
    /// </summary>
    public async Task SetCustomFrameEffectAsync(
        RazerHidDeviceConnection connection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (!connection.Definition.HasSourceCapability("set_custom_effect") ||
            !connection.LightingZones.Values.Any(zone =>
                zone.LightingEffects.Contains(RazerHidLightingEffect.Custom)))
        {
            return;
        }

        await SetLightingAsync(
            connection,
            new RazerHidLightingSettings(RazerHidLightingEffect.Custom),
            cancellationToken).ConfigureAwait(false);
    }


    public Task SetLedStateAsync(RazerHidDeviceConnection connection, RazerHidStorage storage, RazerHidLedZone ledId, bool enabled, CancellationToken cancellationToken = default)
    {
        RequireLightingZone(connection, ledId, capability => capability.CanWriteState, "LED state write");
        if (!SupportsStorageAndZone(connection.Definition,
            "razer_chroma_standard_set_led_state", storage, ledId))
        {
            throw new NotSupportedException(
                $"RazerHid device 1532:{connection.Definition.ProductId:X4} does not support LED state storage {storage} on zone {ledId}.");
        }
        return ExecuteWithoutResultAsync(connection,
            RazerHidStandardProtocol.SetLedState(connection.Definition, (byte)storage, (byte)ledId, enabled), cancellationToken);
    }

    public async Task<bool> GetLedStateAsync(
        RazerHidDeviceConnection connection,
        RazerHidStorage storage,
        RazerHidLedZone ledId,
        CancellationToken cancellationToken = default)
    {
        RequireClassicLedOperation(connection, storage, ledId,
            capability => capability.CanReadState, "LED state read",
            "razer_chroma_standard_get_led_state");
        return RazerHidStandardProtocol.ParseLedState(await ExecuteAsync(connection,
            RazerHidStandardProtocol.GetLedState(connection.Definition, (byte)storage, (byte)ledId),
            cancellationToken).ConfigureAwait(false), (byte)storage, (byte)ledId);
    }

    public async Task<RazerHidLightingEffect?> GetLedEffectAsync(
        RazerHidDeviceConnection connection,
        RazerHidStorage storage,
        RazerHidLedZone ledId,
        CancellationToken cancellationToken = default)
    {
        if (connection.Definition.ProductId == LeviathanV2XProtocol.ProductId)
        {
            var region = await GetLeviathanRegionAsync(connection, cancellationToken).ConfigureAwait(false);
            var effect = LeviathanV2XProtocol.ReadEffect(await ExecuteAsync(connection,
                new RazerHidRequest("razer_chroma_extended_matrix_effect_leviathan_read",
                    LeviathanV2XProtocol.CreateGetEffect(LeviathanV2XProtocol.ReadProfile, region)),
                cancellationToken).ConfigureAwait(false), out _, out _);
            return effect switch
            {
                0x00 => RazerHidLightingEffect.Off,
                0x01 => RazerHidLightingEffect.Static,
                0x02 => RazerHidLightingEffect.BreathingDual,
                0x03 => RazerHidLightingEffect.Spectrum,
                0x04 => RazerHidLightingEffect.Wave,
                _ => null,
            };
        }

        RequireClassicLedOperation(connection, storage, ledId,
            capability => capability.CanReadEffect, "classic LED effect read",
            "razer_chroma_standard_get_led_effect");
        return RazerHidStandardProtocol.ParseLedEffect(await ExecuteAsync(connection,
            RazerHidStandardProtocol.GetLedEffect(connection.Definition, (byte)storage, (byte)ledId),
            cancellationToken).ConfigureAwait(false), (byte)storage, (byte)ledId) switch
        {
            RazerHidClassicLedEffect.Static => RazerHidLightingEffect.Static,
            RazerHidClassicLedEffect.Blinking => RazerHidLightingEffect.Blinking,
            RazerHidClassicLedEffect.Breathing => RazerHidLightingEffect.BreathingSingle,
            RazerHidClassicLedEffect.Spectrum => RazerHidLightingEffect.Spectrum,
            _ => null,
        };
    }

    public Task SetLedEffectAsync(
        RazerHidDeviceConnection connection,
        RazerHidStorage storage,
        RazerHidLedZone ledId,
        RazerHidClassicLedEffect effect,
        CancellationToken cancellationToken = default)
    {
        RequireClassicLedOperation(connection, storage, ledId,
            capability => capability.CanWriteEffect, "classic LED effect write",
            "razer_chroma_standard_set_led_effect");
        return ExecuteWithoutResultAsync(connection,
            RazerHidStandardProtocol.SetLedEffect(connection.Definition, (byte)storage, (byte)ledId, effect),
            cancellationToken);
    }

    public async Task<RazerHidColor> GetLedColorAsync(
        RazerHidDeviceConnection connection,
        RazerHidStorage storage,
        RazerHidLedZone ledId,
        CancellationToken cancellationToken = default)
    {
        RequireClassicLedOperation(connection, storage, ledId,
            capability => capability.CanReadColor, "LED color read",
            "razer_chroma_standard_get_led_rgb");
        return RazerHidStandardProtocol.ParseLedColor(await ExecuteAsync(connection,
            RazerHidStandardProtocol.GetLedColor(connection.Definition, (byte)storage, (byte)ledId),
            cancellationToken).ConfigureAwait(false), (byte)storage, (byte)ledId);
    }

    public Task SetLedColorAsync(
        RazerHidDeviceConnection connection,
        RazerHidStorage storage,
        RazerHidLedZone ledId,
        RazerHidColor color,
        CancellationToken cancellationToken = default)
    {
        RequireClassicLedOperation(connection, storage, ledId,
            capability => capability.CanWriteColor, "LED color write",
            "razer_chroma_standard_set_led_rgb");
        return ExecuteWithoutResultAsync(connection,
            RazerHidStandardProtocol.SetLedColor(connection.Definition, (byte)storage, (byte)ledId, color),
            cancellationToken);
    }

    public Task SetLedBlinkingAsync(
        RazerHidDeviceConnection connection,
        RazerHidStorage storage,
        RazerHidLedZone ledId,
        CancellationToken cancellationToken = default)
    {
        RequireClassicLedOperation(connection, storage, ledId,
            capability => capability.CanWriteBlinking, "LED blinking write",
            "razer_chroma_standard_set_led_blinking");
        return ExecuteWithoutResultAsync(connection,
            RazerHidStandardProtocol.SetLedBlinking(connection.Definition, (byte)storage, (byte)ledId),
            cancellationToken);
    }

    public Task TriggerReactiveAsync(RazerHidDeviceConnection connection, CancellationToken cancellationToken = default) =>
        ExecuteWithoutResultAsync(connection, RazerHidStandardProtocol.Create(connection.Definition,
            "razer_chroma_misc_matrix_reactive_trigger", 0x05, 0x03, 0x0A,
            [0x02, 0x00, 0x00, 0x00, 0x00]), cancellationToken);

    private static void RequireLightingZone(
        RazerHidDeviceConnection connection,
        RazerHidLedZone zone,
        Func<RazerHidLightingZoneCapabilities, bool> predicate,
        string operation)
    {
        RequireReady(connection);
        if (!connection.LightingZones.TryGetValue(zone, out var capabilities) || !predicate(capabilities))
        {
            throw new NotSupportedException(
                $"RazerHid device 1532:{connection.Definition.ProductId:X4} does not support {operation} on zone {zone}.");
        }
    }

    private static RazerHidLedZone GetDefaultBrightnessZone(RazerHidDeviceConnection connection, bool write)
    {
        var defaultZone = connection.Definition.DefaultLedZone;
        bool Supports(RazerHidLightingZoneCapabilities capabilities) =>
            write ? capabilities.CanWriteBrightness : capabilities.CanReadBrightness;
        if (connection.LightingZones.TryGetValue(defaultZone, out var capabilities) && Supports(capabilities))
        {
            return defaultZone;
        }
        return connection.LightingZones.Values.Where(Supports).Select(item => item.Zone)
            .Order().FirstOrDefault(defaultZone);
    }

    private async Task<byte> GetLeviathanRegionAsync(
        RazerHidDeviceConnection connection,
        CancellationToken cancellationToken)
    {
        var request = RazerHidStandardProtocol.Create(connection.Definition,
            "razer_chroma_extended_matrix_get_region_ids", 0x50, 0x0F, 0x80, []);
        return LeviathanV2XProtocol.ReadFirstRegion(
            await ExecuteAsync(connection, request, cancellationToken).ConfigureAwait(false));
    }

    private static void RequireClassicLedOperation(
        RazerHidDeviceConnection connection,
        RazerHidStorage storage,
        RazerHidLedZone zone,
        Func<RazerHidLightingZoneCapabilities, bool> predicate,
        string operation,
        string builder)
    {
        RequireLightingZone(connection, zone, predicate, operation);
        if (!SupportsStorageAndZone(connection.Definition, builder, storage, zone))
        {
            throw new NotSupportedException(
                $"RazerHid device 1532:{connection.Definition.ProductId:X4} does not support {operation} with storage {storage} on zone {zone}.");
        }
    }
}
