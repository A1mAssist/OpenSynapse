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
                    };
                },
                settings =>
                {
                    settings.ChromaEffect = LightingEffects[previousAvneraIndex].ToString();
                    settings.ChromaParameters = new Dictionary<string, string>
                    {
                        ["primary"] = $"{previousAvneraPrimary.R:X2}{previousAvneraPrimary.G:X2}{previousAvneraPrimary.B:X2}",
                        ["secondary"] = $"{previousAvneraSecondary.R:X2}{previousAvneraSecondary.G:X2}{previousAvneraSecondary.B:X2}",
                    };
                },
                () =>
                {
                    SelectedLightingEffectIndex = previousAvneraIndex;
                    PrimaryColor = previousAvneraPrimary;
                    SecondaryColor = previousAvneraSecondary;
                },
                token => WriteChromaEffectHardwareAsync(
                    LightingEffects[previousAvneraIndex], previousAvneraPrimary, previousAvneraSecondary, token),
                cancellationToken);
            return;
        }

        var previousEffectIndex = SelectedLightingEffectIndex;
        var previousPrimary = PrimaryColor;
        var previousSecondary = SecondaryColor;
        var applied = false;
        await RunChromaWriteAsync(async (service, token) =>
        {
            var effect = LightingEffects[SelectedLightingEffectIndex];
            var colors = effect switch
            {
                RazerChromaEffect.Static => new byte[] { PrimaryColor.R, PrimaryColor.G, PrimaryColor.B },
                RazerChromaEffect.Breathing =>
                    [PrimaryColor.R, PrimaryColor.G, PrimaryColor.B, SecondaryColor.R, SecondaryColor.G, SecondaryColor.B],
                _ => [],
            };
            await service.SetEffectAsync(effect, colors, token);
            applied = true;
        }, cancellationToken);
        if (!applied) return;

        var effect = LightingEffects[SelectedLightingEffectIndex];
        await SaveHeadsetSettingAsync(
            settings =>
            {
                settings.ChromaEffect = effect.ToString();
                settings.ChromaParameters = new Dictionary<string, string>
                {
                    ["primary"] = $"{PrimaryColor.R:X2}{PrimaryColor.G:X2}{PrimaryColor.B:X2}",
                    ["secondary"] = $"{SecondaryColor.R:X2}{SecondaryColor.G:X2}{SecondaryColor.B:X2}",
                };
            },
            settings =>
            {
                settings.ChromaEffect = LightingEffects[previousEffectIndex].ToString();
                settings.ChromaParameters = new Dictionary<string, string>
                {
                    ["primary"] = $"{previousPrimary.R:X2}{previousPrimary.G:X2}{previousPrimary.B:X2}",
                    ["secondary"] = $"{previousSecondary.R:X2}{previousSecondary.G:X2}{previousSecondary.B:X2}",
                };
            },
            () =>
            {
                SelectedLightingEffectIndex = previousEffectIndex;
                PrimaryColor = previousPrimary;
                SecondaryColor = previousSecondary;
            },
            token => WriteChromaEffectHardwareAsync(
                LightingEffects[previousEffectIndex], previousPrimary, previousSecondary, token),
            cancellationToken);
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

        await OpenChromaService()
            .SetEffectAsync(RazerChromaEffect.Off, [], cancellationToken)
            .ConfigureAwait(false);
        return true;
    }

    private async Task ApplyPersistedJolietLightingAsync(
        HeadsetProfileSettings profile,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(profile.ChromaEffect) &&
            Enum.TryParse<RazerChromaEffect>(profile.ChromaEffect, ignoreCase: true, out var effect) &&
            LightingEffects.Contains(effect))
        {
            var primary = TryGetChromaColor(profile.ChromaParameters, "primary", PrimaryColor);
            var secondary = TryGetChromaColor(profile.ChromaParameters, "secondary", SecondaryColor);
            try
            {
                await WriteChromaEffectHardwareAsync(effect, primary, secondary, cancellationToken)
                    .ConfigureAwait(false);
                SelectedLightingEffectIndex = Array.IndexOf(LightingEffects, effect);
                PrimaryColor = primary;
                SecondaryColor = secondary;
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
                await WriteChromaBrightnessHardwareAsync(applied, cancellationToken).ConfigureAwait(false);
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
    }

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

