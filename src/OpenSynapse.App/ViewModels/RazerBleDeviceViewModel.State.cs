using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using Windows.UI;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerBleDeviceViewModel
{
    private static readonly RazerBleLightingEffect[] LightingEffects =
    [
        RazerBleLightingEffect.Off,
        RazerBleLightingEffect.Static,
        RazerBleLightingEffect.Breathing,
        RazerBleLightingEffect.Spectrum,
        RazerBleLightingEffect.Wave,
        RazerBleLightingEffect.Reactive,
        RazerBleLightingEffect.Wheel,
    ];

    private void ApplyLighting(RazerBleLightingState state)
    {
        Brightness = state.BrightnessPercent;
        var effectIndex = Array.IndexOf(LightingEffects, (RazerBleLightingEffect)state.EffectId);
        if (effectIndex >= 0)
        {
            SelectedLightingEffectIndex = effectIndex;
        }

        if (state.Direction is 1 or 2 && SelectedLightingEffect == RazerBleLightingEffect.Wave)
        {
            SelectedLightingDirectionIndex = state.Direction == 2 ? 0 : 1;
        }
        else if (state.Direction is 1 or 2 && SelectedLightingEffect == RazerBleLightingEffect.Wheel)
        {
            SelectedLightingDirectionIndex = state.Direction - 1;
        }

        if (state.Colors.Count >= 3)
        {
            LightingColor = Color.FromArgb(255, state.Colors[0], state.Colors[1], state.Colors[2]);
        }

        OnPropertyChanged(nameof(Brightness));
        OnPropertyChanged(nameof(BrightnessText));
        OnPropertyChanged(nameof(SelectedLightingEffectIndex));
        OnPropertyChanged(nameof(SelectedLightingEffect));
        OnPropertyChanged(nameof(LightingDirectionOptions));
        OnPropertyChanged(nameof(SelectedLightingDirectionIndex));
        OnPropertyChanged(nameof(LightingDirectionVisibility));
        OnPropertyChanged(nameof(LightingColor));
        OnPropertyChanged(nameof(LightingColorVisibility));
    }

    private static (byte Flags, byte Rate, byte[] Colors) BuildLightingPayload(
        RazerBleLightingEffect effect,
        Color color,
        int directionIndex)
    {
        byte[] rgb = [color.R, color.G, color.B];
        return effect switch
        {
            RazerBleLightingEffect.Off => (0, 0, []),
            RazerBleLightingEffect.Static => (0, 0, rgb),
            RazerBleLightingEffect.Breathing => (1, 0, rgb),
            RazerBleLightingEffect.Spectrum => (0, 0, []),
            RazerBleLightingEffect.Wave => ((byte)(directionIndex == 0 ? 2 : 1), 40, []),
            RazerBleLightingEffect.Reactive => (0, 1, rgb),
            RazerBleLightingEffect.Wheel => ((byte)(directionIndex + 1), 0, []),
            _ => (0, 0, []),
        };
    }

    private void ApplyDpiStages(RazerBleDpiStages state)
    {
        _confirmedMouseDpiStages = state;
        DpiStages.Clear();
        foreach (var stage in state.Stages)
        {
            DpiStages.Add(new RazerBleDpiStageRowViewModel(stage.Index, stage.X, stage.Y, stage.Z));
        }

        ActiveDpiStage = state.ActiveStage;
    }

    private void ApplyScroll(RazerBleScrollState state)
    {
        if (state.Mode is { } mode && mode <= 2)
        {
            SelectedScrollModeIndex = mode;
        }
        if (state.SmartReel is { } smartReel)
        {
            ScrollSmartReelEnabled = smartReel;
        }
        if (state.SmartReelThreshold is { } threshold)
        {
            ScrollSmartReelThreshold = threshold;
        }
        if (state.Acceleration is { } acceleration)
        {
            ScrollAccelerationEnabled = acceleration;
        }

        ScrollModeVisibility = state.Mode is { } readMode && readMode <= 2
            ? Visibility.Visible
            : Visibility.Collapsed;
        ScrollSmartReelVisibility = state.SmartReel is null
            ? Visibility.Collapsed
            : Visibility.Visible;
        ScrollSmartReelThresholdVisibility = state.SmartReelThreshold is null
            ? Visibility.Collapsed
            : Visibility.Visible;
        ScrollAccelerationVisibility = state.Acceleration is null
            ? Visibility.Collapsed
            : Visibility.Visible;
        ScrollVisibility = state.Mode is not null || state.SmartReel is not null ||
            state.SmartReelThreshold is not null || state.Acceleration is not null
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void ApplyPower(RazerBlePowerState state)
    {
        if (state.LowBatteryPercent is { } lowBattery)
        {
            LowBatteryThresholdPercent = lowBattery;
        }
        if (state.AutoSleep is { } autoSleep)
        {
            AutoSleepEnabled = autoSleep;
        }
        if (state.SleepMinutes is { } sleepMinutes)
        {
            SleepMinutes = sleepMinutes;
        }
        if (state.TimeToDim is { } timeToDim)
        {
            TimeToDimEnabled = timeToDim.Enabled;
            TimeToDimMinutes = Math.Clamp(timeToDim.Seconds / 60d, 1d, 15d);
            _timeToDimPercentage = timeToDim.Percentage is >= 1 ? timeToDim.Percentage : (byte)20; // Synapse uses 20%.
        }

        PowerLowBatteryVisibility = state.LowBatteryPercent is null
            ? Visibility.Collapsed
            : Visibility.Visible;
        PowerAutoSleepVisibility = state.AutoSleep is null
            ? Visibility.Collapsed
            : Visibility.Visible;
        PowerSleepTimeVisibility = state.SleepMinutes is null
            ? Visibility.Collapsed
            : Visibility.Visible;
        PowerTimeToDimVisibility = state.TimeToDim is null
            ? Visibility.Collapsed
            : Visibility.Visible;
        PowerVisibility = state.LowBatteryPercent is not null || state.AutoSleep is not null ||
            state.SleepMinutes is not null || state.TimeToDim is not null
            ? Visibility.Visible
            : Visibility.Collapsed;
        _confirmedMousePower = CaptureMousePower();
    }

    private void Apply(RazerBleDeviceState state)
    {
        _confirmedMousePolling = state.PollingHertz is { } polling && PollingRates.Contains(polling) ? polling : null;
        _confirmedMouseDpi = state.Dpi is { } confirmedDpi ? (confirmedDpi.X, confirmedDpi.Y) : null;
        _firmware = state.Firmware?.ToVersionString();
        _serial = state.SerialNumber;
        _batteryPercent = state.BatteryPercent;
        _chargingStatus = state.ChargingStatus;
        if (state.PollingHertz is { } hertz)
        {
            _selectedPollingIndex = Math.Max(0, PollingRates.ToList().IndexOf(hertz));
        }
        if (state.Dpi is { } dpi)
        {
            _dpiX = dpi.X;
            _dpiY = dpi.Y;
        }

        OnPropertyChanged(nameof(FirmwareText));
        OnPropertyChanged(nameof(SerialText));
        OnPropertyChanged(nameof(BatteryText));
        OnPropertyChanged(nameof(ChargingText));
        OnPropertyChanged(nameof(SelectedPollingIndex));
        OnPropertyChanged(nameof(DpiX));
        OnPropertyChanged(nameof(DpiY));
    }

    private bool BeginOperation()
    {
        if (IsBusy) return false;
        ErrorText = string.Empty;
        OperationStatusText = string.Empty;
        IsBusy = true;
        return true;
    }

    private void EndOperation() => IsBusy = false;
}
