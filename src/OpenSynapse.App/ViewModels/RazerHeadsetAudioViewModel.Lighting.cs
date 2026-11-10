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
}

