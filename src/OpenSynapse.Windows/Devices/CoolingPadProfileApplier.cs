using System.Globalization;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

/// <summary>
/// Applies the persisted cooling-pad fan and lighting settings through the same
/// 0D/0F commands the device page uses. Each setting is read first and skipped
/// when the device already reports the requested value.
/// </summary>
public sealed class CoolingPadProfileApplier : IDeviceProfileApplier
{
    private static readonly DeviceCategory[] SupportedCategories = [DeviceCategory.CoolingPad];
    private readonly Func<DeviceDescriptor, CancellationToken, Task<RazerCoolingPadService?>> _connect;

    public CoolingPadProfileApplier(Action<string>? diagnostic = null)
        : this((_, cancellationToken) => RazerCoolingPadService.ConnectAsync(diagnostic, cancellationToken))
    {
    }

    public CoolingPadProfileApplier(
        Func<DeviceDescriptor, CancellationToken, Task<RazerCoolingPadService?>> connect)
    {
        _connect = connect ?? throw new ArgumentNullException(nameof(connect));
    }

    public IReadOnlyCollection<DeviceCategory> Categories => SupportedCategories;

    public async Task<ProfileDeviceApplyResult> ApplyAsync(
        DeviceProfileApplyContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Device.Category != DeviceCategory.CoolingPad)
        {
            return new ProfileDeviceApplyResult(
                context.Device.Id, ProfileDeviceApplyStatus.Skipped, []);
        }

        var service = await _connect(context.Device, cancellationToken);
        if (service is null || service.ProductId != context.Device.ProductId)
        {
            return new ProfileDeviceApplyResult(
                context.Device.Id,
                ProfileDeviceApplyStatus.Failed,
                [new ProfileSettingApplyResult(
                    "device",
                    ProfileSettingApplyStatus.Failed,
                    "The cooling pad is not available.")]);
        }

        var results = new List<ProfileSettingApplyResult>();
        await ApplyFanAsync(context.Profile.CoolingPad, service, results, cancellationToken);
        await ApplyLightingAsync(context.Profile.CoolingPad, service, results, cancellationToken);
        return new ProfileDeviceApplyResult(context.Device.Id, Status(results), results);
    }

    private static async Task ApplyFanAsync(
        CoolingPadProfileSettings profile,
        RazerCoolingPadService service,
        List<ProfileSettingApplyResult> results,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(profile.FanMode) && profile.FanTargetRpm is null)
        {
            return;
        }

        const string key = "coolingpad.fan";
        RazerCoolingPadCoolingMode? current = null;
        try
        {
            current = await service.ReadCoolingModeAsync(cancellationToken);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            // The write path retries the read itself; a read failure alone is not fatal.
        }

        try
        {
            if (string.Equals(profile.FanMode, "Manual", StringComparison.OrdinalIgnoreCase) ||
                (string.IsNullOrWhiteSpace(profile.FanMode) && profile.FanTargetRpm is not null))
            {
                var target = profile.FanTargetRpm ?? current?.Rpm ?? 0;
                if (target == 0)
                {
                    results.Add(new(key, ProfileSettingApplyStatus.Failed,
                        "A manual fan target is required."));
                    return;
                }

                if (current is { ModeId: <= 2 or 6 } mode &&
                    await service.ReadSpeedRpmAsync(cancellationToken) == target)
                {
                    results.Add(new(key, ProfileSettingApplyStatus.Skipped));
                    return;
                }

                await service.SetSpeedRpmAsync(target, cancellationToken);
                results.Add(new(key, ProfileSettingApplyStatus.Applied));
                return;
            }

            if (Enum.TryParse<RazerCoolingPadFixedMode>(profile.FanMode, true, out var fixedMode) &&
                Enum.IsDefined(fixedMode))
            {
                if (current is { } fixedState &&
                    fixedState.ModeId == (byte)fixedMode &&
                    (fixedMode == RazerCoolingPadFixedMode.Off ||
                     fixedState.SpeedUnits == RazerCoolingPadProtocol.UnitsFor(fixedMode)))
                {
                    results.Add(new(key, ProfileSettingApplyStatus.Skipped));
                    return;
                }

                await service.SetFixedModeAsync(fixedMode, cancellationToken);
                results.Add(new(key, ProfileSettingApplyStatus.Applied));
                return;
            }

            if (Enum.TryParse<RazerCoolingPadSmartMode>(profile.FanMode, true, out var smartMode) &&
                Enum.IsDefined(smartMode))
            {
                if (current is { } smartState && smartState.ModeId == (byte)smartMode)
                {
                    results.Add(new(key, ProfileSettingApplyStatus.Skipped));
                    return;
                }

                await service.SetSmartModeAsync(smartMode, cancellationToken);
                results.Add(new(key, ProfileSettingApplyStatus.Applied));
                return;
            }

            results.Add(new(
                key,
                ProfileSettingApplyStatus.Failed,
                $"Fan mode '{profile.FanMode}' is not recognized."));
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            results.Add(new(key, ProfileSettingApplyStatus.Failed, exception.Message));
        }
    }

    private static async Task ApplyLightingAsync(
        CoolingPadProfileSettings profile,
        RazerCoolingPadService service,
        List<ProfileSettingApplyResult> results,
        CancellationToken cancellationToken)
    {
        if (profile.LightingEnabled == false || string.IsNullOrWhiteSpace(profile.LightingEffect))
        {
            return;
        }

        const string key = "coolingpad.lighting";
        if (!TryReadLighting(profile, out var desired, out var error))
        {
            results.Add(new(key, ProfileSettingApplyStatus.Failed, error));
            return;
        }

        try
        {
            var current = await service.ReadLightingAsync(cancellationToken);
            if (current is not null &&
                current.BrightnessPercent == desired.Brightness &&
                current.EffectId == desired.EffectId &&
                current.Flags == desired.Flags &&
                current.Rate == desired.Rate &&
                current.Colors.SequenceEqual(desired.Colors))
            {
                results.Add(new(key, ProfileSettingApplyStatus.Skipped));
                return;
            }

            await service.SetLightingAsync(
                desired.Brightness,
                desired.EffectId,
                desired.Flags,
                desired.Rate,
                desired.Colors,
                cancellationToken);
            results.Add(new(key, ProfileSettingApplyStatus.Applied));
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            results.Add(new(key, ProfileSettingApplyStatus.Failed, exception.Message));
        }
    }

    private static bool TryReadLighting(
        CoolingPadProfileSettings profile,
        out LightingValues values,
        out string error)
    {
        values = default;
        error = string.Empty;
        if (!byte.TryParse(
                profile.LightingEffect,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var effectId))
        {
            error = $"Lighting effect '{profile.LightingEffect}' is not a byte.";
            return false;
        }

        var parameters = profile.LightingParameters;
        if (!TryReadByte(parameters, "brightness", 0, 100, out var brightness, out error) ||
            !TryReadByte(parameters, "flags", 0, byte.MaxValue, out var flags, out error) ||
            !TryReadByte(parameters, "rate", 0, byte.MaxValue, out var rate, out error))
        {
            return false;
        }

        if (!TryReadColors(parameters, out var colors, out error))
        {
            return false;
        }

        values = new LightingValues(brightness, effectId, flags, rate, colors);
        return true;
    }

    private static bool TryReadByte(
        Dictionary<string, string>? parameters,
        string name,
        int minimum,
        int maximum,
        out byte value,
        out string error)
    {
        value = 0;
        error = string.Empty;
        if (parameters is not null &&
            parameters.TryGetValue(name, out var text) &&
            int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) &&
            parsed >= minimum && parsed <= maximum)
        {
            value = (byte)parsed;
            return true;
        }

        error = $"Lighting parameter '{name}' must be {minimum}-{maximum}.";
        return false;
    }

    private static bool TryReadColors(
        Dictionary<string, string>? parameters,
        out byte[] colors,
        out string error)
    {
        colors = [];
        error = string.Empty;
        if (parameters is null || !parameters.TryGetValue("colors", out var text) ||
            string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        var parts = text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var parsed = new byte[parts.Length];
        for (var index = 0; index < parts.Length; index++)
        {
            if (!byte.TryParse(parts[index], NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out parsed[index]))
            {
                error = "Lighting colors must be comma-separated bytes.";
                return false;
            }
        }

        if (parsed.Length % 3 != 0)
        {
            error = "Lighting colors must contain RGB triples.";
            return false;
        }

        colors = parsed;
        return true;
    }

    private static ProfileDeviceApplyStatus Status(List<ProfileSettingApplyResult> results) =>
        results.Any(item => item.Status == ProfileSettingApplyStatus.Failed)
            ? ProfileDeviceApplyStatus.Failed
            : results.Any(item => item.Status is ProfileSettingApplyStatus.Applied or
                ProfileSettingApplyStatus.AppliedUnverified)
                ? ProfileDeviceApplyStatus.Applied
                : ProfileDeviceApplyStatus.Skipped;

    private static bool IsExpected(Exception exception) =>
        exception is IOException or InvalidDataException or InvalidOperationException or
            NotSupportedException or UnauthorizedAccessException or ArgumentException or
            System.ComponentModel.Win32Exception;

    private readonly record struct LightingValues(
        byte Brightness,
        byte EffectId,
        byte Flags,
        byte Rate,
        byte[] Colors);
}
