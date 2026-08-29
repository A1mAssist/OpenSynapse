using Windows.UI;
using OpenSynapse.Core.Devices;
using static OpenSynapse.App.ViewModels.DeviceUiCatalog;

namespace OpenSynapse.App.ViewModels;

// Owns Blade-facing UI state; MainViewModel remains the hardware/profile coordinator.
internal sealed class BladeViewModel
{
    internal string _bladeDeviceName = "Razer Blade 16 2025";
    internal string _bladeStatusText = AppStrings.Text("Text_DB0974DC");
    internal string _bladeBrightnessText = "--";
    internal string _bladeBrightnessSelectionText = "--";
    internal double _bladeBrightnessPercent;
    internal double _confirmedBladeBrightnessPercent;
    internal bool _canSetBladeBrightness;
    internal string _bladePerformanceModeText = "--";
    internal int _bladePerformanceModeIndex = -1;
    internal int _confirmedBladePerformanceModeIndex = -1;
    internal bool _canSetBladePerformanceMode;
    internal string _bladeFanText = "--";
    internal string _bladeFanModeText = "--";
    internal string _bladeFanTargetRpmText = "--";
    internal string _bladeCurrentFanCpuRpmText = "--";
    internal string _bladeCurrentFanGpuRpmText = "--";
    internal string _bladeAdvancedFanCpuModeRawText = "--";
    internal string _bladeAdvancedFanGpuModeRawText = "--";
    internal string _bladeGameModeText = "--";
    internal byte? _bladeGameModeState;
    internal bool _bladeGameModeWriteSupported = true;
    internal bool _bladeGameModeEnabled;
    internal string _bladeStartupAnimationText = "--";
    internal bool? _bladeStartupAnimationEnabled;
    internal bool _bladeStartupAnimationSelection;
    internal string _bladeNativeDisplayModeText = "--";
    internal string _bladeSkuHardwareText = "--";
    internal string _bladeLocalDimmingText = "--";
    internal string _bladeOneTimeFullChargeText = "--";
    internal bool? _bladeOneTimeFullChargeEnabled;
    internal bool _bladeOneTimeFullChargeSelection;
    internal string _bladeChargeLimitText = "--";
    internal int _bladeChargeLimitIndex = -1;
    internal int _confirmedBladeChargeLimitIndex = -1;
    internal bool _canSetBladeChargeLimit;
    internal string _bladeCpuBoostText = "--";
    internal int _bladeCpuBoostIndex = -1;
    internal int _confirmedBladeCpuBoostIndex = -1;
    internal bool _hasBladeCpuBoost;
    internal string _bladeGpuBoostText = "--";
    internal int _bladeGpuBoostIndex = -1;
    internal int _confirmedBladeGpuBoostIndex = -1;
    internal bool _hasBladeGpuBoost;
    internal string _bladeMaxFanText = "--";
    internal bool _bladeMaxFanEnabled;
    internal bool _confirmedBladeMaxFanEnabled;
    internal bool _hasBladeMaxFan;
    internal string _bladeLogoText = "--";
    internal int _bladeLogoIndex = -1;
    internal int _confirmedBladeLogoIndex = -1;
    internal bool _canSetBladeLogo;
    internal string _bladeTouchpadText = "--";
    internal bool _bladeTouchpadEnabled;
    internal bool _confirmedBladeTouchpadEnabled;
    internal bool _canSetBladeTouchpad;
    internal int _bladeLightingModeIndex = 1;
    internal int _bladeWaveDirectionIndex;
    internal Color _bladeLightingColor = Color.FromArgb(0xFF, 0x99, 0xDD, 0x72);
    internal Color _bladeLightingSecondColor = Color.FromArgb(0xFF, 0x00, 0x66, 0xFF);

    internal bool IsCustomMode =>
        _confirmedBladePerformanceModeIndex >= 0 &&
        BladePerformanceModes[_confirmedBladePerformanceModeIndex] == BladePerformanceMode.Custom;

    internal void SetPerformanceMode(BladePerformanceMode mode)
    {
        var modeChanged = _confirmedBladePerformanceModeIndex < 0 ||
            BladePerformanceModes[_confirmedBladePerformanceModeIndex] != mode;
        _bladePerformanceModeText = mode switch
        {
            BladePerformanceMode.Balanced => AppStrings.Text("Text_9753B259"),
            BladePerformanceMode.Performance => AppStrings.Text("Text_C1DB7AE1"),
            BladePerformanceMode.BatterySaver => AppStrings.Text("Text_EE43F0B1"),
            BladePerformanceMode.Custom => AppStrings.Text("Text_598C5804"),
            BladePerformanceMode.Silent => AppStrings.Text("Text_60E54E25"),
            BladePerformanceMode.BalancedDc => AppStrings.Text("Text_A8CF66DD"),
            BladePerformanceMode.Hyperboost => "HyperBoost",
            _ => "--",
        };
        _bladePerformanceModeIndex = Array.IndexOf(BladePerformanceModes, mode);
        _confirmedBladePerformanceModeIndex = _bladePerformanceModeIndex;
        if (modeChanged)
        {
            ClearCustomPerformance();
        }
    }

    internal void SetGameMode(BladeGameModeTelemetry? gameMode)
    {
        _bladeGameModeState = gameMode?.GameMode;
        _bladeGameModeEnabled = gameMode is { GameMode: not 0 };
        _bladeGameModeText = gameMode is { GameMode: not 0 }
            ? AppStrings.Text("Text_F55AD712")
            : gameMode is not null
                ? AppStrings.Text("Text_5DBCDF6D")
                : "--";
    }

    internal void SetChargeLimit(int percent)
    {
        _bladeChargeLimitText = percent == 100 ? AppStrings.Text("Text_C85491EA") : $"{percent}%";
        _bladeChargeLimitIndex = Array.IndexOf(BladeChargeLimits, percent);
        _confirmedBladeChargeLimitIndex = _bladeChargeLimitIndex;
    }

    internal void ClearCustomPerformance()
    {
        _bladeCpuBoostText = "--";
        _bladeCpuBoostIndex = -1;
        _confirmedBladeCpuBoostIndex = -1;
        _hasBladeCpuBoost = false;
        _bladeGpuBoostText = "--";
        _bladeGpuBoostIndex = -1;
        _confirmedBladeGpuBoostIndex = -1;
        _hasBladeGpuBoost = false;
        _bladeMaxFanText = "--";
        _bladeMaxFanEnabled = false;
        _confirmedBladeMaxFanEnabled = false;
        _hasBladeMaxFan = false;
    }

    internal void SetCpuBoost(BladeCpuBoostMode mode)
    {
        _bladeCpuBoostText = mode switch
        {
            BladeCpuBoostMode.Low => AppStrings.Text("Text_CB5F70D1"),
            BladeCpuBoostMode.Medium => AppStrings.Text("Text_28619638"),
            BladeCpuBoostMode.High => AppStrings.Text("Text_5C1F32A7"),
            BladeCpuBoostMode.Boost => "Boost",
            BladeCpuBoostMode.Undervolt => AppStrings.Text("Text_BAA7D10B"),
            _ => "--",
        };
        _bladeCpuBoostIndex = Array.IndexOf(BladeCpuBoostModes, mode);
        _confirmedBladeCpuBoostIndex = _bladeCpuBoostIndex;
        _hasBladeCpuBoost = _bladeCpuBoostIndex >= 0;
    }

    internal void SetGpuBoost(BladeGpuBoostMode mode)
    {
        _bladeGpuBoostText = mode switch
        {
            BladeGpuBoostMode.Low => AppStrings.Text("Text_CB5F70D1"),
            BladeGpuBoostMode.Medium => AppStrings.Text("Text_28619638"),
            BladeGpuBoostMode.High => AppStrings.Text("Text_5C1F32A7"),
            _ => "--",
        };
        _bladeGpuBoostIndex = Array.IndexOf(BladeGpuBoostModes, mode);
        _confirmedBladeGpuBoostIndex = _bladeGpuBoostIndex;
        _hasBladeGpuBoost = _bladeGpuBoostIndex >= 0;
    }

    internal void SetMaxFan(BladeMaxFanMode mode)
    {
        _bladeMaxFanText = mode == BladeMaxFanMode.Enabled ? AppStrings.Text("Text_7E6D2390") : AppStrings.Text("Text_39B523BD");
        _bladeMaxFanEnabled = mode == BladeMaxFanMode.Enabled;
        _confirmedBladeMaxFanEnabled = _bladeMaxFanEnabled;
        _hasBladeMaxFan = true;
    }

    internal void SetLogo(BladeLogoMode mode)
    {
        _bladeLogoText = mode switch
        {
            BladeLogoMode.Off => AppStrings.Text("Text_39B523BD"),
            BladeLogoMode.Static => AppStrings.Text("Text_6295D9CB"),
            BladeLogoMode.Breathing => AppStrings.Text("Text_7EDA32B9"),
            _ => "--",
        };
        _bladeLogoIndex = Array.IndexOf(BladeLogoModes, mode);
        _confirmedBladeLogoIndex = _bladeLogoIndex;
    }
}
