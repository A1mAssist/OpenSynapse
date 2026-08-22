using Windows.UI;
using OpenSynapse.Core.Devices;
using static OpenSynapse.App.ViewModels.DeviceUiCatalog;

namespace OpenSynapse.App.ViewModels;

// Owns Blade-facing UI state; MainViewModel remains the hardware/profile coordinator.
internal sealed class BladeViewModel
{
    internal string _bladeDeviceName = "Razer Blade 16 2025";
    internal string _bladeStatusText = "未发现";
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
            BladePerformanceMode.Balanced => "平衡",
            BladePerformanceMode.Performance => "性能",
            BladePerformanceMode.BatterySaver => "电池节能",
            BladePerformanceMode.Custom => "自定义",
            BladePerformanceMode.Silent => "静音",
            BladePerformanceMode.BalancedDc => "平衡（电池）",
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
            ? "已启用"
            : gameMode is not null
                ? "已关闭"
                : "--";
    }

    internal void SetChargeLimit(int percent)
    {
        _bladeChargeLimitText = percent == 100 ? "关闭 · 100%" : $"{percent}%";
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
            BladeCpuBoostMode.Low => "低",
            BladeCpuBoostMode.Medium => "中",
            BladeCpuBoostMode.High => "高",
            BladeCpuBoostMode.Boost => "Boost",
            BladeCpuBoostMode.Undervolt => "降压预设",
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
            BladeGpuBoostMode.Low => "低",
            BladeGpuBoostMode.Medium => "中",
            BladeGpuBoostMode.High => "高",
            _ => "--",
        };
        _bladeGpuBoostIndex = Array.IndexOf(BladeGpuBoostModes, mode);
        _confirmedBladeGpuBoostIndex = _bladeGpuBoostIndex;
        _hasBladeGpuBoost = _bladeGpuBoostIndex >= 0;
    }

    internal void SetMaxFan(BladeMaxFanMode mode)
    {
        _bladeMaxFanText = mode == BladeMaxFanMode.Enabled ? "开启" : "关闭";
        _bladeMaxFanEnabled = mode == BladeMaxFanMode.Enabled;
        _confirmedBladeMaxFanEnabled = _bladeMaxFanEnabled;
        _hasBladeMaxFan = true;
    }

    internal void SetLogo(BladeLogoMode mode)
    {
        _bladeLogoText = mode switch
        {
            BladeLogoMode.Off => "关闭",
            BladeLogoMode.Static => "常亮",
            BladeLogoMode.Breathing => "呼吸",
            _ => "--",
        };
        _bladeLogoIndex = Array.IndexOf(BladeLogoModes, mode);
        _confirmedBladeLogoIndex = _bladeLogoIndex;
    }
}
