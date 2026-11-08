using System.Globalization;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

/// <summary>
/// Applies the persisted gamepad settings through the same protocol-25 commands
/// the device page uses. Hardware reads gate every write, so an already-matching
/// value is skipped and an unsupported row is reported instead of guessed.
/// </summary>
public sealed partial class GamepadProfileApplier : IDeviceProfileApplier
{
    private static readonly DeviceCategory[] SupportedCategories = [DeviceCategory.Gamepad];
    private readonly Func<DeviceDescriptor, CancellationToken, Task<RazerGamepadService?>> _connect;

    public GamepadProfileApplier(Action<string>? diagnostic = null)
        : this((device, cancellationToken) => ConnectAsync(device, diagnostic, cancellationToken))
    {
    }

    public GamepadProfileApplier(
        Func<DeviceDescriptor, CancellationToken, Task<RazerGamepadService?>> connect)
    {
        _connect = connect ?? throw new ArgumentNullException(nameof(connect));
    }

    public IReadOnlyCollection<DeviceCategory> Categories => SupportedCategories;

    public async Task<ProfileDeviceApplyResult> ApplyAsync(
        DeviceProfileApplyContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Device.Category != DeviceCategory.Gamepad)
        {
            return new ProfileDeviceApplyResult(
                context.Device.Id, ProfileDeviceApplyStatus.Skipped, []);
        }

        var service = await _connect(context.Device, cancellationToken);
        if (service is null)
        {
            return new ProfileDeviceApplyResult(
                context.Device.Id,
                ProfileDeviceApplyStatus.Failed,
                [new ProfileSettingApplyResult(
                    "device",
                    ProfileSettingApplyStatus.Failed,
                    "The gamepad is not available.")]);
        }

        var profile = context.Profile.Gamepad;
        var results = new List<ProfileSettingApplyResult>();
        RazerGamepadState? state = null;
        try
        {
            state = await service.ReadStateAsync(cancellationToken);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            results.Add(new("device", ProfileSettingApplyStatus.Failed, exception.Message));
        }

        await ApplyPollingAsync(profile, service, results, cancellationToken);
        await ApplyButtonAssignmentsAsync(profile, service, results, cancellationToken);
        if (state is not null)
        {
            await ApplySticksAsync(profile, service, state, results, cancellationToken);
            await ApplyTriggersAsync(profile, service, state, results, cancellationToken);
            await ApplyDpadAsync(profile, service, state, results, cancellationToken);
            await ApplyVibrationAsync(profile, service, state, results, cancellationToken);
        }

        await ApplyLightingAsync(profile, service, results, cancellationToken);
        return new ProfileDeviceApplyResult(context.Device.Id, Status(results), results);
    }

    private static async Task ApplyPollingAsync(
        GamepadProfileSettings profile,
        RazerGamepadService service,
        List<ProfileSettingApplyResult> results,
        CancellationToken cancellationToken)
    {
        if (profile.PollingRateHertz is not { } hertz)
        {
            return;
        }

        const string key = "gamepad.polling-rate";
        try
        {
            var current = await service.ReadPollingProfileAsync(cancellationToken);
            if (current.PollingRateHertz == hertz)
            {
                results.Add(new(key, ProfileSettingApplyStatus.Skipped));
                return;
            }

            await service.SetPollingRateAsync(hertz, cancellationToken);
            results.Add(new(key, ProfileSettingApplyStatus.Applied));
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            results.Add(new(key, ProfileSettingApplyStatus.Failed, exception.Message));
        }
    }

    private static async Task ApplyTriggersAsync(
        GamepadProfileSettings profile,
        RazerGamepadService service,
        RazerGamepadState state,
        List<ProfileSettingApplyResult> results,
        CancellationToken cancellationToken)
    {
        await ApplyTriggerAsync(
            1,
            "left",
            profile.LeftTrigger,
            profile.TriggerMode,
            service,
            state,
            results,
            cancellationToken);
        await ApplyTriggerAsync(
            2,
            "right",
            profile.RightTrigger,
            profile.TriggerMode,
            service,
            state,
            results,
            cancellationToken);
    }

    private static async Task ApplyTriggerAsync(
        byte controlId,
        string label,
        GamepadTriggerProfileSettings? settings,
        string? legacyMode,
        RazerGamepadService service,
        RazerGamepadState state,
        List<ProfileSettingApplyResult> results,
        CancellationToken cancellationToken)
    {
        var modeText = settings?.Mode ?? legacyMode;
        if (string.IsNullOrWhiteSpace(modeText) && settings is null)
        {
            return;
        }

        var trigger = state.Triggers.FirstOrDefault(item => item.ControlId == controlId);
        RazerGamepadTriggerMode? requestedMode = null;
        if (!string.IsNullOrWhiteSpace(modeText))
        {
            var modeKey = $"gamepad.{label}-trigger-mode";
            if (!TryParseTriggerMode(modeText, out var mode))
            {
                results.Add(new(
                    modeKey,
                    ProfileSettingApplyStatus.Failed,
                    $"Trigger mode '{modeText}' is not recognized."));
                return;
            }

            requestedMode = mode;
            try
            {
                if (trigger?.Mode is null)
                {
                    results.Add(new(
                        modeKey,
                        ProfileSettingApplyStatus.Skipped,
                        "The device did not report trigger mode."));
                }
                else if (trigger.Mode == mode)
                {
                    results.Add(new(modeKey, ProfileSettingApplyStatus.Skipped));
                }
                else
                {
                    await service.SetTriggerModeAsync(controlId, mode, cancellationToken);
                    results.Add(new(modeKey, ProfileSettingApplyStatus.Applied));
                }
            }
            catch (Exception exception) when (IsExpected(exception))
            {
                results.Add(new(modeKey, ProfileSettingApplyStatus.Failed, exception.Message));
            }
        }

        if (settings is null)
        {
            return;
        }

        var effectiveMode = requestedMode ?? trigger?.Mode;
        if (effectiveMode is null)
        {
            if (settings.StartPercent is not null || settings.EndPercent is not null ||
                settings.ActuationIndex is not null || settings.RapidTrigger is not null)
            {
                results.Add(new(
                    $"gamepad.{label}-trigger",
                    ProfileSettingApplyStatus.Skipped,
                    "The device did not report trigger mode or range."));
            }
            return;
        }

        if (effectiveMode == RazerGamepadTriggerMode.Analog &&
            (settings.StartPercent is not null || settings.EndPercent is not null))
        {
            var key = $"gamepad.{label}-trigger-range";
            if (trigger?.StartRange is not { } currentStart || trigger.EndRange is not { } currentEnd)
            {
                results.Add(new(
                    key,
                    ProfileSettingApplyStatus.Skipped,
                    "The device did not report trigger range."));
                return;
            }

            var desiredStart = (byte)Math.Clamp(settings.StartPercent ?? currentStart, 0, 100);
            var desiredEnd = (byte)Math.Clamp(settings.EndPercent ?? currentEnd, 0, 100);
            try
            {
                if (currentStart == desiredStart && currentEnd == desiredEnd)
                {
                    results.Add(new(key, ProfileSettingApplyStatus.Skipped));
                }
                else
                {
                    await service.SetTriggerRangeAsync(
                        controlId,
                        desiredStart,
                        desiredEnd,
                        cancellationToken);
                    results.Add(new(key, ProfileSettingApplyStatus.Applied));
                }
            }
            catch (Exception exception) when (IsExpected(exception))
            {
                results.Add(new(key, ProfileSettingApplyStatus.Failed, exception.Message));
            }
        }

        if (effectiveMode == RazerGamepadTriggerMode.Digital &&
            (settings.ActuationIndex is not null || settings.RapidTrigger is not null))
        {
            var key = $"gamepad.{label}-trigger-digital";
            if (trigger?.Digital is not { } currentDigital)
            {
                results.Add(new(
                    key,
                    ProfileSettingApplyStatus.Skipped,
                    "The device did not report digital trigger settings."));
                return;
            }

            var desired = new RazerGamepadDigitalTrigger(
                (byte)Math.Clamp(settings.ActuationIndex ?? currentDigital.ActuationPoint, 0, 6),
                settings.RapidTrigger ?? currentDigital.IsRapidTrigger);
            try
            {
                if (currentDigital == desired)
                {
                    results.Add(new(key, ProfileSettingApplyStatus.Skipped));
                }
                else
                {
                    await service.SetDigitalTriggerAsync(controlId, desired, cancellationToken);
                    results.Add(new(key, ProfileSettingApplyStatus.Applied));
                }
            }
            catch (Exception exception) when (IsExpected(exception))
            {
                results.Add(new(key, ProfileSettingApplyStatus.Failed, exception.Message));
            }
        }
    }

    private static async Task ApplyDpadAsync(
        GamepadProfileSettings profile,
        RazerGamepadService service,
        RazerGamepadState state,
        List<ProfileSettingApplyResult> results,
        CancellationToken cancellationToken)
    {
        if (profile.IgnoreDpadDiagonals is { } ignore)
        {
            const string key = "gamepad.ignore-dpad-diagonals";
            try
            {
                if (state.IgnoreDPadDiagonals is null)
                {
                    results.Add(new(key, ProfileSettingApplyStatus.Skipped,
                        "The device did not report D-pad diagonal handling."));
                }
                else if (state.IgnoreDPadDiagonals == ignore)
                {
                    results.Add(new(key, ProfileSettingApplyStatus.Skipped));
                }
                else
                {
                    await service.SetIgnoreDPadDiagonalsAsync(ignore, cancellationToken);
                    results.Add(new(key, ProfileSettingApplyStatus.Applied));
                }
            }
            catch (Exception exception) when (IsExpected(exception))
            {
                results.Add(new(key, ProfileSettingApplyStatus.Failed, exception.Message));
            }
        }

        if (profile.SocdModeIndex is { } socdIndex)
        {
            const string key = "gamepad.socd";
            try
            {
                if (state.SocdMode is null)
                {
                    results.Add(new(key, ProfileSettingApplyStatus.Skipped,
                        "The device did not report SOCD handling."));
                }
                else if ((int)state.SocdMode == socdIndex)
                {
                    results.Add(new(key, ProfileSettingApplyStatus.Skipped));
                }
                else
                {
                    await service.SetSocdAsync((RazerGamepadSocdMode)socdIndex, cancellationToken);
                    results.Add(new(key, ProfileSettingApplyStatus.Applied));
                }
            }
            catch (Exception exception) when (IsExpected(exception))
            {
                results.Add(new(key, ProfileSettingApplyStatus.Failed, exception.Message));
            }
        }
    }

    private static async Task ApplyVibrationAsync(
        GamepadProfileSettings profile,
        RazerGamepadService service,
        RazerGamepadState state,
        List<ProfileSettingApplyResult> results,
        CancellationToken cancellationToken)
    {
        if (profile.VibrationStrengthPercent is not { } percent)
        {
            return;
        }

        const string key = "gamepad.vibration";
        try
        {
            if (state.VibrationScale is null)
            {
                results.Add(new(key, ProfileSettingApplyStatus.Skipped,
                    "The device did not report vibration strength."));
                return;
            }

            var scale = (byte)Math.Clamp(percent, 0, 100);
            if (state.VibrationScale == scale)
            {
                results.Add(new(key, ProfileSettingApplyStatus.Skipped));
                return;
            }

            await service.SetVibrationScaleAsync(scale, cancellationToken);
            results.Add(new(key, ProfileSettingApplyStatus.Applied));
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            results.Add(new(key, ProfileSettingApplyStatus.Failed, exception.Message));
        }
    }

    private static async Task ApplyLightingAsync(
        GamepadProfileSettings profile,
        RazerGamepadService service,
        List<ProfileSettingApplyResult> results,
        CancellationToken cancellationToken)
    {
        if (profile.LightingEnabled == false || string.IsNullOrWhiteSpace(profile.LightingEffect))
        {
            return;
        }

        const string key = "gamepad.lighting";
        if (!TryReadLighting(profile, out var desired, out var error))
        {
            results.Add(new(key, ProfileSettingApplyStatus.Failed, error));
            return;
        }

        try
        {
            var current = await service.ReadLightingAsync(cancellationToken);
            if (current.Regions.Count == 0)
            {
                results.Add(new(key, ProfileSettingApplyStatus.Skipped,
                    "The device did not report a lighting region."));
                return;
            }

            var region = current.Regions.FirstOrDefault(
                             item => item.Region.RegionId == desired.RegionId) ??
                         current.Regions[0];
            var brightnessMatches = desired.Brightness is null ||
                region.Brightness == ToRawBrightness(desired.Brightness.Value);
            var colorsMatch = desired.Colors.Count == 0 ||
                (region.Effect is { } currentEffect &&
                 currentEffect.Colors.SequenceEqual(desired.Colors));
            if (region.Effect?.EffectId == desired.EffectId &&
                region.Effect.Flags == desired.Flags &&
                region.Effect.Rate == desired.Rate &&
                brightnessMatches &&
                colorsMatch)
            {
                results.Add(new(key, ProfileSettingApplyStatus.Skipped));
                return;
            }

            await service.SetLightingEffectAsync(
                region.Region.RegionId,
                desired.EffectId,
                desired.Flags,
                desired.Rate,
                desired.Colors,
                cancellationToken);
            if (desired.Brightness is { } brightness && region.Brightness is not null)
            {
                await service.SetLightingBrightnessAsync(
                    region.Region.RegionId,
                    brightness,
                    cancellationToken);
            }

            results.Add(new(key, ProfileSettingApplyStatus.Applied));
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            results.Add(new(key, ProfileSettingApplyStatus.Failed, exception.Message));
        }
    }

    private static bool TryReadLighting(
        GamepadProfileSettings profile,
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
        if (!TryReadByte(parameters, "flags", out var flags, out error) ||
            !TryReadByte(parameters, "rate", out var rate, out error) ||
            !TryReadByte(parameters, "regionId", out var regionId, out error))
        {
            return false;
        }

        byte? brightness = null;
        if (parameters?.TryGetValue("brightness", out var brightnessText) == true)
        {
            if (!byte.TryParse(
                    brightnessText,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var parsed) || parsed > 100)
            {
                error = "Lighting brightness must be 0-100.";
                return false;
            }

            brightness = parsed;
        }

        if (!TryReadColors(parameters, out var colors, out error))
        {
            return false;
        }

        values = new LightingValues(regionId, effectId, flags, rate, brightness, colors);
        return true;
    }

    private static bool TryReadByte(
        Dictionary<string, string>? parameters,
        string name,
        out byte value,
        out string error)
    {
        value = 0;
        error = string.Empty;
        if (parameters is not null &&
            parameters.TryGetValue(name, out var text) &&
            byte.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        error = $"Lighting parameter '{name}' is missing or invalid.";
        return false;
    }

    private static bool TryReadColors(
        Dictionary<string, string>? parameters,
        out IReadOnlyList<RazerRgb> colors,
        out string error)
    {
        colors = [];
        error = string.Empty;
        if (parameters is null || !parameters.TryGetValue("color", out var text) ||
            string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        var parts = text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3 ||
            !byte.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var red) ||
            !byte.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var green) ||
            !byte.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var blue))
        {
            error = "Lighting color must be R,G,B bytes.";
            return false;
        }

        colors = [new RazerRgb(red, green, blue)];
        return true;
    }

    private static bool TryParseTriggerMode(string text, out RazerGamepadTriggerMode mode)
    {
        mode = RazerGamepadTriggerMode.Analog;
        return Enum.TryParse(text, true, out mode) && Enum.IsDefined(mode);
    }

    private static byte ToRawBrightness(byte percent) =>
        (byte)Math.Floor(percent / 100d * 255d);

    private static ProfileDeviceApplyStatus Status(List<ProfileSettingApplyResult> results) =>
        results.Any(item => item.Status == ProfileSettingApplyStatus.Failed)
            ? ProfileDeviceApplyStatus.Failed
            : results.Any(item => item.Status is ProfileSettingApplyStatus.Applied or
                ProfileSettingApplyStatus.AppliedUnverified)
                ? ProfileDeviceApplyStatus.Applied
                : ProfileDeviceApplyStatus.Skipped;

    private static async Task<RazerGamepadService?> ConnectAsync(
        DeviceDescriptor device,
        Action<string>? diagnostic,
        CancellationToken cancellationToken)
    {
        var connection = await RazerGamepadService.FindAsync(diagnostic, cancellationToken);
        return connection is null || connection.ProductId != device.ProductId
            ? null
            : new RazerGamepadService(
                new RazerFeatureTransport(),
                connection.DevicePath,
                TimeSpan.FromMilliseconds(20),
                diagnostic,
                connection.ProductId);
    }

    private static bool IsExpected(Exception exception) =>
        exception is IOException or InvalidDataException or InvalidOperationException or
            NotSupportedException or UnauthorizedAccessException or ArgumentException or
            System.ComponentModel.Win32Exception;

    private readonly record struct LightingValues(
        byte RegionId,
        byte EffectId,
        byte Flags,
        byte Rate,
        byte? Brightness,
        IReadOnlyList<RazerRgb> Colors);
}
