using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using OpenSynapse.App.ViewModels;
using OpenSynapse.Windows.Lighting;
using OpenSynapse.Windows.Protocols;
using System.Collections.ObjectModel;
using Windows.UI;

namespace OpenSynapse.App;

public sealed partial class MainWindow
{
    private bool _touchpadToggleInFlight;
    private bool _bladeLightingSettingsInFlight;
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
        await _viewModel.ApplyBladeBrightnessAsync(_lifetime.Token);

    private async void ApplyLightingEffectClick(object sender, RoutedEventArgs e) =>
        await _viewModel.ApplySelectedBladeLightingEffectAsync(_lifetime.Token);

    private async void LightingPresetButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: LightingPresetOption preset }) return;
        await ApplyLightingPresetAsync("openrazer", preset);
    }

    private async void BladeLightingPresetButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: LightingPresetOption preset }) await ApplyLightingPresetAsync("blade", preset);
    }

    private async void KrakenLightingPresetButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: LightingPresetOption preset }) await ApplyLightingPresetAsync("kraken", preset);
    }

    private async Task ApplyLightingPresetAsync(string target, LightingPresetOption preset)
    {
        if (target == "blade")
        {
            if (_viewModel.BladeLightingModeIndex >= 0 &&
                _viewModel.BladeLightingModeIndex < DeviceUiCatalog.BladeLightingModes.Length &&
                DeviceUiCatalog.BladeLightingModes[_viewModel.BladeLightingModeIndex] == BladeLightingMode.Starlight)
            {
                _viewModel.BladeStarlightColorModeIndex = 2;
            }
            _viewModel.BladeLightingColor = preset.Primary;
            _viewModel.BladeLightingSecondColor = preset.Secondary;
            _viewModel.BladeLightingTertiaryColor = preset.Tertiary;
            await _viewModel.ApplySelectedBladeLightingEffectAsync(_lifetime.Token);
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
            await device.ApplyLightingAsync(_lifetime.Token);
        }
        else if (target == "kraken" && SelectedOpenRazerKraken is { } kraken)
        {
            kraken.Primary = preset.Primary;
            kraken.Secondary = preset.Secondary;
            kraken.Tertiary = preset.Tertiary;
            await kraken.ApplyAsync(_lifetime.Token);
        }
    }

    internal void RefreshLightingPresetOptions()
    {
        LightingPresetOptions.Clear();
        foreach (var preset in BuiltInLightingPresets) LightingPresetOptions.Add(preset);
    }

    private async void ApplyPerformanceModeClick(object sender, RoutedEventArgs e) =>
        await _viewModel.ApplyBladePerformanceModeAsync(_lifetime.Token);

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
                await _viewModel.ApplyBladePerformanceModeAsync(_lifetime.Token);
                break;
            case "charge":
                await _viewModel.ApplyBladeChargeLimitAsync(_lifetime.Token);
                break;
            case "cpuBoost":
                await _viewModel.ApplyBladeCpuBoostAsync(_lifetime.Token);
                break;
            case "gpuBoost":
                await _viewModel.ApplyBladeGpuBoostAsync(_lifetime.Token);
                break;
            case "lighting":
                await _viewModel.ApplySelectedBladeLightingEffectAsync(_lifetime.Token);
                break;
            case "logo":
                await _viewModel.ApplyBladeLogoAsync(_lifetime.Token);
                break;
            case "refreshRate":
                await _viewModel.ApplyInternalDisplayRefreshRateAsync(_lifetime.Token);
                break;
        }
    }

    private async void AutoApplyMaxFanToggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch { FocusState: not FocusState.Unfocused })
        {
            await _viewModel.ApplyBladeMaxFanAsync(_lifetime.Token);
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
            await _viewModel.ApplyBladeLightingSettingsAsync(_lifetime.Token);
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
                await _viewModel.ApplyBladeGamingModeAsync(_lifetime.Token);
                break;
            case "startupAnimation":
                await _viewModel.ApplyBladeStartupAnimationAsync(_lifetime.Token);
                break;
            case "oneTimeFullCharge":
                await _viewModel.ApplyBladeOneTimeFullChargeAsync(_lifetime.Token);
                break;
        }
    }

    private async void AutoApplyBrightnessValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (sender is Slider { FocusState: not FocusState.Unfocused })
        {
            await _viewModel.ApplyBladeBrightnessAsync(_lifetime.Token);
        }
    }

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
            await _viewModel.ApplySelectedBladeLightingEffectAsync(_lifetime.Token);
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
                await openRazer.ApplyLightingAsync(_lifetime.Token);
            }
        }
        else if (ReferenceEquals(sender, KrakenPrimaryColorFlyout) ||
            ReferenceEquals(sender, KrakenSecondaryColorFlyout) ||
            ReferenceEquals(sender, KrakenTertiaryColorFlyout))
        {
            if (_viewModel.SelectedOpenRazerKraken is { } kraken)
            {
                await kraken.ApplyAsync(_lifetime.Token);
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
        await _viewModel.ApplyBladeChargeLimitAsync(_lifetime.Token);

    private async void ApplyCpuBoostClick(object sender, RoutedEventArgs e) =>
        await _viewModel.ApplyBladeCpuBoostAsync(_lifetime.Token);

    private async void ApplyGpuBoostClick(object sender, RoutedEventArgs e) =>
        await _viewModel.ApplyBladeGpuBoostAsync(_lifetime.Token);

    private async void ApplyMaxFanClick(object sender, RoutedEventArgs e) =>
        await _viewModel.ApplyBladeMaxFanAsync(_lifetime.Token);

    private async void ApplyLogoClick(object sender, RoutedEventArgs e) =>
        await _viewModel.ApplyBladeLogoAsync(_lifetime.Token);

    private async void ToggleTouchpadToggled(object sender, RoutedEventArgs e)
    {
        if (_touchpadToggleInFlight || sender is not ToggleSwitch { FocusState: not FocusState.Unfocused })
        {
            return;
        }

        _touchpadToggleInFlight = true;
        try
        {
            await _viewModel.ToggleBladeTouchpadAsync(_lifetime.Token);
        }
        finally
        {
            _touchpadToggleInFlight = false;
        }
    }
}
