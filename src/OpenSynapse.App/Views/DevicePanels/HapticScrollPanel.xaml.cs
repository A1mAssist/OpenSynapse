using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OpenSynapse.App.ViewModels;

namespace OpenSynapse.App.Views.DevicePanels;

public sealed partial class HapticScrollPanel : UserControl
{
    private CancellationTokenSource _lifetime = new();
    private readonly SettingApplyQueue _queue = new();
    private readonly SettingAutoApply _apply;
    private RazerHidDeviceViewModel? Model => DataContext as RazerHidDeviceViewModel;

    public HapticScrollPanel()
    {
        InitializeComponent();
        _apply = SettingAutoApply.Attach(HapticSettings, _queue, () => DataContext,
            () => Model?.PrepareHapticAutoApply(_lifetime.Token));
        Loaded += (_, _) => RenewLifetime();
        Unloaded += (_, _) => _lifetime.Cancel();
        DataContextChanged += (_, _) => { _lifetime.Cancel(); RenewLifetime(); };
    }
    private void RenewLifetime()
    {
        if (!_lifetime.IsCancellationRequested) return;
        _lifetime.Dispose();
        _lifetime = new();
    }
    private void CurveEditCommitted(object? sender, EventArgs e) => _apply.CommitEdit();
    private async void ReadClick(object sender, RoutedEventArgs e)
    { if (Model is { } model) await model.LoadHapticScrollAsync(_lifetime.Token); }
    private void DefaultClick(object sender, RoutedEventArgs e)
    { Model?.CreateDefaultHapticCurve(); _apply.CommitEdit(); }
    private async void RestoreClick(object sender, RoutedEventArgs e)
    { if (Model is { } model) await model.RestoreSavedHapticCurveAsync(_lifetime.Token); }
}
