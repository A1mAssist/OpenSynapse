using System.Globalization;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed partial class RazerHidProfileApplier
{
    private const byte KnobId = RazerKnobProtocol.KnobId;

    private async Task ApplyKeyboardAsync(
        KeyboardProfileSettings profile,
        LightingProfile lighting,
        RazerHidDeviceConnection connection,
        List<ProfileSettingApplyResult> results,
        CancellationToken cancellationToken)
    {
        if (profile.PollingRateHertz is { } polling)
        {
            await ApplySettingAsync(
                results, "keyboard.polling-rate", connection,
                RazerHidBackendCapability.PollingRateRead,
                RazerHidBackendCapability.PollingRateWrite,
                token => _service.GetPollingRateAsync(connection, token),
                async (value, token) => _ = await _service.SetPollingRateAsync(connection, value, token),
                polling, (left, right) => left == right, cancellationToken);
        }

        if (TryParseKeyswitch(profile.SwitchType, out var keyswitch))
        {
            await ApplySettingAsync(
                results, "keyboard.keyswitch", connection,
                RazerHidBackendCapability.KeyswitchOptimizationRead,
                RazerHidBackendCapability.KeyswitchOptimizationWrite,
                token => _service.GetKeyswitchOptimizationAsync(connection, token),
                (value, token) => _service.SetKeyswitchOptimizationAsync(connection, value, token),
                keyswitch, (left, right) => left == right, cancellationToken);
        }
        else if (!string.IsNullOrWhiteSpace(profile.SwitchType))
        {
            results.Add(new ProfileSettingApplyResult(
                "keyboard.keyswitch", ProfileSettingApplyStatus.Failed, "Unknown keyswitch type."));
        }

        if (profile.SnapTapEnabled is { } snapTap)
        {
            await ApplySnapTapAsync(connection, snapTap, results, cancellationToken);
        }

        if (profile.DebounceMilliseconds is { } debounce)
        {
            await ApplyDebounceAsync(connection, (byte)debounce, results, cancellationToken);
        }

        if (profile.OledBrightness is { } oledBrightness)
        {
            await ApplySettingAsync(
                results, "keyboard.oled-brightness", connection,
                RazerHidBackendCapability.OledRead,
                RazerHidBackendCapability.OledWrite,
                token => _service.GetOledBrightnessAsync(connection, token),
                (value, token) => _service.SetOledBrightnessAsync(connection, value, token),
                (byte)oledBrightness, (left, right) => left == right, cancellationToken);
        }

        if (TryParseOledLanguage(profile.OledLanguage, out var language))
        {
            await ApplySettingAsync(
                results, "keyboard.oled-language", connection,
                RazerHidBackendCapability.OledRead,
                RazerHidBackendCapability.OledWrite,
                token => _service.GetOledLanguageAsync(connection, token),
                (value, token) => _service.SetOledLanguageAsync(connection, value, token),
                language, (left, right) => left == right, cancellationToken);
        }
        else if (!string.IsNullOrWhiteSpace(profile.OledLanguage))
        {
            results.Add(new ProfileSettingApplyResult(
                "keyboard.oled-language", ProfileSettingApplyStatus.Failed, "Unknown OLED language."));
        }

        if (profile.KnobMode is { } knobMode)
        {
            await ApplyKnobAsync(connection, (byte)knobMode, results, cancellationToken);
        }

        if (profile.WristRestLightingEnabled is { } wristRest)
        {
            // The wrist-rest command only carries a colour. The lighting section is
            // the existing colour source; lighting itself remains applied elsewhere.
            await ApplyWristRestAsync(
                connection, wristRest, lighting, results, cancellationToken);
        }

        // Disable first, then restore the key table; enabling only after the controller
        // mappings are in place keeps the keyboard usable if the profile fails halfway.
        if (profile.GamepadRemapEnabled == false)
        {
            await ApplyJoystickModeAsync(connection, false, results, cancellationToken);
        }

        if (RazerHidDeviceService.UsesHostKeypadMapping(connection))
        {
            try
            {
                await _service.ReplaceKeypadMappingsAsync(
                    connection, profile.ButtonMappings ?? [], cancellationToken).ConfigureAwait(false);
                results.Add(Applied("mapping"));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception) { results.Add(Failed("mapping", exception)); }
        }
        else if (profile.ButtonMappings is { Count: > 0 } mappings)
        {
            await ApplyButtonMappingsAsync(connection, mappings, results, cancellationToken);
        }

        if (profile.GamepadRemapEnabled == true)
        {
            await ApplyJoystickModeAsync(connection, true, results, cancellationToken);
        }
    }

    private async Task ApplySnapTapAsync(
        RazerHidDeviceConnection connection,
        bool enabled,
        List<ProfileSettingApplyResult> results,
        CancellationToken cancellationToken)
    {
        const string key = "keyboard.snap-tap";
        if (!Has(connection, RazerHidBackendCapability.AnalogKeyWrite))
        {
            results.Add(Unsupported(key, RazerHidBackendCapability.AnalogKeyWrite));
            return;
        }

        var canRead = Has(connection, RazerHidBackendCapability.AnalogKeyRead);
        if (enabled && !canRead)
        {
            results.Add(new ProfileSettingApplyResult(key, ProfileSettingApplyStatus.Skipped,
                "Reading the current Snap Tap pairs is required before enabling it."));
            return;
        }

        try
        {
            IReadOnlyList<RazerAnalogSnapTapPair> pairs = [];
            if (canRead)
            {
                var current = await _service.GetAnalogSnapTapAsync(connection, ProfileId, cancellationToken);
                if (current.Enabled == enabled)
                {
                    results.Add(Applied(key));
                    return;
                }
                pairs = current.Pairs;
            }

            await _service.SetAnalogSnapTapAsync(connection, ProfileId, enabled, pairs, cancellationToken);
            if (!canRead)
            {
                results.Add(new ProfileSettingApplyResult(key,
                    ProfileSettingApplyStatus.AppliedUnverified));
                return;
            }

            var applied = await _service.GetAnalogSnapTapAsync(connection, ProfileId, cancellationToken);
            results.Add(applied.Enabled == enabled
                ? Applied(key)
                : Mismatch(key, $"expected {enabled}, got {applied.Enabled}"));
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

    private async Task ApplyDebounceAsync(
        RazerHidDeviceConnection connection,
        byte debounce,
        List<ProfileSettingApplyResult> results,
        CancellationToken cancellationToken)
    {
        const string key = "keyboard.debounce";
        if (!Has(connection, RazerHidBackendCapability.ProfileDebounceTimeWrite))
        {
            results.Add(Unsupported(key, RazerHidBackendCapability.ProfileDebounceTimeWrite));
            return;
        }

        var canRead = Has(connection, RazerHidBackendCapability.ProfileDebounceTimeRead);
        try
        {
            var current = canRead
                ? await _service.GetProfileDebounceTimeAsync(connection, cancellationToken)
                : null;
            if (current is not null &&
                current.MakeDebounceTimeMs == debounce &&
                current.BreakDebounceTimeMs == debounce)
            {
                results.Add(Applied(key));
                return;
            }

            await _service.SetProfileDebounceTimeAsync(
                connection,
                debounce,
                current?.MakeMaskTimeMs ?? 0,
                debounce,
                current?.BreakMaskTimeMs ?? 0,
                cancellationToken);
            if (!canRead)
            {
                results.Add(new ProfileSettingApplyResult(key,
                    ProfileSettingApplyStatus.AppliedUnverified));
                return;
            }

            var applied = await _service.GetProfileDebounceTimeAsync(connection, cancellationToken);
            results.Add(applied.MakeDebounceTimeMs == debounce &&
                applied.BreakDebounceTimeMs == debounce
                    ? Applied(key)
                    : Mismatch(key, "debounce times did not match the write"));
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

    private async Task ApplyKnobAsync(
        RazerHidDeviceConnection connection,
        byte mode,
        List<ProfileSettingApplyResult> results,
        CancellationToken cancellationToken)
    {
        const string key = "keyboard.knob-mode";
        if (!Has(connection, RazerHidBackendCapability.KnobWrite))
        {
            results.Add(Unsupported(key, RazerHidBackendCapability.KnobWrite));
            return;
        }
        if (!Has(connection, RazerHidBackendCapability.KnobRead))
        {
            results.Add(new ProfileSettingApplyResult(key, ProfileSettingApplyStatus.Skipped,
                "Reading the current knob stage is required before changing its mode."));
            return;
        }
        if (RazerKnobProtocol.GetModeTable(connection.Definition.ProductId) is not { } table)
        {
            results.Add(new ProfileSettingApplyResult(key, ProfileSettingApplyStatus.Skipped,
                "This keyboard has no product-specific command-dial table."));
            return;
        }

        try
        {
            var profileId = await _service.ResolveActiveProfileIdAsync(connection, cancellationToken);
            var configuration = await _service.GetKnobConfigurationAsync(
                connection, profileId, KnobId, cancellationToken);
            var current = await _service.GetKnobModeAsync(
                connection, profileId, KnobId, cancellationToken);
            if (table.Find(mode) is null)
            {
                results.Add(Mismatch(key, $"mode {mode} is not in this keyboard's dial table"));
                return;
            }

            var stage = current.Stage with { ModeId = mode };
            await _service.SetKnobModeAsync(
                connection, profileId, KnobId, stage, cancellationToken);
            if (Has(connection, RazerHidBackendCapability.OledWrite))
            {
                var flags = RazerKnobProtocol.CreateModeStateFlags(table, configuration.Stages);
                await _service.SetKnobModeStateAsync(
                    connection, profileId, flags, cancellationToken);
                if (RazerKnobProtocol.FindActiveMode(table, configuration.Stages, mode) is { } active)
                {
                    await _service.SetKnobActiveModeAsync(
                        connection, profileId, active, cancellationToken);
                }
            }
            var applied = await _service.GetKnobModeAsync(
                connection, profileId, KnobId, cancellationToken);
            results.Add(applied.Stage.ModeId == mode
                ? Applied(key)
                : Mismatch(key, $"expected {mode}, got {applied.Stage.ModeId}"));
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

    private async Task ApplyJoystickModeAsync(
        RazerHidDeviceConnection connection,
        bool enabled,
        List<ProfileSettingApplyResult> results,
        CancellationToken cancellationToken)
    {
        const string key = "keyboard.gamepad-remap";
        if (!Has(connection, RazerHidBackendCapability.AnalogJoystickWrite))
        {
            results.Add(Unsupported(key, RazerHidBackendCapability.AnalogJoystickWrite));
            return;
        }

        try
        {
            await _service.SetAnalogJoystickReportModeAsync(
                connection, enabled ? (byte)0x01 : (byte)0x00, cancellationToken);
            results.Add(Applied(key));
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

    private async Task ApplyWristRestAsync(
        RazerHidDeviceConnection connection,
        bool enabled,
        LightingProfile lighting,
        List<ProfileSettingApplyResult> results,
        CancellationToken cancellationToken)
    {
        const string key = "keyboard.wrist-rest";
        if (!RazerHidWristRestProtocol.Supports(connection.Definition) ||
            !Has(connection, RazerHidBackendCapability.MatrixFrameWrite))
        {
            results.Add(Unsupported(key, RazerHidBackendCapability.MatrixFrameWrite));
            return;
        }

        var color = enabled && TryGetLightingColor(lighting, out var profileColor)
            ? profileColor
            : default;
        if (enabled && color == default)
        {
            results.Add(new ProfileSettingApplyResult(key, ProfileSettingApplyStatus.Skipped,
                "The profile does not contain a wrist-rest colour."));
            return;
        }

        try
        {
            await _service.SetWristRestColorAsync(connection, color, cancellationToken);
            results.Add(new ProfileSettingApplyResult(key, ProfileSettingApplyStatus.AppliedUnverified));
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

    private static bool TryParseKeyswitch(string? value, out RazerHidKeyswitchOptimization optimization)
    {
        optimization = default;
        return !string.IsNullOrWhiteSpace(value) &&
            Enum.TryParse(value, ignoreCase: true, out optimization) &&
            Enum.IsDefined(optimization);
    }

    private static bool TryParseOledLanguage(string? value, out byte language)
    {
        language = 0;
        if (string.IsNullOrWhiteSpace(value) ||
            !Enum.TryParse<KeyboardOledLanguage>(value, ignoreCase: true, out var parsed) ||
            !Enum.IsDefined(parsed))
        {
            return false;
        }

        language = (byte)parsed;
        return true;
    }

    private static bool TryGetLightingColor(LightingProfile lighting, out RazerHidColor color)
    {
        color = default;
        if (!lighting.Parameters.TryGetValue("color", out var raw) || raw.Length != 6)
        {
            return false;
        }

        if (!byte.TryParse(raw[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var red) ||
            !byte.TryParse(raw[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var green) ||
            !byte.TryParse(raw[4..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var blue))
        {
            return false;
        }

        color = new RazerHidColor(red, green, blue);
        return true;
    }
}
