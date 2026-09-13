using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using System.Collections.ObjectModel;
using Windows.UI;

namespace OpenSynapse.App;

public sealed partial class MainWindow
{
    private bool _touchpadToggleInFlight;
    private Color? _lightingColorBeforeEdit;
    private ColorPicker? _activeLightingColorPicker;
    private Flyout? _activeLightingColorFlyout;

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

    private async void ApplyBrightnessClick(object sender, RoutedEventArgs e) =>
        await _viewModel.ApplyBladeBrightnessAsync(_lifetime.Token);

    private async void ApplyLightingEffectClick(object sender, RoutedEventArgs e) =>
        await _viewModel.ApplySelectedBladeLightingEffectAsync(_lifetime.Token);

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
                : null;
        _lightingColorBeforeEdit = _activeLightingColorPicker?.Color;
    }

    private async void LightingColorFlyoutClosed(object sender, object e)
    {
        var picker = _activeLightingColorPicker;
        var changed = picker is not null && _lightingColorBeforeEdit is Color previous &&
            picker.Color != previous;
        if (changed)
        {
            AddRecentLightingColor(picker!.Color);
        }
        _lightingColorBeforeEdit = null;
        _activeLightingColorPicker = null;
        _activeLightingColorFlyout = null;
        if (changed)
        {
            await _viewModel.ApplySelectedBladeLightingEffectAsync(_lifetime.Token);
        }
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
        PrimaryRecentLightingColors.Visibility = Visibility.Visible;
        SecondaryRecentLightingColors.Visibility = Visibility.Visible;
    }

    private static LightingColorOption CreateLightingColor(string hex, byte red, byte green, byte blue) =>
        new(hex, new SolidColorBrush(Color.FromArgb(0xFF, red, green, blue)));

    internal sealed record LightingColorOption(string Hex, SolidColorBrush Brush);

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
