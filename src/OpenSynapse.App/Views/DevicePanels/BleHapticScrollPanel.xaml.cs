using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OpenSynapse.App.ViewModels;

namespace OpenSynapse.App.Views.DevicePanels;

public sealed partial class BleHapticScrollPanel : UserControl
{
    private readonly SettingApplyQueue _queue = new();
    private CancellationTokenSource _lifetime = new();
    public BleHapticScrollPanel()
    {
        InitializeComponent();
        SettingAutoApply.Attach(BleHapticSettings, _queue, () => DataContext,
            () => (DataContext as RazerBleDeviceViewModel)?.PrepareHapticAutoApply(_lifetime.Token));
        Unloaded += (_, _) => _lifetime.Cancel();
        Loaded += (_, _) => Renew();
        DataContextChanged += (_, _) => { _lifetime.Cancel(); Renew(); };
    }
    private void Renew()
    {
        if (!_lifetime.IsCancellationRequested) return;
        _lifetime.Dispose(); _lifetime = new();
    }
    private async void ReadClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is RazerBleDeviceViewModel model) await model.LoadHapticStageAsync(_lifetime.Token);
    }
}
