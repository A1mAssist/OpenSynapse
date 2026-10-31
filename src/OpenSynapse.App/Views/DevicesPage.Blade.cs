using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using OpenSynapse.App.ViewModels;
using OpenSynapse.Core.Displays;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Lifecycle;
using OpenSynapse.Windows.Lighting;
using OpenSynapse.Windows.Protocols;
using System.Collections.ObjectModel;
using System.Diagnostics;
using Windows.Storage.Pickers;
using Windows.System;
using Windows.UI;

namespace OpenSynapse.App.Views;

public sealed partial class DevicesPage : UserControl
{
    private bool _touchpadToggleInFlight;
    private bool _bladeLightingSettingsInFlight;
    private bool _bladePerformanceApplyInFlight;
    private bool _bladeBrightnessPointerActive;
    private Color? _lightingColorBeforeEdit;
    private ColorPicker? _activeLightingColorPicker;
    private Flyout? _activeLightingColorFlyout;
    private bool _editingOpenRazerTertiary;
    private Color? _openRazerSecondaryColorBeforeTertiary;

    internal IReadOnlyList<LightingColorOption> LightingPaletteColors { get; } =
    [
        CreateLightingColor("#FFFFFF", 0xFF, 0xFF, 0xFF),
        CreateLightingColor("#FF5252", 0xFF, 0x52, 0x52),
        CreateLightingColor("#FF8A3D", 0xFF, 0x8A, 0x3D),
        CreateLightingColor("#FFD740", 0xFF, 0xD7, 0x40),
        CreateLightingColor("#9BE564", 0x9B, 0xE5, 0x64),
        CreateLightingColor("#42D67B", 0x42, 0xD6, 0x7B),
        CreateLightingColor("#38D9C5", 0x38, 0xD9, 0xC5),
        CreateLightingColor("#4FC3F7", 0x4F, 0xC3, 0xF7),
        CreateLightingColor("#448AFF", 0x44, 0x8A, 0xFF),
        CreateLightingColor("#536DFE", 0x53, 0x6D, 0xFE),
        CreateLightingColor("#7C5CFC", 0x7C, 0x5C, 0xFC),
        CreateLightingColor("#B968FF", 0xB9, 0x68, 0xFF),
        CreateLightingColor("#EC5AC8", 0xEC, 0x5A, 0xC8),
        CreateLightingColor("#FF5C93", 0xFF, 0x5C, 0x93),
        CreateLightingColor("#B0BEC5", 0xB0, 0xBE, 0xC5),
        CreateLightingColor("#202020", 0x20, 0x20, 0x20),
    ];

    internal ObservableCollection<LightingColorOption> RecentLightingColors { get; } = [];
    internal ObservableCollection<LightingPresetOption> LightingPresetOptions { get; } = [];

    private static IReadOnlyList<LightingPresetOption> BuiltInLightingPresets =>
    [
        CreateLightingPreset(AppStrings.Text("LightingPresetFire"), "#FF2000", "#FF9000", "#FFF0A0"),
        CreateLightingPreset(AppStrings.Text("LightingPresetStarlight"), "#AA33FF", "#3355FF", "#FFFFFF"),
        CreateLightingPreset(AppStrings.Text("LightingPresetRainbow"), "#FF0000", "#00FF00", "#0000FF"),
        CreateLightingPreset(AppStrings.Text("LightingPresetAudio"), "#00FF66", "#FFFF00", "#FF0033"),
    ];

    private async void ApplyBrightnessClick(object sender, RoutedEventArgs e) =>
        await ViewModel.Blade.ApplyBrightnessAsync(Lifetime);

    private async void ApplyLightingEffectClick(object sender, RoutedEventArgs e) =>
        await ViewModel.Blade.ApplySelectedLightingEffectAsync(Lifetime);

    private async void LightingPresetButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: LightingPresetOption preset }) return;
        await ApplyLightingPresetAsync("openrazer", preset);
    }

    private async void BladeLightingPresetButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: LightingPresetOption preset }) await ApplyLightingPresetAsync("blade", preset);
    }

    private async void ImportBladeChromaClick(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
        };
        picker.FileTypeFilter.Add(".chroma");
        Host.InitializePicker(picker);
        var file = await picker.PickSingleFileAsync();
        if (file is not null && !string.IsNullOrWhiteSpace(file.Path))
        {
            await ViewModel.Blade.ImportChromaFileAsync(file.Path, Lifetime);
        }
    }

    private async void KrakenLightingPresetButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: LightingPresetOption preset }) await ApplyLightingPresetAsync("kraken", preset);
    }

    private async Task ApplyLightingPresetAsync(string target, LightingPresetOption preset)
    {
        if (target == "blade")
        {
            // Ask the view model which effect is selected; the admitted list differs per
            // Blade generation, so the page must not index the full catalog itself.
            if (ViewModel.Blade.SelectedLightingModeForPage == BladeLightingMode.Starlight)
            {
                ViewModel.Blade.StarlightColorModeIndex = 2;
            }
            ViewModel.Blade.LightingColor = preset.Primary;
            ViewModel.Blade.LightingSecondColor = preset.Secondary;
            ViewModel.Blade.LightingTertiaryColor = preset.Tertiary;
            await ViewModel.Blade.ApplySelectedLightingEffectAsync(Lifetime);
        }
        else if (target == "openrazer" && SelectedOpenRazerDevice is { } device)
        {
            if ((device.SelectedLightingEffect is OpenRazerLightingEffect.StarlightRandom or
                OpenRazerLightingEffect.StarlightSingle) &&
                device.LightingEffects.Contains(OpenRazerLightingEffect.StarlightDual))
            {
                device.SelectedLightingEffect = OpenRazerLightingEffect.StarlightDual;
            }
            device.PrimaryColor = preset.Primary;
            device.SecondaryColor = preset.Secondary;
            device.TertiaryColor = preset.Tertiary;
            await device.ApplyLightingAsync(Lifetime);
        }
        else if (target == "kraken" && SelectedOpenRazerKraken is { } kraken)
        {
            kraken.Primary = preset.Primary;
            kraken.Secondary = preset.Secondary;
            kraken.Tertiary = preset.Tertiary;
            await kraken.ApplyAsync(Lifetime);
        }
    }

    internal void RefreshLightingPresetOptions()
    {
        LightingPresetOptions.Clear();
        foreach (var preset in BuiltInLightingPresets) LightingPresetOptions.Add(preset);
    }

    private async void ApplyPerformanceModeClick(object sender, RoutedEventArgs e) =>
        await ViewModel.Blade.ApplyPerformanceModeAsync(Lifetime);

    private async void CpuCurveApplyClick(object sender, RoutedEventArgs e) =>
        await ViewModel.CpuCurve.ApplyAsync(Lifetime);

    private async void ApplyBladeGpuClockOffsetClick(object sender, RoutedEventArgs e) =>
        await ViewModel.Blade.ApplyGpuClockOffsetAsync(Lifetime);

    private async void ResetBladeGpuClockOffsetClick(object sender, RoutedEventArgs e) =>
        await ViewModel.Blade.ResetGpuClockOffsetDefaultsAsync(Lifetime);

    internal async Task<bool> ConfirmPadlessHyperBoostAsync()
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = AppStrings.Text("BladeHyperboostDisclaimerTitle"),
            Content = AppStrings.Text("BladeHyperboostDisclaimerMessage"),
            PrimaryButtonText = AppStrings.Text("BladeHyperboostDisclaimerAccept"),
            CloseButtonText = AppStrings.Text("Text_949856B3"),
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return false;
        }

        ViewModel.Blade.SetPadlessHyperBoostAccepted(true);
        if (!Host.BehaviorSettings.PadlessHyperBoostAccepted)
        {
            Host.BehaviorSettings.PadlessHyperBoostAccepted = true;
            try
            {
                Host.BehaviorSettings.Save();
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or
                    System.Security.SecurityException or InvalidOperationException)
            {
                ViewModel.ReportApplicationError(AppStrings.FormatText(
                    "BehaviorSettingError",
                    exception.Message));
            }
        }

        return true;
    }

    private async void AutoApplyComboSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox combo || combo.Tag is not string setting ||
            (!combo.IsDropDownOpen && combo.FocusState == FocusState.Unfocused))
        {
            return;
        }

        switch (setting)
        {
            case "performance":
                if (_bladePerformanceApplyInFlight)
                {
                    return;
                }
                _bladePerformanceApplyInFlight = true;
                try
                {
                    await ViewModel.Blade.ApplyPerformanceModeAsync(Lifetime);
                }
                finally
                {
                    _bladePerformanceApplyInFlight = false;
                }
                break;
            case "charge":
                await ViewModel.Blade.ApplyChargeLimitAsync(Lifetime);
                break;
            case "cpuBoost":
                await ViewModel.Blade.ApplyCpuBoostAsync(Lifetime);
                break;
            case "cpuPowerLimit":
                if (ViewModel.IsRefreshingCpuPowerLimit)
                {
                    return;
                }
                await ViewModel.ApplyCpuPowerLimitAsync(Lifetime);
                break;
            case "gpuBoost":
                await ViewModel.Blade.ApplyGpuBoostAsync(Lifetime);
                break;
            case "lighting":
                await ViewModel.Blade.ApplySelectedLightingEffectAsync(Lifetime);
                break;
            case "logo":
                await ViewModel.Blade.ApplyLogoAsync(Lifetime);
                break;
            case "refreshRate":
            await ViewModel.Display.ApplyInternalDisplayRefreshRateAsync(Lifetime);
                break;
            case "nativeDisplayMode":
                await ViewModel.Blade.ApplyNativeDisplayModeAsync(Lifetime);
                break;
        }
    }

    private async void BladeFanAutomaticModeClick(object sender, RoutedEventArgs e) =>
        await ViewModel.Fan.SelectBladeFanAutomaticModeAsync(Lifetime);

    private void BladeFanFixedModeClick(object sender, RoutedEventArgs e) =>
        ViewModel.Fan.ShowBladeFanFixedMode();

    private void BladeFanCurveModeClick(object sender, RoutedEventArgs e) =>
        ViewModel.Fan.ShowBladeFanCurveMode();

    private async void ApplyBladeFanFixedRpmClick(object sender, RoutedEventArgs e) =>
        await ViewModel.Fan.SelectBladeFanFixedModeAsync(Lifetime);

    private async void AutoApplyMaxFanToggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch { FocusState: not FocusState.Unfocused })
        {
            await ViewModel.Blade.ApplyMaxFanAsync(Lifetime);
        }
    }

    private async void AutoApplyBladeLightingSettingsToggled(object sender, RoutedEventArgs e)
    {
        if (_bladeLightingSettingsInFlight || sender is not ToggleSwitch { FocusState: not FocusState.Unfocused })
        {
            return;
        }

        _bladeLightingSettingsInFlight = true;
        try
        {
            await ViewModel.Blade.ApplyLightingSettingsAsync(Lifetime);
        }
        finally
        {
            _bladeLightingSettingsInFlight = false;
        }
    }

    private async void AutoApplyPlatformToggleToggled(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleSwitch { FocusState: not FocusState.Unfocused, Tag: string setting })
        {
            return;
        }

        switch (setting)
        {
            case "gamingMode":
                await ViewModel.Blade.ApplyGamingModeAsync(Lifetime);
                break;
            case "startupAnimation":
                await ViewModel.Blade.ApplyStartupAnimationAsync(Lifetime);
                break;
            case "oneTimeFullCharge":
                await ViewModel.Blade.ApplyOneTimeFullChargeAsync(Lifetime);
                break;
            case "localDimming":
                await ViewModel.Blade.ApplyLocalDimmingAsync(Lifetime);
                break;
            case "cpuPowerBoost":
                await ViewModel.Blade.ApplyCpuPowerBoostAsync(Lifetime);
                break;
        }
    }

    private async void AutoApplyBrightnessValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_bladeBrightnessPointerActive && sender is Slider)
        {
            await ViewModel.Blade.QueueBrightnessAsync(Lifetime);
        }
    }

    private async void ScreenBrightnessValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (sender is Slider)
        {
            await ViewModel.Display.QueueInternalDisplayBrightnessAsync(e.NewValue, Lifetime);
        }
    }

    private void BladeBrightnessPointerPressed(object sender, PointerRoutedEventArgs e) =>
        _bladeBrightnessPointerActive = true;

    private async void BladeBrightnessPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _bladeBrightnessPointerActive = false;
        await ViewModel.Blade.QueueBrightnessAsync(Lifetime);
    }

    private void BladeBrightnessPointerCaptureLost(object sender, PointerRoutedEventArgs e) =>
        _bladeBrightnessPointerActive = false;

    private void LightingColorFlyoutOpened(object sender, object e)
    {
        _activeLightingColorFlyout = sender as Flyout;
        _activeLightingColorPicker = ReferenceEquals(sender, PrimaryLightingColorFlyout)
            ? PrimaryLightingColorPicker
            : ReferenceEquals(sender, SecondaryLightingColorFlyout)
                ? SecondaryLightingColorPicker
                : ReferenceEquals(sender, TertiaryLightingColorFlyout)
                    ? TertiaryLightingColorPicker
                : ReferenceEquals(sender, OpenRazerPrimaryColorFlyout)
                    ? OpenRazerPrimaryColorPicker
                    : ReferenceEquals(sender, OpenRazerSecondaryColorFlyout)
                        ? OpenRazerSecondaryColorPicker
                        : ReferenceEquals(sender, KrakenPrimaryColorFlyout)
                            ? KrakenPrimaryColorPicker
                            : ReferenceEquals(sender, KrakenSecondaryColorFlyout)
                                ? KrakenSecondaryColorPicker
                                : ReferenceEquals(sender, KrakenTertiaryColorFlyout)
                                    ? KrakenTertiaryColorPicker
                : null;
        _lightingColorBeforeEdit = _activeLightingColorPicker?.Color;
        UpdateRecentLightingColorVisibility();
    }

    private async void LightingColorFlyoutClosed(object sender, object e)
    {
        var picker = _activeLightingColorPicker;
        var changed = picker is not null && _lightingColorBeforeEdit is Color previous &&
            picker.Color != previous;
        var editingOpenRazerTertiary = _editingOpenRazerTertiary;
        var openRazerSecondaryColorBeforeTertiary = _openRazerSecondaryColorBeforeTertiary;
        if (changed)
        {
            AddRecentLightingColor(picker!.Color);
        }
        _lightingColorBeforeEdit = null;
        _activeLightingColorPicker = null;
        _activeLightingColorFlyout = null;
        _editingOpenRazerTertiary = false;

        if (editingOpenRazerTertiary &&
            openRazerSecondaryColorBeforeTertiary is Color secondaryBeforeTertiary &&
            SelectedOpenRazerDevice is { } openRazerForRestore)
        {
            openRazerForRestore.SecondaryColor = secondaryBeforeTertiary;
        }
        _openRazerSecondaryColorBeforeTertiary = null;

        if (!changed)
        {
            return;
        }

        if (ReferenceEquals(sender, PrimaryLightingColorFlyout) ||
            ReferenceEquals(sender, SecondaryLightingColorFlyout) ||
            ReferenceEquals(sender, TertiaryLightingColorFlyout))
        {
            await ViewModel.Blade.ApplySelectedLightingEffectAsync(Lifetime);
        }
        else if (ReferenceEquals(sender, OpenRazerPrimaryColorFlyout) ||
            ReferenceEquals(sender, OpenRazerSecondaryColorFlyout))
        {
            if (SelectedOpenRazerDevice is { } openRazer)
            {
                if (ReferenceEquals(sender, OpenRazerPrimaryColorFlyout))
                    openRazer.PrimaryColor = picker!.Color;
                else if (editingOpenRazerTertiary)
                    openRazer.TertiaryColor = picker!.Color;
                else if (ReferenceEquals(sender, OpenRazerSecondaryColorFlyout))
                    openRazer.SecondaryColor = picker!.Color;
                await openRazer.ApplyLightingAsync(Lifetime);
            }
        }
        else if (ReferenceEquals(sender, KrakenPrimaryColorFlyout) ||
            ReferenceEquals(sender, KrakenSecondaryColorFlyout) ||
            ReferenceEquals(sender, KrakenTertiaryColorFlyout))
        {
            if (ViewModel.OpenRazer.SelectedKraken is { } kraken)
            {
                await kraken.ApplyAsync(Lifetime);
            }
        }
    }

    private void OpenRazerTertiaryColorButtonClick(object sender, RoutedEventArgs e)
    {
        if (SelectedOpenRazerDevice is not { } openRazer ||
            sender is not FrameworkElement target)
        {
            return;
        }

        _editingOpenRazerTertiary = true;
        _openRazerSecondaryColorBeforeTertiary = openRazer.SecondaryColor;
        OpenRazerSecondaryColorPicker.Color = openRazer.TertiaryColor;
        OpenRazerSecondaryColorFlyout.ShowAt(target);
    }

    private void LightingColorSwatchClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: LightingColorOption option } ||
            _activeLightingColorPicker is null)
        {
            return;
        }

        _activeLightingColorPicker.Color = option.Brush.Color;
        AddRecentLightingColor(option.Brush.Color);
        _activeLightingColorFlyout?.Hide();
    }

    private void AddRecentLightingColor(Color color)
    {
        var hex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        var existing = RecentLightingColors.FirstOrDefault(item => item.Hex == hex);
        if (existing is not null)
        {
            RecentLightingColors.Remove(existing);
        }
        RecentLightingColors.Insert(0, new LightingColorOption(hex, new SolidColorBrush(color)));
        while (RecentLightingColors.Count > 6)
        {
            RecentLightingColors.RemoveAt(RecentLightingColors.Count - 1);
        }
        UpdateRecentLightingColorVisibility();
    }

    private void UpdateRecentLightingColorVisibility()
    {
        var visibility = RecentLightingColors.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (PrimaryRecentLightingColors is { } bladePrimary) bladePrimary.Visibility = visibility;
        if (SecondaryRecentLightingColors is { } bladeSecondary) bladeSecondary.Visibility = visibility;
        if (OpenRazerPrimaryRecentColors is { } openRazerPrimary) openRazerPrimary.Visibility = visibility;
        if (OpenRazerSecondaryRecentColors is { } openRazerSecondary) openRazerSecondary.Visibility = visibility;
        if (KrakenPrimaryRecentColors is { } krakenPrimary) krakenPrimary.Visibility = visibility;
        if (KrakenSecondaryRecentColors is { } krakenSecondary) krakenSecondary.Visibility = visibility;
        if (KrakenTertiaryRecentColors is { } krakenTertiary) krakenTertiary.Visibility = visibility;
    }

    private static LightingColorOption CreateLightingColor(string hex, byte red, byte green, byte blue) =>
        new(hex, new SolidColorBrush(Color.FromArgb(0xFF, red, green, blue)));

    private static LightingPresetOption CreateLightingPreset(string name, string primary, string secondary, string tertiary) =>
        new(name, ParseColor(primary), ParseColor(secondary), ParseColor(tertiary));

    private static Color ParseColor(string value) =>
        TryParseColor(value, out var color) ? color : Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);

    private static bool TryParseColor(string value, out Color color)
    {
        color = default;
        var hex = value?.Trim().TrimStart('#');
        if (hex is null || hex.Length != 6 ||
            !byte.TryParse(hex[..2], System.Globalization.NumberStyles.HexNumber, null, out var red) ||
            !byte.TryParse(hex[2..4], System.Globalization.NumberStyles.HexNumber, null, out var green) ||
            !byte.TryParse(hex[4..], System.Globalization.NumberStyles.HexNumber, null, out var blue)) return false;
        color = Color.FromArgb(0xFF, red, green, blue);
        return true;
    }

    internal sealed record LightingColorOption(string Hex, SolidColorBrush Brush);
    internal sealed record LightingPresetOption(string Name, Color Primary, Color Secondary, Color Tertiary)
    {
        public SolidColorBrush PrimaryBrush => new(Primary);
        public SolidColorBrush SecondaryBrush => new(Secondary);
        public SolidColorBrush TertiaryBrush => new(Tertiary);
    }

    private async void ApplyChargeLimitClick(object sender, RoutedEventArgs e) =>
        await ViewModel.Blade.ApplyChargeLimitAsync(Lifetime);

    private async void ApplyBladeBatteryOptimizerClick(object sender, RoutedEventArgs e) =>
        await ViewModel.Blade.BatteryOptimizer.ApplyAsync(Lifetime);

    private async void ApplyCpuBoostClick(object sender, RoutedEventArgs e) =>
        await ViewModel.Blade.ApplyCpuBoostAsync(Lifetime);

    private async void ApplyGpuBoostClick(object sender, RoutedEventArgs e) =>
        await ViewModel.Blade.ApplyGpuBoostAsync(Lifetime);

    private async void ApplyMaxFanClick(object sender, RoutedEventArgs e) =>
        await ViewModel.Blade.ApplyMaxFanAsync(Lifetime);

    private async void ApplyLogoClick(object sender, RoutedEventArgs e) =>
        await ViewModel.Blade.ApplyLogoAsync(Lifetime);

    private async void CheckFirmwareUpdateClick(object sender, RoutedEventArgs e) =>
        await ViewModel.Firmware.CheckUpdateAsync(Lifetime);

    private async void OpenFirmwareDownloadClick(object sender, RoutedEventArgs e) =>
        await Launcher.LaunchUriAsync(new Uri(ViewModel.Firmware.DownloadUrl));

    private async void ToggleTouchpadToggled(object sender, RoutedEventArgs e)
    {
        if (_touchpadToggleInFlight || sender is not ToggleSwitch { FocusState: not FocusState.Unfocused })
        {
            return;
        }

        _touchpadToggleInFlight = true;
        try
        {
            await ViewModel.Blade.ToggleTouchpadAsync(Lifetime);
        }
        finally
        {
            _touchpadToggleInFlight = false;
        }
    }
}
