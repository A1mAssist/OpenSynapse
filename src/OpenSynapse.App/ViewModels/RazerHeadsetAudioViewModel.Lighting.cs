using System.Globalization;
using Microsoft.UI.Xaml;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using Windows.UI;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHeadsetAudioViewModel
{
    [ProfileSetting("headset.lighting.brightness", DeviceCategory.Headset, powerScoped: true)]
    public async Task ApplyMatrixBrightnessAsync(CancellationToken cancellationToken = default)
    {
        if (IsAvnera65)
        {
            var previousBrightness = Brightness;
            if (!await RunAvneraWriteAsync(
                    (service, token) => service.SetChromaBrightnessAsync(
                        (byte)Math.Round(Brightness), token),
                    cancellationToken))
            {
                return;
            }

            await SaveHeadsetSettingAsync(
                settings => settings.ChromaBrightness = (int)Math.Round(Brightness),
                settings => settings.ChromaBrightness = (int)Math.Round(previousBrightness),
                () => Brightness = previousBrightness,
                token => WriteChromaBrightnessHardwareAsync(previousBrightness, token),
                cancellationToken);
            return;
        }

        var previous = Brightness;
        var applied = false;
        await RunChromaWriteAsync(
            async (service, token) =>
            {
                await service.SetBrightnessAsync((byte)Math.Round(Brightness), token);
                applied = true;
            },
            cancellationToken);
        if (!applied) return;

        await SaveHeadsetSettingAsync(
            settings => settings.ChromaBrightness = (int)Math.Round(Brightness),
            settings => settings.ChromaBrightness = (int)Math.Round(previous),
            () => Brightness = previous,
            token => OpenChromaService().SetBrightnessAsync((byte)Math.Round(previous), token),
            cancellationToken);
    }

    [ProfileSetting("headset.lighting.effect", DeviceCategory.Headset, powerScoped: true)]
    public async Task ApplyMatrixLightingAsync(CancellationToken cancellationToken = default)
    {
        if (IsAvnera65)
        {
            var previousAvneraIndex = SelectedLightingEffectIndex;
            var previousAvneraPrimary = PrimaryColor;
            var previousAvneraSecondary = SecondaryColor;
            var previousAvneraDirection = SelectedWaveDirectionIndex;
            var avneraEffect = LightingEffects[SelectedLightingEffectIndex];
            if (!await RunAvneraWriteAsync(
                    (service, token) => service.SetChromaEffectAsync(
                        avneraEffect, PrimaryColor.R, PrimaryColor.G, PrimaryColor.B, token),
                    cancellationToken))
            {
                return;
            }

            await SaveHeadsetSettingAsync(
                settings =>
                {
                    settings.ChromaEffect = avneraEffect.ToString();
                    settings.ChromaParameters = new Dictionary<string, string>
                    {
                        ["primary"] = $"{PrimaryColor.R:X2}{PrimaryColor.G:X2}{PrimaryColor.B:X2}",
                        ["secondary"] = $"{SecondaryColor.R:X2}{SecondaryColor.G:X2}{SecondaryColor.B:X2}",
                        ["direction"] = SelectedWaveDirectionIndex.ToString(),
                    };
                },
                settings =>
                {
                    settings.ChromaEffect = LightingEffects[previousAvneraIndex].ToString();
                    settings.ChromaParameters = new Dictionary<string, string>
                    {
                        ["primary"] = $"{previousAvneraPrimary.R:X2}{previousAvneraPrimary.G:X2}{previousAvneraPrimary.B:X2}",
                        ["secondary"] = $"{previousAvneraSecondary.R:X2}{previousAvneraSecondary.G:X2}{previousAvneraSecondary.B:X2}",
                        ["direction"] = previousAvneraDirection.ToString(),
                    };
                },
                () =>
                {
                    SelectedLightingEffectIndex = previousAvneraIndex;
                    PrimaryColor = previousAvneraPrimary;
                    SecondaryColor = previousAvneraSecondary;
                    SelectedWaveDirectionIndex = previousAvneraDirection;
                },
                token => WriteChromaEffectHardwareAsync(
                    LightingEffects[previousAvneraIndex], previousAvneraPrimary, previousAvneraSecondary,
                    previousAvneraDirection, token),
                cancellationToken);
            return;
        }

        var previousEffectIndex = SelectedLightingEffectIndex;
        var previousPrimary = PrimaryColor;
        var previousSecondary = SecondaryColor;
        var previousDirection = SelectedWaveDirectionIndex;
        var previousEffect = LightingEffects[previousEffectIndex];
        var effect = LightingEffects[SelectedLightingEffectIndex];
        if (IsJolie && IsSoftwareRenderedEffect(effect))
        {
            if (!await RunSoftwareEffectAsync(
                    effect, previousEffect, previousPrimary, previousSecondary, previousDirection,
                    cancellationToken))
            {
                SelectedLightingEffectIndex = previousEffectIndex;
                return;
            }
        }
        else
        {
            await StopSoftwareEffectCoreAsync(fadeOut: true);
            var applied = false;
            await RunChromaWriteAsync(async (service, token) =>
            {
                await WriteChromaEffectAsync(service, effect, PrimaryColor, SecondaryColor, token);
                applied = true;
            }, cancellationToken);
            if (!applied) return;
        }

        await SaveHeadsetSettingAsync(
            settings =>
            {
                settings.ChromaEffect = effect.ToString();
                settings.ChromaParameters = new Dictionary<string, string>
                {
                    ["primary"] = $"{PrimaryColor.R:X2}{PrimaryColor.G:X2}{PrimaryColor.B:X2}",
                    ["secondary"] = $"{SecondaryColor.R:X2}{SecondaryColor.G:X2}{SecondaryColor.B:X2}",
                    ["direction"] = SelectedWaveDirectionIndex.ToString(),
                };
            },
            settings =>
            {
                settings.ChromaEffect = LightingEffects[previousEffectIndex].ToString();
                settings.ChromaParameters = new Dictionary<string, string>
                {
                    ["primary"] = $"{previousPrimary.R:X2}{previousPrimary.G:X2}{previousPrimary.B:X2}",
                    ["secondary"] = $"{previousSecondary.R:X2}{previousSecondary.G:X2}{previousSecondary.B:X2}",
                    ["direction"] = previousDirection.ToString(),
                };
            },
            () =>
            {
                SelectedLightingEffectIndex = previousEffectIndex;
                PrimaryColor = previousPrimary;
                SecondaryColor = previousSecondary;
                SelectedWaveDirectionIndex = previousDirection;
            },
            token => RestoreEffectAsync(
                previousEffect, previousPrimary, previousSecondary, previousDirection, token),
            cancellationToken);
    }

    [ProfileSetting("headset.auto-sleep", DeviceCategory.Headset, powerScoped: true)]
    public async Task ApplyAutoSleepAsync(CancellationToken cancellationToken = default)
    {
        if (!IsJolie || !BeginOperation()) return;

        var previousEnabled = AutoSleepEnabled;
        var previousIndex = SelectedAutoSleepMinutesIndex;
        var minutes = AutoSleepMinutes;
        try
        {
            var power = new RazerMakoXPowerService(_connection.DevicePath, _diagnostic);
            var previousHardware = await power.ReadAsync(cancellationToken);
            await power.SetAsync(AutoSleepEnabled, minutes, cancellationToken);
            var applied = await power.ReadAsync(cancellationToken);
            if (applied.Enabled != AutoSleepEnabled || Math.Abs(applied.Minutes - minutes) > 1)
            {
                throw new InvalidOperationException(
                    $"Mako X reported auto sleep {applied.Enabled}/{applied.Minutes} min instead of {AutoSleepEnabled}/{minutes} min.");
            }

            AutoSleepEnabled = applied.Enabled;
            SelectAutoSleepMinutes(applied.Minutes);
            await SaveHeadsetSettingAsync(
                settings =>
                {
                    settings.AutoSleepEnabled = applied.Enabled;
                    settings.AutoShutdownMinutes = applied.Minutes;
                },
                settings =>
                {
                    settings.AutoSleepEnabled = previousEnabled;
                    settings.AutoShutdownMinutes = AutoSleepMinuteChoices[previousIndex];
                },
                () =>
                {
                    AutoSleepEnabled = previousEnabled;
                    SelectedAutoSleepMinutesIndex = previousIndex;
                },
                token => new RazerMakoXPowerService(_connection.DevicePath, _diagnostic)
                    .SetAsync(previousHardware.Enabled, previousHardware.Minutes, token),
                cancellationToken);
            ErrorText = string.Empty;
            OperationStatusText = AppStrings.Text("BlackSharkSaved");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            AutoSleepEnabled = previousEnabled;
            SelectedAutoSleepMinutesIndex = previousIndex;
            ErrorText = exception.Message;
            OperationStatusText = string.Empty;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task WriteChromaEffectAsync(
        IRazerHeadsetLightingService service,
        RazerChromaEffect effect,
        Color primary,
        Color secondary,
        CancellationToken cancellationToken)
    {
        if (effect == RazerChromaEffect.CustomFrame ||
            (service is RazerMakoXChromaService && IsSoftwareRenderedEffect(effect)))
        {
            throw new InvalidOperationException(
                $"{effect} is rendered as frames by the software effect pump.");
        }

        var colors = effect switch
        {
            RazerChromaEffect.Static => new byte[] { primary.R, primary.G, primary.B },
            // Synapse's Joliet defaults send one color for Breathing.
            RazerChromaEffect.Breathing when service is RazerMakoXChromaService =>
                [primary.R, primary.G, primary.B],
            RazerChromaEffect.Breathing =>
                [primary.R, primary.G, primary.B, secondary.R, secondary.G, secondary.B],
            RazerChromaEffect.Starlight => new byte[] { primary.R, primary.G, primary.B },
            _ => [],
        };
        await service.SetEffectAsync(effect, colors, cancellationToken).ConfigureAwait(false);
    }

    internal async Task<bool> TurnOffLightingForShutdownAsync(
        CancellationToken cancellationToken = default)
    {
        // Mako X keeps the 0x0F Chroma channel on the audio HID collection, not
        // the RazerHid catalogue, so the RazerHid shutdown sweep never sees it.
        if (!IsJolie)
        {
            return false;
        }

        await StopSoftwareEffectCoreAsync(fadeOut: true);
        await OpenChromaService()
            .SetEffectAsync(RazerChromaEffect.Off, [], cancellationToken)
            .ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Hands the Mako X strip to Lighting Studio: stop the device-page frame pump
    /// before Studio enters CustomFrame, otherwise both pumps overwrite each other.
    /// </summary>
    internal Task StopSoftwareEffectForExternalAsync() =>
        IsJolie ? StopSoftwareEffectCoreAsync(fadeOut: true) : Task.CompletedTask;

    private async Task ApplyPersistedJolietLightingAsync(
        HeadsetProfileSettings profile,
        CancellationToken cancellationToken)
    {
        var restoreSoftwareEffect = false;
        if (!string.IsNullOrWhiteSpace(profile.ChromaEffect) &&
            Enum.TryParse<RazerChromaEffect>(profile.ChromaEffect, ignoreCase: true, out var effect) &&
            LightingEffects.Contains(effect))
        {
            var primary = TryGetChromaColor(profile.ChromaParameters, "primary", PrimaryColor);
            var secondary = TryGetChromaColor(profile.ChromaParameters, "secondary", SecondaryColor);
            if (profile.ChromaParameters is { } parameters &&
                parameters.TryGetValue("direction", out var direction) &&
                int.TryParse(direction, out var directionIndex))
            {
                SelectedWaveDirectionIndex = directionIndex;
            }
            try
            {
                // Software effects need their saved colors selected first; the pump
                // starts after this block. Firmware effects can be written now.
                if (!IsSoftwareRenderedEffect(effect))
                {
                    await StopSoftwareEffectCoreAsync();
                    await WriteChromaEffectHardwareAsync(
                        effect, primary, secondary, SelectedWaveDirectionIndex, cancellationToken);
                }
                SelectedLightingEffectIndex = Array.IndexOf(LightingEffects, effect);
                PrimaryColor = primary;
                SecondaryColor = secondary;
                restoreSoftwareEffect = IsSoftwareRenderedEffect(effect);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (IsExpectedException(exception))
            {
                _diagnostic?.Invoke($"profile headset.lighting.effect: {exception.Message}");
            }
        }

        if (profile.ChromaBrightness is int brightness)
        {
            var applied = (byte)Math.Clamp(brightness, 0, 100);
            try
            {
                await WriteChromaBrightnessHardwareAsync(applied, cancellationToken);
                Brightness = applied;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (IsExpectedException(exception))
            {
                _diagnostic?.Invoke($"profile headset.lighting.brightness: {exception.Message}");
            }
        }

        if (restoreSoftwareEffect)
        {
            try
            {
                // The frame pump does not survive a restart; replay the saved effect now.
                await ResumeSoftwareEffectIfNeededAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (IsExpectedException(exception))
            {
                _diagnostic?.Invoke($"profile headset.lighting.resume: {exception.Message}");
            }
        }
    }

    [NotProfileScoped]
    private async Task ApplyPersistedJolietPowerAsync(
        HeadsetProfileSettings profile,
        CancellationToken cancellationToken)
    {
        var minutes = profile.AutoShutdownMinutes;
        var enabled = profile.AutoSleepEnabled;
        if (minutes is null && enabled is null)
        {
            return;
        }

        var appliedMinutes = Math.Clamp(
            minutes is > 0 ? minutes.Value : AutoSleepMinuteChoices[_selectedAutoSleepMinutesIndex],
            15,
            60);
        var appliedEnabled = enabled ?? (minutes is > 0);
        try
        {
            await new RazerMakoXPowerService(_connection.DevicePath, _diagnostic)
                .SetAsync(appliedEnabled, appliedMinutes, cancellationToken);
            AutoSleepEnabled = appliedEnabled;
            SelectAutoSleepMinutes(appliedMinutes);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            _diagnostic?.Invoke($"profile headset.auto-sleep: {exception.Message}");
        }
    }

    private void SelectAutoSleepMinutes(int minutes) =>
        SelectedAutoSleepMinutesIndex = Math.Clamp(
            (int)Math.Round((minutes - AutoSleepMinuteChoices[0]) / 5d), 0,
            AutoSleepMinuteChoices.Length - 1);

    private static Color TryGetChromaColor(
        IReadOnlyDictionary<string, string>? parameters,
        string key,
        Color fallback)
    {
        if (parameters is null)
        {
            return fallback;
        }

        foreach (var (name, value) in parameters)
        {
            if (!string.Equals(name, key, StringComparison.OrdinalIgnoreCase) ||
                value is not { Length: 6 } ||
                !uint.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
            {
                continue;
            }

            return Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        }

        return fallback;
    }
}

