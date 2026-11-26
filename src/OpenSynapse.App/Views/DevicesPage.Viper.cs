using Microsoft.UI.Xaml.Controls;
using OpenSynapse.App.ViewModels;

namespace OpenSynapse.App.Views;

public sealed partial class DevicesPage : UserControl
{
    private void InitializeViperAutoApply()
    {
        Attach(ViperPollingAutoApply, device => device.PreparePollingAutoApply(Lifetime));
        Attach(ViperIdleAutoApply, device => device.PrepareIdleAutoApply(Lifetime));
        Attach(ViperDpiAutoApply, device => device.PrepareDpiAutoApply(Lifetime));
        Attach(ViperLowBatteryAutoApply, device => device.PrepareLowBatteryAutoApply(Lifetime));
        Attach(ViperBatteryChemistryAutoApply, device => device.PrepareBatteryChemistryAutoApply(Lifetime));
        Attach(ViperDpiStagesAutoApply, device => device.PrepareDpiStagesAutoApply(Lifetime));

        void Attach(StackPanel group, Func<ViperWorkspace, Func<Task<string?>>?> prepare) =>
            SettingAutoApply.Attach(group, _mouseApplyQueue,
                () => (DataContext as MainViewModel)?.Viper.MouseAutoApplyContext,
                () => DataContext is MainViewModel model ? prepare(model.Viper) : null);
    }
}
