using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Windows.UI;

namespace OpenSynapse.App;

public sealed partial class MainWindow
{
    private bool _touchpadToggleInFlight;
    private Color? _lightingColorBeforeEdit;

    private async void ApplyBrightnessClick(object sender, RoutedEventArgs e) =>
        await _viewModel.ApplyBladeBrightnessAsync(_lifetime.Token);

    private async void ApplyLightingEffectClick(object sender, RoutedEventArgs e) =>
        await _viewModel.ApplySelectedBladeLightingEffectAsync(_lifetime.Token);

    private async void ApplyPerformanceModeClick(object sender, RoutedEventArgs e) =>
        await _viewModel.ApplyBladePerformanceModeAsync(_lifetime.Token);

    private async void AutoApplyComboSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox { IsDropDownOpen: true, Tag: string setting })
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
        _lightingColorBeforeEdit = sender is Flyout { Content: ColorPicker picker }
            ? picker.Color
            : null;
    }

    private async void LightingColorFlyoutClosed(object sender, object e)
    {
        var changed = sender is Flyout { Content: ColorPicker picker } &&
            _lightingColorBeforeEdit is Color previous && picker.Color != previous;
        _lightingColorBeforeEdit = null;
        if (changed)
        {
            await _viewModel.ApplySelectedBladeLightingEffectAsync(_lifetime.Token);
        }
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
