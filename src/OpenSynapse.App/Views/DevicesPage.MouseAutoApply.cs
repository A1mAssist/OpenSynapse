using Microsoft.UI.Xaml.Controls;
using OpenSynapse.App.ViewModels;

namespace OpenSynapse.App.Views;

public sealed partial class DevicesPage : UserControl
{
    private readonly SettingApplyQueue _mouseApplyQueue = new();

    private void InitializeMouseAutoApply()
    {
        InitializeViperAutoApply();
        AttachHid(HidPollingAutoApply, device => device.PreparePollingAutoApply(Lifetime));
        AttachHid(HidDpiAutoApply, device => device.PrepareDpiAutoApply(Lifetime));
        AttachHid(HidDpiStagesAutoApply, device => device.PrepareDpiStagesAutoApply(Lifetime));
        AttachHid(HidIdleAutoApply, device => device.PrepareIdleAutoApply(Lifetime));
        AttachHid(HidLowBatteryAutoApply, device => device.PrepareLowBatteryAutoApply(Lifetime));
        AttachHid(HidAutoSleepAutoApply, device => device.PrepareAutoSleepAutoApply(Lifetime));
        AttachHid(HidChargeLimiterAutoApply, device => device.PrepareChargeLimiterAutoApply(Lifetime));
        AttachHid(HidTimeToDimAutoApply, device => device.PrepareTimeToDimAutoApply(Lifetime));
        AttachHid(HidIndicatorAutoApply, device => device.PrepareIndicatorAutoApply(Lifetime));
        AttachHid(HidIndicatorV2AutoApply, device => device.PrepareIndicatorV2AutoApply(Lifetime));
        AttachHid(HidBatteryChemistryAutoApply, device => device.PrepareBatteryChemistryAutoApply(Lifetime));

        AttachBle(BlePollingAutoApply, device => device.PreparePollingAutoApply(Lifetime));
        AttachBle(BleDpiAutoApply, device => device.PrepareDpiAutoApply(Lifetime));
        AttachBle(BleDpiStagesAutoApply, device => device.PrepareDpiStagesAutoApply(Lifetime));
        AttachBle(BlePowerAutoApply, device => device.PreparePowerAutoApply(Lifetime));
        AttachBle(BlePowerPlanAutoApply, device => device.PreparePowerPlanAutoApply(Lifetime));
        AttachBle(BleBatteryChemistryAutoApply, device => device.PrepareBatteryChemistryAutoApply(Lifetime));
    }

    private void AttachHid(StackPanel group, Func<RazerHidDeviceViewModel, Func<Task<string?>>?> prepare) =>
        SettingAutoApply.Attach(group, _mouseApplyQueue, () => group.DataContext as RazerHidDeviceViewModel,
            () => group.DataContext is RazerHidDeviceViewModel device ? prepare(device) : null);

    private void AttachBle(StackPanel group, Func<RazerBleDeviceViewModel, Func<Task<string?>>?> prepare) =>
        SettingAutoApply.Attach(group, _mouseApplyQueue, () => group.DataContext as RazerBleDeviceViewModel,
            () => group.DataContext is RazerBleDeviceViewModel device ? prepare(device) : null);
}
