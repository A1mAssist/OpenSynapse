using Microsoft.UI.Xaml.Controls;
using OpenSynapse.App.ViewModels;

namespace OpenSynapse.App.Views;

public sealed partial class DevicesPage : UserControl
{
    private void InitializeKeyboardAutoApply()
    {
        AttachKeyboard(KeyboardGameModeAutoApply, device => device.PrepareGameModeAutoApply(Lifetime));
        AttachKeyboard(KeyboardPollingAutoApply, device => device.PreparePollingAutoApply(Lifetime));
        AttachKeyboard(KeyboardBrightnessAutoApply, device => device.PrepareBrightnessAutoApply(Lifetime));
        AttachHidKeyboard(HidGameModeAutoApply, device => device.PrepareGameModeAutoApply(Lifetime));
        AttachHidKeyboard(HidKnobAutoApply, device => device.PrepareKnobAutoApply(Lifetime));
        AttachHidKeyboard(HidOledAutoApply, device => device.PrepareOledAutoApply(Lifetime));
        AttachHidKeyboard(HidProfileDebounceAutoApply, device => device.PrepareProfileDebounceAutoApply(Lifetime));
        AttachHidKeyboard(HidKeyswitchAutoApply, device => device.PrepareKeyswitchAutoApply(Lifetime));
        AttachHidKeyboard(HidAnalogGlobalAutoApply, device => device.PrepareAnalogKeyAutoApply(Lifetime));
        AttachBleKeyboard(BleSmartIlluminationAutoApply, device => device.PrepareSmartIlluminationAutoApply(Lifetime));
        AttachBleKeyboard(BleKeysAutoApply, device => device.PrepareKeysAutoApply(Lifetime));
    }

    private void AttachKeyboard(StackPanel group, Func<RazerKeyboardViewModel, Func<Task<string?>>?> prepare) =>
        SettingAutoApply.Attach(group, _mouseApplyQueue, () => (group.DataContext as RazerKeyboardViewModel)?.KeyboardAutoApplyContext,
            () => group.DataContext is RazerKeyboardViewModel device ? prepare(device) : null);

    private void AttachHidKeyboard(StackPanel group, Func<RazerHidDeviceViewModel, Func<Task<string?>>?> prepare) =>
        SettingAutoApply.Attach(group, _mouseApplyQueue, () => (group.DataContext as RazerHidDeviceViewModel)?.KeyboardAutoApplyContext,
            () => group.DataContext is RazerHidDeviceViewModel device ? prepare(device) : null);

    private void AttachBleKeyboard(StackPanel group, Func<RazerBleDeviceViewModel, Func<Task<string?>>?> prepare) =>
        SettingAutoApply.Attach(group, _mouseApplyQueue, () => (group.DataContext as RazerBleDeviceViewModel)?.KeyboardAutoApplyContext,
            () => group.DataContext is RazerBleDeviceViewModel device ? prepare(device) : null);
}
