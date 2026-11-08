using System.Globalization;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Core.Sensors;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed partial class RazerHidProfileApplier
{
    private async Task ApplyLightingAsync(
        DeviceProfileApplyContext context,
        RazerHidDeviceConnection connection,
        List<ProfileSettingApplyResult> results,
        CancellationToken cancellationToken)
    {
        if (!IsLightingEnabled(context))
        {
            await DisableLightingAsync(connection, results, cancellationToken);
            return;
        }

        foreach (var (zone, profile) in ResolveLightingProfiles(context, connection))
        {
            await ApplyZoneLightingAsync(connection, zone, profile, results, cancellationToken);
        }
    }

    private static bool IsLightingEnabled(DeviceProfileApplyContext context) =>
        !context.Document.Devices.TryGetValue(
            ProfileResolver.GetDeviceKey(context.Device), out var settings) ||
        settings.LightingEnabled != false;

    private static List<(RazerHidLedZone Zone, LightingProfile Profile)> ResolveLightingProfiles(
        DeviceProfileApplyContext context,
        RazerHidDeviceConnection connection)
    {
        var defaultZone = connection.Definition.DefaultLedZone;
        var defaultZoneKey = ((byte)defaultZone).ToString(CultureInfo.InvariantCulture);
        var keyNames = new HashSet<string>(context.Profile.LightingZones.Keys, StringComparer.OrdinalIgnoreCase);

        var power = context.PowerSource switch
        {
            PowerSourceKind.FullPowerAdapter => context.Document.PluggedIn,
            PowerSourceKind.LowPowerAdapter => context.Document.LowPowerAdapter,
            PowerSourceKind.Battery => context.Document.OnBattery,
            _ => null,
        };
        var deviceKey = ProfileResolver.GetDeviceKey(context.Device);
        if (power?.RazerHidLightingZones.TryGetValue(deviceKey, out var powerZones) == true)
        {
            foreach (var zoneKey in powerZones.Keys)
            {
                keyNames.Add(zoneKey);
            }
        }

        var legacy = ProfileResolver.ResolveRazerHidLighting(context.Document, context.Device, context.PowerSource);
        if (legacy is not null)
        {
            keyNames.Add(legacy.Parameters.TryGetValue("zone", out var rawZone) &&
                TryParseZone(rawZone, out _)
                    ? rawZone
                    : defaultZoneKey);
        }

        var profiles = new List<(RazerHidLedZone, LightingProfile)>();
        foreach (var zoneKey in keyNames)
        {
            if (!TryParseZone(zoneKey, out var zone))
            {
                continue;
            }

            var profile = ProfileResolver.ResolveRazerHidLighting(
                context.Document, context.Device, context.PowerSource, zoneKey, defaultZoneKey)
                ?? context.Profile.LightingZones.GetValueOrDefault(zoneKey);
            if (profile is not null)
            {
                profiles.Add((zone, profile));
            }
        }

        return profiles.OrderBy(item => item.Item1).ToList();
    }

    private async Task ApplyZoneLightingAsync(
        RazerHidDeviceConnection connection,
        RazerHidLedZone zone,
        LightingProfile profile,
        List<ProfileSettingApplyResult> results,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(profile.Effect))
        {
            return;
        }

        var key = $"lighting.{ZoneKey(zone)}";
        if (!TryParseEffect(profile.Effect, out var effect))
        {
            results.Add(new ProfileSettingApplyResult(
                key, ProfileSettingApplyStatus.Failed, $"Unknown lighting effect '{profile.Effect}'."));
            return;
        }

        connection.LightingZones.TryGetValue(zone, out var capabilities);
        var parts = new List<ProfileSettingApplyResult>();

        // Match the device page: effect, then brightness, then the zone state.
        // Some devices re-enable a zone when an effect is written.
        await ApplyLightingEffectAsync(
            connection, zone, capabilities, effect, profile, parts, key, cancellationToken);

        if (profile.Parameters.TryGetValue("brightness", out var rawBrightness))
        {
            if (!byte.TryParse(rawBrightness, NumberStyles.Integer, CultureInfo.InvariantCulture,
                out var brightness))
            {
                parts.Add(new ProfileSettingApplyResult(
                    $"{key}.brightness", ProfileSettingApplyStatus.Failed,
                    $"Invalid brightness '{rawBrightness}'."));
            }
            else
            {
                await ApplyLightingPartAsync(
                    parts, $"{key}.brightness",
                    canRead: capabilities?.CanReadBrightness == true,
                    canWrite: capabilities?.CanWriteBrightness == true,
                    token => _service.GetBrightnessAsync(
                        connection, connection.Definition.DefaultStorage, zone, token),
                    (value, token) => _service.SetBrightnessAsync(
                        connection, value, connection.Definition.DefaultStorage, zone, token),
                    brightness, (left, right) => left == right,
                    "The zone does not support brightness writes.", cancellationToken);
            }
        }

        if (profile.Parameters.TryGetValue("enabled", out var rawEnabled))
        {
            if (!bool.TryParse(rawEnabled, out var enabled))
            {
                parts.Add(new ProfileSettingApplyResult(
                    $"{key}.enabled", ProfileSettingApplyStatus.Failed,
                    $"Invalid LED state '{rawEnabled}'."));
            }
            else
            {
                await ApplyLightingPartAsync(
                    parts, $"{key}.enabled",
                    canRead: capabilities?.CanReadState == true,
                    canWrite: capabilities?.CanWriteState == true,
                    token => _service.GetLedStateAsync(
                        connection, connection.Definition.DefaultStorage, zone, token),
                    (value, token) => _service.SetLedStateAsync(
                        connection, connection.Definition.DefaultStorage, zone, value, token),
                    enabled, (left, right) => left == right,
                    "The zone does not support LED state writes.", cancellationToken);
            }
        }

        results.Add(AggregateLightingResult(key, parts));
    }

    private async Task ApplyLightingEffectAsync(
        RazerHidDeviceConnection connection,
        RazerHidLedZone zone,
        RazerHidLightingZoneCapabilities? capabilities,
        RazerHidLightingEffect effect,
        LightingProfile profile,
        List<ProfileSettingApplyResult> parts,
        string key,
        CancellationToken cancellationToken)
    {
        var effectKey = $"{key}.effect";
        if (capabilities is null || !capabilities.LightingEffects.Contains(effect))
        {
            parts.Add(new ProfileSettingApplyResult(
                effectKey, ProfileSettingApplyStatus.Skipped,
                capabilities is null
                    ? $"The device does not advertise lighting zone {ZoneKey(zone)}."
                    : $"The zone does not support the {effect} effect."));
            return;
        }

        var canRead = capabilities.CanReadEffect;
        if (canRead)
        {
            try
            {
                if (await LightingEffectMatchesAsync(
                    connection, zone, capabilities, effect, profile, cancellationToken))
                {
                    parts.Add(Applied(effectKey));
                    return;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // The effect may still be writable when its read is unavailable.
            }
        }

        try
        {
            await _service.SetLightingAsync(
                connection, CreateLightingSettings(profile, effect, zone), cancellationToken);
            if (!canRead)
            {
                parts.Add(new ProfileSettingApplyResult(
                    effectKey, ProfileSettingApplyStatus.AppliedUnverified));
                return;
            }

            var applied = await _service.GetLedEffectAsync(
                connection, connection.Definition.DefaultStorage, zone, cancellationToken);
            parts.Add(applied == effect
                ? Applied(effectKey)
                : applied is null
                    ? new ProfileSettingApplyResult(effectKey, ProfileSettingApplyStatus.AppliedUnverified)
                    : Mismatch(effectKey, "read-back did not match the requested effect"));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            parts.Add(Failed(effectKey, exception));
        }
    }

    private async Task<bool> LightingEffectMatchesAsync(
        RazerHidDeviceConnection connection,
        RazerHidLedZone zone,
        RazerHidLightingZoneCapabilities capabilities,
        RazerHidLightingEffect effect,
        LightingProfile profile,
        CancellationToken cancellationToken)
    {
        var current = await _service.GetLedEffectAsync(
            connection, connection.Definition.DefaultStorage, zone, cancellationToken);
        if (current != effect ||
            UsesSpeed(effect) && profile.Parameters.ContainsKey("speed") ||
            UsesDirection(effect) && profile.Parameters.ContainsKey("direction") ||
            UsesSecondaryColor(effect) && profile.Parameters.ContainsKey("color2") ||
            UsesTertiaryColor(effect) && profile.Parameters.ContainsKey("color3"))
        {
            return false;
        }

        if (!UsesPrimaryColor(effect) || !profile.Parameters.ContainsKey("color"))
        {
            return true;
        }
        if (!capabilities.CanReadColor)
        {
            return false;
        }

        return await _service.GetLedColorAsync(
            connection, connection.Definition.DefaultStorage, zone, cancellationToken) ==
            ParameterColor(profile, "color");
    }

    private async Task DisableLightingAsync(
        RazerHidDeviceConnection connection,
        List<ProfileSettingApplyResult> results,
        CancellationToken cancellationToken)
    {
        foreach (var zone in connection.LightingZones.Keys.Order())
        {
            var key = $"lighting.{ZoneKey(zone)}";
            var capabilities = connection.LightingZones[zone];
            try
            {
                if (capabilities.CanWriteState)
                {
                    await _service.SetLedStateAsync(
                        connection, connection.Definition.DefaultStorage, zone, false, cancellationToken);
                    results.Add(Applied(key));
                }
                else if (capabilities.LightingEffects.Contains(RazerHidLightingEffect.Off))
                {
                    await _service.SetLightingAsync(
                        connection,
                        new RazerHidLightingSettings(RazerHidLightingEffect.Off, Zone: zone),
                        cancellationToken);
                    results.Add(Applied(key));
                }
                else
                {
                    results.Add(new ProfileSettingApplyResult(
                        key, ProfileSettingApplyStatus.Skipped,
                        "The zone supports neither LED state nor the Off effect."));
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                results.Add(Failed(key, exception));
            }
        }
    }

    private static async Task ApplyLightingPartAsync<T>(
        List<ProfileSettingApplyResult> parts,
        string key,
        bool canRead,
        bool canWrite,
        Func<CancellationToken, Task<T>> readAsync,
        Func<T, CancellationToken, Task> writeAsync,
        T desired,
        Func<T, T, bool> equals,
        string unsupportedDetail,
        CancellationToken cancellationToken)
    {
        if (!canWrite)
        {
            parts.Add(new ProfileSettingApplyResult(key, ProfileSettingApplyStatus.Skipped, unsupportedDetail));
            return;
        }

        if (canRead)
        {
            try
            {
                if (equals(await readAsync(cancellationToken), desired))
                {
                    parts.Add(Applied(key));
                    return;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // The setting may still be writable when its read is unavailable.
            }
        }

        try
        {
            await writeAsync(desired, cancellationToken);
            if (!canRead)
            {
                parts.Add(new ProfileSettingApplyResult(key, ProfileSettingApplyStatus.AppliedUnverified));
                return;
            }

            var applied = await readAsync(cancellationToken);
            parts.Add(equals(applied, desired)
                ? Applied(key)
                : Mismatch(key, "read-back did not match the requested value"));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            parts.Add(Failed(key, exception));
        }
    }

    private static ProfileSettingApplyResult AggregateLightingResult(
        string key, List<ProfileSettingApplyResult> parts)
    {
        var failures = parts.Where(part => part.Status == ProfileSettingApplyStatus.Failed).ToArray();
        if (failures.Length > 0)
        {
            return new ProfileSettingApplyResult(
                key, ProfileSettingApplyStatus.Failed,
                string.Join(" ", failures.Select(part => part.Error).Where(error => error is not null)));
        }
        if (parts.Any(part => part.Status == ProfileSettingApplyStatus.Applied))
        {
            return Applied(key);
        }
        if (parts.Any(part => part.Status == ProfileSettingApplyStatus.AppliedUnverified))
        {
            return new ProfileSettingApplyResult(key, ProfileSettingApplyStatus.AppliedUnverified);
        }
        return new ProfileSettingApplyResult(
            key, ProfileSettingApplyStatus.Skipped,
            string.Join(" ", parts.Select(part => part.Error).Where(error => error is not null)));
    }

    private static RazerHidLightingSettings CreateLightingSettings(
        LightingProfile profile, RazerHidLightingEffect effect, RazerHidLedZone zone) =>
        new(
            effect,
            ParameterByte(profile, "speed", 2),
            ParameterByte(profile, "direction", 1),
            ParameterColor(profile, "color"),
            ParameterColor(profile, "color2"),
            Storage: null,
            Zone: zone,
            ParameterColor(profile, "color3"));

    private static bool TryParseEffect(string value, out RazerHidLightingEffect effect) =>
        Enum.TryParse(value, ignoreCase: true, out effect) && Enum.IsDefined(effect);

    private static bool TryParseZone(string value, out RazerHidLedZone zone)
    {
        zone = default;
        if (!byte.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var raw) ||
            !Enum.IsDefined(typeof(RazerHidLedZone), raw))
        {
            return false;
        }

        zone = (RazerHidLedZone)raw;
        return true;
    }

    private static byte ParameterByte(LightingProfile profile, string key, byte fallback) =>
        profile.Parameters.TryGetValue(key, out var raw) &&
        byte.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;

    private static RazerHidColor ParameterColor(LightingProfile profile, string key) =>
        profile.Parameters.TryGetValue(key, out var raw) && TryParseColor(raw, out var color)
            ? color
            : default;

    private static bool TryParseColor(string? value, out RazerHidColor color)
    {
        color = default;
        if (value is null || value.Length != 6 ||
            !byte.TryParse(value[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var red) ||
            !byte.TryParse(value[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var green) ||
            !byte.TryParse(value[4..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var blue))
        {
            return false;
        }

        color = new RazerHidColor(red, green, blue);
        return true;
    }

    private static bool UsesPrimaryColor(RazerHidLightingEffect effect) => effect is
        RazerHidLightingEffect.Static or RazerHidLightingEffect.Reactive or
        RazerHidLightingEffect.Blinking or RazerHidLightingEffect.BreathingSingle or
        RazerHidLightingEffect.BreathingDual or RazerHidLightingEffect.BreathingTriple or
        RazerHidLightingEffect.StarlightSingle or RazerHidLightingEffect.StarlightDual;

    private static bool UsesSecondaryColor(RazerHidLightingEffect effect) => effect is
        RazerHidLightingEffect.BreathingDual or RazerHidLightingEffect.BreathingTriple or
        RazerHidLightingEffect.StarlightDual;

    private static bool UsesTertiaryColor(RazerHidLightingEffect effect) =>
        effect == RazerHidLightingEffect.BreathingTriple;

    private static bool UsesSpeed(RazerHidLightingEffect effect) => effect is
        RazerHidLightingEffect.Reactive or RazerHidLightingEffect.BreathingRandom or
        RazerHidLightingEffect.BreathingSingle or RazerHidLightingEffect.BreathingDual or
        RazerHidLightingEffect.BreathingTriple or RazerHidLightingEffect.StarlightRandom or
        RazerHidLightingEffect.StarlightSingle or RazerHidLightingEffect.StarlightDual;

    private static bool UsesDirection(RazerHidLightingEffect effect) =>
        effect is RazerHidLightingEffect.Wave or RazerHidLightingEffect.Wheel;

    private static string ZoneKey(RazerHidLedZone zone) =>
        ((byte)zone).ToString(CultureInfo.InvariantCulture);
}
