using Microsoft.UI.Xaml;
using OpenSynapse.Windows.Devices;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHidDeviceViewModel
{
    public Visibility ScrollVisibility => VisibleWhen(HasAny(
        RazerHidBackendCapability.ScrollAccelerationRead,
        RazerHidBackendCapability.ScrollAccelerationWrite,
        RazerHidBackendCapability.SmartReelRead,
        RazerHidBackendCapability.SmartReelWrite,
        RazerHidBackendCapability.SmartReelThresholdRead,
        RazerHidBackendCapability.SmartReelThresholdWrite,
        RazerHidBackendCapability.ScrollModeRead,
        RazerHidBackendCapability.ScrollModeWrite,
        RazerHidBackendCapability.ScrollFreeNotchedModeRead,
        RazerHidBackendCapability.ScrollFreeNotchedModeWrite,
        RazerHidBackendCapability.ScrollAccelerationLevelRead,
        RazerHidBackendCapability.ScrollAccelerationLevelWrite,
        RazerHidBackendCapability.LkScrollWheelModeRead,
        RazerHidBackendCapability.LkScrollWheelModeWrite));
    public Visibility ScrollModeVisibility => Visibility.Collapsed;
    public Visibility ScrollModeWriteVisibility => VisibleWhen(Has(RazerHidBackendCapability.ScrollModeWrite));
    public Visibility ScrollAccelerationVisibility => VisibleWhen(HasAny(
        RazerHidBackendCapability.ScrollAccelerationRead, RazerHidBackendCapability.ScrollAccelerationWrite));
    public Visibility ScrollAccelerationWriteVisibility => VisibleWhen(Has(RazerHidBackendCapability.ScrollAccelerationWrite));
    public Visibility SmartReelVisibility => Visibility.Collapsed;
    public Visibility SmartReelWriteVisibility => VisibleWhen(Has(RazerHidBackendCapability.SmartReelWrite));
    public Visibility SmartReelThresholdVisibility => VisibleWhen(HasAny(
        RazerHidBackendCapability.SmartReelThresholdRead, RazerHidBackendCapability.SmartReelThresholdWrite));
    public Visibility SmartReelThresholdWriteVisibility => VisibleWhen(Has(RazerHidBackendCapability.SmartReelThresholdWrite));
    public Visibility ScrollFreeNotchedModeVisibility => Visibility.Collapsed;
    public Visibility ScrollFreeNotchedModeWriteVisibility =>
        VisibleWhen(Has(RazerHidBackendCapability.ScrollFreeNotchedModeWrite));
    public Visibility ScrollAccelerationLevelVisibility => VisibleWhen(HasAny(
        RazerHidBackendCapability.ScrollAccelerationLevelRead, RazerHidBackendCapability.ScrollAccelerationLevelWrite));
    public Visibility ScrollAccelerationLevelWriteVisibility =>
        VisibleWhen(Has(RazerHidBackendCapability.ScrollAccelerationLevelWrite));
    public IReadOnlyList<string> ScrollFreeNotchedModeTexts { get; } =
        [AppStrings.Text("RazerHidScrollNotchedMode"), AppStrings.Text("RazerHidScrollFreeSpinMode")];
    public bool IsScrollLoading { get => _isScrollLoading; private set => SetField(ref _isScrollLoading, value); }
    public byte ScrollMode { get => _scrollMode; set => SetField(ref _scrollMode, value); }
    public bool ScrollAccelerationEnabled { get => _scrollAccelerationEnabled; set => SetField(ref _scrollAccelerationEnabled, value); }
    public bool SmartReelEnabled { get => _smartReelEnabled; set => SetField(ref _smartReelEnabled, value); }
    public byte SmartReelThreshold { get => _smartReelThreshold; set => SetField(ref _smartReelThreshold, value); }
    public int ScrollFreeNotchedModeIndex
    {
        get => _scrollFreeNotchedModeIndex;
        set => SetField(ref _scrollFreeNotchedModeIndex, value);
    }
    public byte ScrollAccelerationLevel
    {
        get => _scrollAccelerationLevel;
        set => SetField(ref _scrollAccelerationLevel, value);
    }
    public bool IsScrollFreeNotchedBusy { get => _isScrollFreeNotchedBusy; private set => SetBusy(ref _isScrollFreeNotchedBusy, value, nameof(CanEditScrollFreeNotched), nameof(CanWriteScrollFreeNotched)); }
    public bool IsScrollAccelerationLevelBusy { get => _isScrollAccelerationLevelBusy; private set => SetBusy(ref _isScrollAccelerationLevelBusy, value, nameof(CanEditScrollAccelerationLevel), nameof(CanWriteScrollAccelerationLevel)); }
    public bool IsScrollModeBusy { get => _isScrollModeBusy; private set => SetBusy(ref _isScrollModeBusy, value, nameof(CanEditScrollMode), nameof(CanWriteScrollMode)); }
    public bool IsScrollAccelerationBusy { get => _isScrollAccelerationBusy; private set => SetBusy(ref _isScrollAccelerationBusy, value, nameof(CanEditScrollAcceleration), nameof(CanWriteScrollAcceleration)); }
    public bool IsSmartReelBusy { get => _isSmartReelBusy; private set => SetBusy(ref _isSmartReelBusy, value, nameof(CanEditSmartReel), nameof(CanWriteSmartReel)); }
    public bool IsSmartReelThresholdBusy { get => _isSmartReelThresholdBusy; private set => SetBusy(ref _isSmartReelThresholdBusy, value, nameof(CanEditSmartReelThreshold), nameof(CanWriteSmartReelThreshold)); }
    public bool CanWriteScrollFreeNotched => CanUse(RazerHidBackendCapability.ScrollFreeNotchedModeWrite) && !IsScrollFreeNotchedBusy;
    public bool CanWriteScrollAccelerationLevel => CanUse(RazerHidBackendCapability.ScrollAccelerationLevelWrite) && !IsScrollAccelerationLevelBusy;
    public bool CanWriteScrollMode => CanUse(RazerHidBackendCapability.ScrollModeWrite) && !IsScrollModeBusy;
    public bool CanWriteScrollAcceleration => CanUse(RazerHidBackendCapability.ScrollAccelerationWrite) && !IsScrollAccelerationBusy;
    public bool CanWriteSmartReel => CanUse(RazerHidBackendCapability.SmartReelWrite) && !IsSmartReelBusy;
    public bool CanWriteSmartReelThreshold => CanUse(RazerHidBackendCapability.SmartReelThresholdWrite) && !IsSmartReelThresholdBusy;
    public bool CanEditScrollFreeNotched => CanUse(RazerHidBackendCapability.ScrollFreeNotchedModeWrite);
    public bool CanEditScrollAccelerationLevel => CanUse(RazerHidBackendCapability.ScrollAccelerationLevelWrite);
    public bool CanEditScrollMode => CanUse(RazerHidBackendCapability.ScrollModeWrite);
    public bool CanEditScrollAcceleration => CanUse(RazerHidBackendCapability.ScrollAccelerationWrite);
    public bool CanEditSmartReel => CanUse(RazerHidBackendCapability.SmartReelWrite);
    public bool CanEditSmartReelThreshold => CanUse(RazerHidBackendCapability.SmartReelThresholdWrite);

    public Visibility KeyswitchVisibility => VisibleWhen(HasAny(
        RazerHidBackendCapability.KeyswitchOptimizationRead,
        RazerHidBackendCapability.KeyswitchOptimizationWrite));
    public Visibility KeyswitchWriteVisibility => VisibleWhen(Has(RazerHidBackendCapability.KeyswitchOptimizationWrite));
    public IReadOnlyList<RazerHidKeyswitchOptimization> KeyswitchOptions { get; } =
        Enum.GetValues<RazerHidKeyswitchOptimization>();
    public IReadOnlyList<string> KeyswitchOptionTexts =>
        [AppStrings.Text("RazerHidKeyswitchTyping"), AppStrings.Text("RazerHidKeyswitchGaming")];
    public int KeyswitchOptimizationIndex
    {
        get => (int)KeyswitchOptimization;
        set
        {
            if (Enum.IsDefined((RazerHidKeyswitchOptimization)value))
                KeyswitchOptimization = (RazerHidKeyswitchOptimization)value;
        }
    }
    public RazerHidKeyswitchOptimization KeyswitchOptimization
    {
        get => _keyswitchOptimization;
        set
        {
            if (SetField(ref _keyswitchOptimization, value)) OnPropertyChanged(nameof(KeyswitchOptimizationIndex));
        }
    }
    public bool IsKeyswitchLoading { get => _isKeyswitchLoading; private set => SetField(ref _isKeyswitchLoading, value); }
    public bool IsKeyswitchBusy { get => _isKeyswitchBusy; private set => SetBusy(ref _isKeyswitchBusy, value, nameof(CanEditKeyswitch), nameof(CanWriteKeyswitch)); }
    public bool CanWriteKeyswitch => CanUse(RazerHidBackendCapability.KeyswitchOptimizationWrite) && !IsKeyswitchBusy;
    public bool CanEditKeyswitch => CanUse(RazerHidBackendCapability.KeyswitchOptimizationWrite);

    public Visibility HyperPollingVisibility => VisibleWhen(HasAny(
        RazerHidBackendCapability.HyperPollingIndicatorWrite,
        RazerHidBackendCapability.HyperPollingPairWrite,
        RazerHidBackendCapability.HyperPollingUnpairWrite));
    public Visibility HyperPollingIndicatorVisibility => VisibleWhen(Has(RazerHidBackendCapability.HyperPollingIndicatorWrite));
    public Visibility HyperPollingPairVisibility => VisibleWhen(Has(RazerHidBackendCapability.HyperPollingPairWrite));
    public Visibility HyperPollingUnpairVisibility => VisibleWhen(Has(RazerHidBackendCapability.HyperPollingUnpairWrite));
    public byte HyperPollingIndicatorMode { get => _hyperPollingIndicatorMode; set => SetField(ref _hyperPollingIndicatorMode, value); }
    public bool IsHyperPollingBusy
    {
        get => _isHyperPollingBusy;
        private set
        {
            if (!SetField(ref _isHyperPollingBusy, value)) return;
            OnPropertyChanged(nameof(CanUseHyperPolling));
            OnPropertyChanged(nameof(CanSetHyperPollingIndicator));
            OnPropertyChanged(nameof(CanPairHyperPolling));
            OnPropertyChanged(nameof(CanUnpairHyperPolling));
        }
    }
    public bool CanUseHyperPolling => CanWrite;
    public bool CanSetHyperPollingIndicator => CanUseHyperPolling && Has(RazerHidBackendCapability.HyperPollingIndicatorWrite);
    public bool CanPairHyperPolling => CanUseHyperPolling && Has(RazerHidBackendCapability.HyperPollingPairWrite);
    public bool CanUnpairHyperPolling => CanUseHyperPolling && Has(RazerHidBackendCapability.HyperPollingUnpairWrite);
}
