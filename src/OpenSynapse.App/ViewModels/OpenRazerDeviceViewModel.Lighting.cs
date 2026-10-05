using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using Windows.UI;

namespace OpenSynapse.App.ViewModels;

public sealed partial class OpenRazerDeviceViewModel : INotifyPropertyChanged
{
    public async Task LoadLightingAsync(CancellationToken cancellationToken = default)
    {
        if (!IsReady || RequiresRescan || IsLightingLoading || SelectedZoneCapabilities is null)
        {
            return;
        }
        var selectedZone = SelectedLightingZone;
        IsLightingLoading = true;
        try
        {
            var zone = SelectedZoneCapabilities;
            if (zone.CanReadBrightness && !_unsupportedBrightnessReads.Contains(selectedZone))
                await ReadZoneAsync(selectedZone, _unsupportedBrightnessReads, async () =>
                {
                    var brightness = await _service.GetBrightnessAsync(Connection,
                        Connection.Definition.DefaultStorage, selectedZone, cancellationToken);
                    if (SelectedLightingZone != selectedZone) return;
                    Brightness = brightness;
                    _brightnessKnown = true;
                    OnPropertyChanged(nameof(BrightnessText));
                });
            if (SelectedLightingZone == selectedZone && zone.CanReadState &&
                !_unsupportedLedStateReads.Contains(selectedZone))
                await ReadZoneAsync(selectedZone, _unsupportedLedStateReads, async () =>
                {
                    var enabled = await _service.GetLedStateAsync(Connection,
                        Connection.Definition.DefaultStorage, selectedZone, cancellationToken);
                    if (SelectedLightingZone != selectedZone) return;
                    LedEnabled = enabled;
                    _ledStateKnown = true;
                    OnPropertyChanged(nameof(LedStateText));
                });
            if (SelectedLightingZone == selectedZone && zone.CanReadColor &&
                !_unsupportedLedColorReads.Contains(selectedZone))
                await ReadZoneAsync(selectedZone, _unsupportedLedColorReads, async () =>
                {
                    var color = await _service.GetLedColorAsync(Connection,
                        Connection.Definition.DefaultStorage, selectedZone, cancellationToken);
                    if (SelectedLightingZone != selectedZone) return;
                    PrimaryColor = ToColor(color);
                });
            if (SelectedLightingZone == selectedZone && zone.CanReadEffect &&
                !_unsupportedLedEffectReads.Contains(selectedZone))
                await ReadZoneAsync(selectedZone, _unsupportedLedEffectReads, async () =>
                {
                    var effect = await _service.GetLedEffectAsync(Connection,
                        Connection.Definition.DefaultStorage, selectedZone, cancellationToken);
                    if (SelectedLightingZone != selectedZone) return;
                    var mapped = effect switch
                    {
                        OpenRazerClassicLedEffect.Static => OpenRazerLightingEffect.Static,
                        OpenRazerClassicLedEffect.Blinking => OpenRazerLightingEffect.Blinking,
                        OpenRazerClassicLedEffect.Breathing => OpenRazerLightingEffect.BreathingSingle,
                        OpenRazerClassicLedEffect.Spectrum => OpenRazerLightingEffect.Spectrum,
                        _ => (OpenRazerLightingEffect?)null,
                    };
                    if (mapped is { } value && LightingEffects.Contains(value)) SelectedLightingEffect = value;
                });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            HandleFailure(exception);
        }
        finally
        {
            IsLightingLoading = false;
        }
        if (SelectedLightingZone != selectedZone && !cancellationToken.IsCancellationRequested)
            await LoadLightingAsync(cancellationToken);
    }

    public async Task ApplyConfiguredLightingAsync(CancellationToken cancellationToken = default)
    {
        if (_lightingProfileResolver is null || !IsReady || RequiresRescan)
        {
            return;
        }

        if (!LightingEnabled)
        {
            await DisableLightingAsync(cancellationToken);
            return;
        }

        var profile = _lightingProfileResolver(SelectedLightingPowerState);
        if (profile is null)
        {
            return;
        }

        ApplyLightingProfileToEditor(profile);
        if (!IsSelectedLightingPowerActive)
        {
            return;
        }

        try
        {
            await ApplyLightingProfileToHardwareAsync(profile, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            HandleFailure(exception);
        }
    }

    public async Task ApplyLightingSettingsAsync(CancellationToken cancellationToken = default)
    {
        if (_lightingSettingsSaver is null || !CanEditLightingSettings)
        {
            return;
        }

        IsLightingSettingsBusy = true;
        try
        {
            if (!LightingEnabled)
            {
                await DisableLightingAsync(cancellationToken);
            }
            else
            {
                await ApplyConfiguredLightingAsync(cancellationToken);
            }

            if (!await _lightingSettingsSaver(
                    LightingEnabled,
                    ChromaOverrideEnabled,
                    cancellationToken))
            {
                throw new IOException("OpenRazer lighting settings could not be saved.");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            HandleFailure(exception);
        }
        finally
        {
            IsLightingSettingsBusy = false;
            OnPropertyChanged(nameof(CanEditLightingSettings));
        }
    }

    public Task ApplyBrightnessAsync(CancellationToken cancellationToken = default)
    {
        var zone = SelectedLightingZone;
        var capabilities = SelectedZoneCapabilities;
        return RunZoneWriteAsync(() => capabilities?.CanWriteBrightness == true &&
                !_unsupportedBrightnessWrites.Contains(zone),
            () => IsBrightnessBusy, value => IsBrightnessBusy = value,
            async () =>
            {
                if (IsSelectedLightingPowerActive)
                {
                    await _service.SetBrightnessAsync(Connection, Brightness,
                        Connection.Definition.DefaultStorage, zone, cancellationToken);
                    if (capabilities?.CanReadBrightness == true)
                        Brightness = await _service.GetBrightnessAsync(Connection,
                            Connection.Definition.DefaultStorage, zone, cancellationToken);
                }
                await SaveLightingProfileAsync(cancellationToken);
                OnPropertyChanged(nameof(BrightnessText));
            }, cancellationToken,
            () =>
            {
                _unsupportedBrightnessWrites.Add(zone);
                OnLightingSelectionChanged();
            });
    }

    public Task ApplyLedStateAsync(CancellationToken cancellationToken = default)
    {
        var zone = SelectedLightingZone;
        var enabled = LedEnabled;
        return RunZoneWriteAsync(
            () => Connection.LightingZones.GetValueOrDefault(zone)?.CanWriteState == true &&
                !_unsupportedLedStateWrites.Contains(zone),
            () => IsLedStateBusy, value => IsLedStateBusy = value,
            async () =>
            {
                if (IsSelectedLightingPowerActive)
                {
                    await _service.SetLedStateAsync(Connection, Connection.Definition.DefaultStorage, zone, enabled, cancellationToken);
                    if (Connection.LightingZones.GetValueOrDefault(zone)?.CanReadState == true)
                        LedEnabled = await _service.GetLedStateAsync(Connection, Connection.Definition.DefaultStorage, zone, cancellationToken);
                }
                await SaveLightingProfileAsync(cancellationToken);
            }, cancellationToken,
            () =>
            {
                _unsupportedLedStateWrites.Add(zone);
                OnLightingSelectionChanged();
            });
    }

    public Task TriggerReactiveAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(OpenRazerBackendCapability.ReactiveTriggerWrite,
            () => IsLightingBusy, value => IsLightingBusy = value,
            () => _service.TriggerReactiveAsync(Connection, cancellationToken), cancellationToken);

    public Task ApplyMatrixAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(OpenRazerBackendCapability.MatrixFrameWrite,
            () => IsMatrixBusy, value => IsMatrixBusy = value,
            async () =>
            {
                foreach (var row in MatrixCells.GroupBy(cell => cell.Row).OrderBy(group => group.Key))
                {
                    var colors = row.OrderBy(cell => cell.Column)
                        .Select(cell => ToOpenRazerColor(cell.Color)).ToArray();
                    await _service.SetCustomRowAsync(Connection, row.Key, 0, colors, cancellationToken);
                }
            }, cancellationToken);

    private async Task DisableLightingAsync(CancellationToken cancellationToken)
    {
        var zone = SelectedLightingZone;
        var capabilities = SelectedZoneCapabilities;
        if (capabilities?.CanWriteState == true && !_unsupportedLedStateWrites.Contains(zone))
        {
            await _service.SetLedStateAsync(
                Connection,
                Connection.Definition.DefaultStorage,
                zone,
                false,
                cancellationToken);
            LedEnabled = false;
            _ledStateKnown = true;
            OnPropertyChanged(nameof(LedStateText));
            return;
        }

        if (Has(OpenRazerBackendCapability.LightingEffectWrite) &&
            capabilities?.LightingEffects.Contains(OpenRazerLightingEffect.Off) == true &&
            !_unsupportedLighting.Contains((zone, OpenRazerLightingEffect.Off)))
        {
            await _service.SetLightingAsync(
                Connection,
                new OpenRazerLightingSettings(
                    OpenRazerLightingEffect.Off,
                    LightingSpeed,
                    LightingDirection,
                    ToOpenRazerColor(PrimaryColor),
                    ToOpenRazerColor(SecondaryColor),
                    null,
                    zone),
                cancellationToken);
        }
    }

    public Task ApplyLightingAsync(CancellationToken cancellationToken = default)
    {
        var zone = SelectedLightingZone;
        var effect = SelectedLightingEffect;
        return RunZoneWriteAsync(
            () => Connection.Capabilities.Contains(OpenRazerBackendCapability.LightingEffectWrite) &&
                Connection.LightingZones.TryGetValue(zone, out var capability) &&
                capability.LightingEffects.Contains(effect) && !_unsupportedLighting.Contains((zone, effect)),
            () => IsLightingBusy, value => IsLightingBusy = value,
            async () =>
            {
                if (IsSelectedLightingPowerActive)
                {
                    await _service.SetLightingAsync(Connection, new OpenRazerLightingSettings(
                        effect,
                        LightingSpeed,
                        LightingDirection,
                        ToOpenRazerColor(PrimaryColor),
                        ToOpenRazerColor(SecondaryColor),
                        null,
                        zone), cancellationToken);
                }
                await SaveLightingProfileAsync(cancellationToken);
            },
            cancellationToken,
            () =>
            {
                _unsupportedLighting.Add((zone, effect));
                if (SelectedLightingZone == zone && SelectedLightingEffect == effect)
                    _selectedLightingEffect = LightingEffects.FirstOrDefault();
                OnLightingSelectionChanged();
            });
    }


    private bool? SelectedLightingPowerState => _lightingPowerProfileIndex switch
    {
        1 => true,
        2 => false,
        _ => _powerStateProvider?.Invoke(),
    };

    private bool IsSelectedLightingPowerActive =>
        _lightingPowerProfileIndex == 0 ||
        SelectedLightingPowerState == _powerStateProvider?.Invoke();

    public bool IsLightingPowerProfileActive => IsSelectedLightingPowerActive;

    private void ApplyConfiguredLightingToEditor()
    {
        var profile = _lightingProfileResolver?.Invoke(SelectedLightingPowerState);
        if (profile is not null)
        {
            ApplyLightingProfileToEditor(profile);
        }
    }

    private void ApplyLightingProfileToEditor(LightingProfile profile)
    {
        if (profile.Parameters.TryGetValue("zone", out var rawZone) &&
            byte.TryParse(rawZone, NumberStyles.Integer, CultureInfo.InvariantCulture, out var zoneValue) &&
            Enum.IsDefined(typeof(OpenRazerLedZone), zoneValue) &&
            LightingZones.Contains((OpenRazerLedZone)zoneValue))
        {
            SelectedLightingZone = (OpenRazerLedZone)zoneValue;
        }

        if (Enum.TryParse<OpenRazerLightingEffect>(profile.Effect, true, out var effect) &&
            LightingEffects.Contains(effect))
        {
            SelectedLightingEffect = effect;
            if (effect == OpenRazerLightingEffect.Off)
            {
                LedEnabled = false;
            }
        }

        if (TryGetByte(profile, "brightness", out var brightness)) Brightness = brightness;
        if (TryGetByte(profile, "speed", out var speed)) LightingSpeed = speed;
        if (TryGetByte(profile, "direction", out var direction)) LightingDirection = direction;
        if (TryGetColor(profile, "color", out var primary)) PrimaryColor = ToColor(primary);
        if (TryGetColor(profile, "color2", out var secondary)) SecondaryColor = ToColor(secondary);
        if (profile.Parameters.TryGetValue("enabled", out var rawEnabled) &&
            bool.TryParse(rawEnabled, out var enabled)) LedEnabled = enabled;
    }

    private async Task ApplyLightingProfileToHardwareAsync(
        LightingProfile profile,
        CancellationToken cancellationToken)
    {
        var zone = SelectedLightingZone;
        if (Enum.TryParse<OpenRazerLightingEffect>(profile.Effect, true, out var effect) &&
            Connection.LightingZones.TryGetValue(zone, out var capabilities) &&
            capabilities.LightingEffects.Contains(effect) &&
            Has(OpenRazerBackendCapability.LightingEffectWrite) &&
            !_unsupportedLighting.Contains((zone, effect)))
        {
            try
            {
                await _service.SetLightingAsync(Connection, new OpenRazerLightingSettings(
                    effect,
                    LightingSpeed,
                    LightingDirection,
                    ToOpenRazerColor(PrimaryColor),
                    ToOpenRazerColor(SecondaryColor),
                    null,
                    zone), cancellationToken);
            }
            catch (NotSupportedException)
            {
                _unsupportedLighting.Add((zone, effect));
                if (SelectedLightingEffect == effect)
                    _selectedLightingEffect = LightingEffects.FirstOrDefault();
                OnLightingSelectionChanged();
            }
        }

        if (TryGetByte(profile, "brightness", out var brightness) &&
            Connection.LightingZones.GetValueOrDefault(zone)?.CanWriteBrightness == true &&
            !_unsupportedBrightnessWrites.Contains(zone))
        {
            try
            {
                await _service.SetBrightnessAsync(Connection, brightness,
                    Connection.Definition.DefaultStorage, zone, cancellationToken);
            }
            catch (NotSupportedException)
            {
                _unsupportedBrightnessWrites.Add(zone);
                OnLightingSelectionChanged();
            }
        }

        var stateEnabled = effect == OpenRazerLightingEffect.Off
            ? false
            : profile.Parameters.TryGetValue("enabled", out var rawEnabled) &&
                bool.TryParse(rawEnabled, out var enabled)
                ? enabled
                : (bool?)null;
        if (stateEnabled is bool enabledState &&
            Connection.LightingZones.GetValueOrDefault(zone)?.CanWriteState == true &&
            !_unsupportedLedStateWrites.Contains(zone))
        {
            try
            {
                await _service.SetLedStateAsync(Connection,
                    Connection.Definition.DefaultStorage, zone, enabledState, cancellationToken);
            }
            catch (NotSupportedException)
            {
                _unsupportedLedStateWrites.Add(zone);
                OnLightingSelectionChanged();
            }
        }
    }

    private async Task SaveLightingProfileAsync(CancellationToken cancellationToken)
    {
        if (_lightingProfileSaver is not null)
        {
            if (!await _lightingProfileSaver(
                SelectedLightingPowerState,
                CreateLightingProfile(),
                cancellationToken))
            {
                throw new IOException("OpenRazer lighting profile could not be saved.");
            }
        }
    }

    private LightingProfile CreateLightingProfile()
    {
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["zone"] = ((byte)SelectedLightingZone).ToString(CultureInfo.InvariantCulture),
            ["brightness"] = Brightness.ToString(CultureInfo.InvariantCulture),
            ["speed"] = LightingSpeed.ToString(CultureInfo.InvariantCulture),
            ["direction"] = LightingDirection.ToString(CultureInfo.InvariantCulture),
            ["color"] = FormatColor(ToOpenRazerColor(PrimaryColor)),
            ["color2"] = FormatColor(ToOpenRazerColor(SecondaryColor)),
            ["enabled"] = LedEnabled.ToString(CultureInfo.InvariantCulture),
        };
        return new LightingProfile { Effect = SelectedLightingEffect.ToString(), Parameters = parameters };
    }

    private static bool TryGetByte(LightingProfile profile, string key, out byte value)
    {
        value = 0;
        return profile.Parameters.TryGetValue(key, out var raw) &&
            byte.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryGetColor(LightingProfile profile, string key, out OpenRazerColor color)
    {
        color = default;
        if (!profile.Parameters.TryGetValue(key, out var raw) || raw.Length != 6 ||
            !byte.TryParse(raw[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var red) ||
            !byte.TryParse(raw[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var green) ||
            !byte.TryParse(raw[4..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var blue))
        {
            return false;
        }

        color = new OpenRazerColor(red, green, blue);
        return true;
    }

    private static string FormatColor(OpenRazerColor color) =>
        $"{color.Red:X2}{color.Green:X2}{color.Blue:X2}";
}
