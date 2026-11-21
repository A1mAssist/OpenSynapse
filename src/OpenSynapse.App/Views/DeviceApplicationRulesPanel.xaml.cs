using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OpenSynapse.App.ViewModels;
using Windows.Storage.Pickers;

namespace OpenSynapse.App.Views;

public sealed partial class DeviceApplicationRulesPanel : UserControl
{
    internal MainWindow? Host { get; set; }
    public DeviceApplicationRulesPanel() => InitializeComponent();
    private DeviceApplicationRulesWorkspace ViewModel => (DeviceApplicationRulesWorkspace)DataContext;
    public IReadOnlyList<string> ProfileNames => ViewModel.ProfileNames;
    private CancellationToken Lifetime => Host?.LifetimeToken ?? CancellationToken.None;
    internal void BindDeviceRules() => RulesList.ItemsSource = ViewModel.DeviceRules;

    private async void AddRuleClick(object sender, RoutedEventArgs e)
    {
        if (ApplicationPicker.SelectedItem is not string path || ProfilePicker.SelectedItem is not string profile) return;
        await ViewModel.AddAsync(path, profile, Lifetime);
    }

    private async void RemoveRuleClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string id }) await ViewModel.RemoveAsync(id, Lifetime);
    }

    private async void RuleChanged(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not DeviceApplicationRuleRowViewModel row) return;
        try
        {
            await ViewModel.SaveAsync(row, Lifetime);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            Host?.ViewModel.ReportApplicationError(AppStrings.FormatText(
                "LabeledError", AppStrings.Text("DeviceRulesHeader.Header"), exception.Message));
        }
    }

    private async void BrowseApplicationClick(object sender, RoutedEventArgs e)
    {
        if (Host is null || ProfilePicker.SelectedItem is not string profile) return;
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
        picker.FileTypeFilter.Add(".exe");
        Host.InitializePicker(picker);
        if (await picker.PickSingleFileAsync() is { Path.Length: > 0 } file)
            await ViewModel.AddAsync(file.Path, profile, Lifetime);
    }
}
