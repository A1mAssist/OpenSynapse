using Microsoft.UI.Xaml;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerAwBleHeadsetViewModel
{
    private async Task LoadControlsAsync(CancellationToken cancellationToken)
    {
        if (_service is null)
        {
            return;
        }

        var any = false;

        var volume = await TryReadAsync(() => _service.ReadVolumeAsync(cancellationToken))
            .ConfigureAwait(false);
        VolumeVisibility = SetVisibility(volume is not null, ref any);
        if (volume is { } volumeValue)
        {
            Volume = volumeValue;
        }

        var sidetone = await TryReadAsync(() => _service.ReadSidetoneAsync(cancellationToken))
            .ConfigureAwait(false);
        SidetoneVisibility = SetVisibility(sidetone is not null, ref any);
        if (sidetone is { } sidetoneValue)
        {
            Sidetone = sidetoneValue;
        }

        var gameMode = await TryReadAsync(() => _service.ReadGameModeAsync(cancellationToken))
            .ConfigureAwait(false);
        GameModeVisibility = SetVisibility(gameMode is not null, ref any);
        if (gameMode is { } gameModeValue)
        {
            GameMode = gameModeValue;
        }

        var eqPreset = await TryReadAsync(() => _service.ReadEqPresetAsync(cancellationToken))
            .ConfigureAwait(false);
        var eqIndex = eqPreset is { } preset ? IndexOfPreset(preset) : -1;
        EqPresetVisibility = SetVisibility(eqIndex >= 0, ref any);
        if (eqIndex >= 0)
        {
            SelectedEqPresetIndex = eqIndex;
        }

        if (eqPreset is { } currentEqPreset)
        {
            CustomEqEnabled = RazerAwBleProtocol.IsCustomEqPreset(currentEqPreset);
        }

        var autoPowerOff = await TryReadAsync(
            () => _service.ReadAutoPowerOffMinutesAsync(cancellationToken)).ConfigureAwait(false);
        var autoPowerOffIndex = autoPowerOff is { } minutes
            ? Array.IndexOf(RazerAwBleProtocol.AutoPowerOffMinuteOptions, minutes)
            : -1;
        AutoPowerOffVisibility = SetVisibility(autoPowerOffIndex >= 0, ref any);
        if (autoPowerOffIndex >= 0)
        {
            SelectedAutoPowerOffIndex = autoPowerOffIndex;
        }

        var chromaBrightness = await TryReadAsync(
            () => _service.ReadChromaBrightnessAsync(cancellationToken)).ConfigureAwait(false);
        ChromaBrightnessVisibility = SetVisibility(chromaBrightness is not null, ref any);
        if (chromaBrightness is { } chromaValue)
        {
            ChromaBrightness = chromaValue;
        }

        var chromaEffect = await TryReadAsync(
            () => _service.ReadChromaEffectAsync(cancellationToken)).ConfigureAwait(false);
        if (chromaEffect is { } chromaState)
        {
            ApplyChromaState(chromaState);
        }
        else
        {
            ChromaEffectVisibility = Visibility.Collapsed;
        }

        var bass = await TryReadAsync(() => _service.ReadBassAsync(cancellationToken))
            .ConfigureAwait(false);
        BassVisibility = SetVisibility(bass is not null, ref any);
        if (bass is { } bassValue)
        {
            BassLevel = bassValue;
        }

        var haptics = await TryReadAsync(() => _service.ReadHapticsAsync(cancellationToken))
            .ConfigureAwait(false);
        HapticsVisibility = SetVisibility(haptics is not null, ref any);
        if (haptics is { } hapticsValue)
        {
            HapticsLevel = hapticsValue;
        }

        var micEq = await TryReadAsync(
            () => _service.ReadMicEqPresetAsync(cancellationToken)).ConfigureAwait(false);
        var micIndex = micEq is { } micPreset ? IndexOfMicPreset(micPreset) : -1;
        MicEqVisibility = SetVisibility(micIndex >= 0, ref any);
        if (micIndex >= 0)
        {
            SelectedMicEqPresetIndex = micIndex;
        }

        if (micEq is { } currentMicPreset)
        {
            MicCustomEqEnabled = currentMicPreset == RazerAwBleProtocol.CustomEqPreset;
        }

        var micCustomEq = await TryReadReferenceAsync(
            () => _service.ReadMicCustomEqBandsAsync(cancellationToken)).ConfigureAwait(false);
        MicCustomEqVisibility = SetVisibility(micCustomEq is { Count: 10 }, ref any);
        if (micCustomEq is { Count: 10 } micCustomLevels)
        {
            ApplyMicCustomEqBands(micCustomLevels);
        }

        var customEq = await TryReadReferenceAsync(
            () => _service.ReadCustomEqBandsAsync(cancellationToken)).ConfigureAwait(false);
        CustomEqVisibility = SetVisibility(customEq is { Count: 10 }, ref any);
        if (customEq is { Count: 10 } customLevels)
        {
            ApplyCustomEqBands(customLevels);
        }

        ControlsVisibility = any ? Visibility.Visible : Visibility.Collapsed;
    }

    private void HideControls()
    {
        ControlsVisibility = Visibility.Collapsed;
        VolumeVisibility = Visibility.Collapsed;
        SidetoneVisibility = Visibility.Collapsed;
        GameModeVisibility = Visibility.Collapsed;
        EqPresetVisibility = Visibility.Collapsed;
        AutoPowerOffVisibility = Visibility.Collapsed;
        ChromaBrightnessVisibility = Visibility.Collapsed;
        ChromaEffectVisibility = Visibility.Collapsed;
        ChromaPrimaryColorVisibility = Visibility.Collapsed;
        ChromaSecondaryColorVisibility = Visibility.Collapsed;
        BassVisibility = Visibility.Collapsed;
        HapticsVisibility = Visibility.Collapsed;
        MicEqVisibility = Visibility.Collapsed;
        MicCustomEqVisibility = Visibility.Collapsed;
        CustomEqVisibility = Visibility.Collapsed;
        FirmwareVisibility = Visibility.Collapsed;
        SerialVisibility = Visibility.Collapsed;
        ConnectionVisibility = Visibility.Collapsed;
        ChargingVisibility = Visibility.Collapsed;
    }

    private static int IndexOfPreset(byte preset)
    {
        for (var index = 0; index < EqPresets.Length; index++)
        {
            if (EqPresets[index].Value == preset)
            {
                return index;
            }
        }

        return -1;
    }

    private static int IndexOfMicPreset(byte preset)
    {
        for (var index = 0; index < MicEqPresets.Length; index++)
        {
            if (MicEqPresets[index].Value == preset)
            {
                return index;
            }
        }

        return -1;
    }

    private void ApplyCustomEqBands(IReadOnlyList<sbyte> levels)
    {
        if (levels.Count != RazerAwBleProtocol.CustomEqBandCount)
        {
            return;
        }

        if (CustomEqBands.Count != levels.Count)
        {
            CustomEqBands.Clear();
            for (var index = 0; index < levels.Count; index++)
            {
                CustomEqBands.Add(new RazerAwBleEqBandViewModel(
                    CustomEqFrequencies[index], levels[index]));
            }

            return;
        }

        for (var index = 0; index < levels.Count; index++)
        {
            CustomEqBands[index].Level = levels[index];
        }
    }

    private void ApplyMicCustomEqBands(IReadOnlyList<sbyte> levels)
    {
        if (levels.Count != RazerAwBleProtocol.CustomEqBandCount)
        {
            return;
        }

        if (MicCustomEqBands.Count != levels.Count)
        {
            MicCustomEqBands.Clear();
            for (var index = 0; index < levels.Count; index++)
            {
                MicCustomEqBands.Add(new RazerAwBleEqBandViewModel(
                    MicCustomEqFrequencies[index], levels[index]));
            }

            return;
        }

        for (var index = 0; index < levels.Count; index++)
        {
            MicCustomEqBands[index].Level = levels[index];
        }
    }

    private Visibility SetVisibility(bool visible, ref bool any)
    {
        if (!visible)
        {
            return Visibility.Collapsed;
        }

        any = true;
        ControlsVisibility = Visibility.Visible;
        return Visibility.Visible;
    }

    private static async Task<T?> TryReadAsync<T>(Func<Task<T>> read) where T : struct
    {
        try
        {
            return await read().ConfigureAwait(false);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            return null;
        }
    }

    private static async Task<T?> TryReadReferenceAsync<T>(Func<Task<T>> read) where T : class
    {
        try
        {
            return await read().ConfigureAwait(false);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            return null;
        }
    }
}
