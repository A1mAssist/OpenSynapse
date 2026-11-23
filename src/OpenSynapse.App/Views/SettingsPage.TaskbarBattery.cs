using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OpenSynapse.App.ViewModels;
using OpenSynapse.Core.Sensors;

namespace OpenSynapse.App.Views;

public sealed partial class SettingsPage
{
    private bool _taskbarBatteryUiReady;
    private bool _taskbarBatterySourcesHooked;
    private readonly HashSet<INotifyPropertyChanged> _taskbarBatteryRows = [];

    internal void InitializeTaskbarBatteryUi()
    {
        if (!_taskbarBatterySourcesHooked)
        {
            _taskbarBatterySourcesHooked = true;
            ViewModel.Devices.CollectionChanged += OnTaskbarBatterySourcesChanged;
            ViewModel.RazerHid.Devices.CollectionChanged += OnTaskbarBatterySourcesChanged;
            ViewModel.Accessories.HeadsetAudioDevices.CollectionChanged += OnTaskbarBatterySourcesChanged;
            ViewModel.Accessories.KeyboardDevices.CollectionChanged += OnTaskbarBatterySourcesChanged;
            ViewModel.BlackSharkDevices.CollectionChanged += OnTaskbarBatterySourcesChanged;
            ViewModel.BleDevices.CollectionChanged += OnTaskbarBatterySourcesChanged;
            ViewModel.AwBleHeadsetDevices.CollectionChanged += OnTaskbarBatterySourcesChanged;
        }
        RefreshTaskbarBatteryChoices();
    }

    private void OnTaskbarBatterySourcesChanged(object? sender, NotifyCollectionChangedEventArgs args) =>
        RefreshTaskbarBatteryChoices();

    internal void ShutdownTaskbarBatteryUi()
    {
        if (_taskbarBatterySourcesHooked && DataContext is MainViewModel viewModel)
        {
            _taskbarBatterySourcesHooked = false;
            viewModel.Devices.CollectionChanged -= OnTaskbarBatterySourcesChanged;
            viewModel.RazerHid.Devices.CollectionChanged -= OnTaskbarBatterySourcesChanged;
            viewModel.Accessories.HeadsetAudioDevices.CollectionChanged -= OnTaskbarBatterySourcesChanged;
            viewModel.Accessories.KeyboardDevices.CollectionChanged -= OnTaskbarBatterySourcesChanged;
            viewModel.BlackSharkDevices.CollectionChanged -= OnTaskbarBatterySourcesChanged;
            viewModel.BleDevices.CollectionChanged -= OnTaskbarBatterySourcesChanged;
            viewModel.AwBleHeadsetDevices.CollectionChanged -= OnTaskbarBatterySourcesChanged;
        }
        ObserveTaskbarBatteryRows([]);
        TaskbarBatteryChoices.ItemsSource = null;
    }

    private void RefreshTaskbarBatteryChoices()
    {
        if (Host?.BehaviorSettings is null) return;
        var settings = Host.BehaviorSettings;
        var available = ViewModel.Tray.ConnectedBatteries
            .Where(item => item.Percent is not null)
            .GroupBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToDictionary(item => item.Key, StringComparer.OrdinalIgnoreCase);
        ObserveTaskbarBatteryRows(available.Values);
        var saved = settings.TaskbarBatteryDisplay;
        var savedKeys = saved.Select(item => item.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var enabled = TaskbarStatusToggle.IsOn;
        var full = saved.Count >= TaskbarStatusPolicy.MaximumBatteryCount;
        var choices = new List<TaskbarBatteryChoiceViewModel>(available.Count + saved.Count);

        foreach (var preference in saved)
        {
            if (available.TryGetValue(preference.Key, out var battery))
            {
                choices.Add(Choice(battery, selected: true, enabled));
            }
            else
            {
                choices.Add(new TaskbarBatteryChoiceViewModel(
                    preference.Key,
                    preference.Name,
                    string.Empty,
                    AppStrings.Text("TaskbarBatteryDisconnected"),
                    isSelected: true,
                    isEnabled: enabled));
            }
        }

        foreach (var battery in available.Values
            .Where(item => !savedKeys.Contains(item.Key))
            .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            choices.Add(Choice(battery, selected: false, enabled && !full));
        }

        _taskbarBatteryUiReady = false;
        TaskbarBatteryChoices.ItemsSource = choices;
        TaskbarBatterySelectionPanel.Visibility =
            available.Count >= 2 || saved.Count >= 2 ? Visibility.Visible : Visibility.Collapsed;
        _taskbarBatteryUiReady = true;
    }

    private void ObserveTaskbarBatteryRows(IEnumerable<PeripheralBattery> batteries)
    {
        var rows = batteries
            .Select(item => item.Source)
            .OfType<INotifyPropertyChanged>()
            .ToHashSet();
        foreach (var row in _taskbarBatteryRows.Except(rows).ToArray())
        {
            row.PropertyChanged -= OnTaskbarBatteryRowChanged;
            _taskbarBatteryRows.Remove(row);
        }

        foreach (var row in rows.Where(_taskbarBatteryRows.Add))
        {
            row.PropertyChanged += OnTaskbarBatteryRowChanged;
        }
    }

    private void OnTaskbarBatteryRowChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (!string.IsNullOrEmpty(args.PropertyName) &&
            args.PropertyName != nameof(IDeviceBatteryRow.BatteryPercent))
        {
            return;
        }

        if (DispatcherQueue.HasThreadAccess)
        {
            RefreshTaskbarBatteryChoices();
        }
        else
        {
            DispatcherQueue.TryEnqueue(RefreshTaskbarBatteryChoices);
        }
    }

    private static TaskbarBatteryChoiceViewModel Choice(
        PeripheralBattery battery, bool selected, bool enabled) =>
        new(
            battery.Key,
            battery.Name,
            battery.Glyph,
            battery.Percent is { } percent
                ? AppStrings.FormatText("TaskbarBatteryCurrentPercent", percent)
                : "—",
            selected,
            enabled);

    private void TaskbarBatteryChoiceToggled(object sender, RoutedEventArgs e)
    {
        if (!_taskbarBatteryUiReady ||
            sender is not ToggleSwitch toggle ||
            toggle.DataContext is not TaskbarBatteryChoiceViewModel choice ||
            toggle.IsOn == choice.IsSelected)
        {
            return;
        }

        var settings = Host.BehaviorSettings;
        var previous = CloneTaskbarBatteryDisplay(settings.TaskbarBatteryDisplay);
        try
        {
            settings.TaskbarBatteryDisplay = ApplyTaskbarBatteryChoice(
                settings.TaskbarBatteryDisplay, choice, toggle.IsOn);
            settings.Save();
            Host.RefreshTaskbarStatus();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
            InvalidOperationException or System.Security.SecurityException)
        {
            settings.TaskbarBatteryDisplay = previous;
            ViewModel.ReportApplicationError(
                AppStrings.FormatText("BehaviorSettingError", exception.Message));
        }
        RefreshTaskbarBatteryChoices();
    }

    private static List<TaskbarBatteryPreference> ApplyTaskbarBatteryChoice(
        IReadOnlyList<TaskbarBatteryPreference> current,
        TaskbarBatteryChoiceViewModel choice,
        bool selected)
    {
        var next = CloneTaskbarBatteryDisplay(current);
        next.RemoveAll(item => StringComparer.OrdinalIgnoreCase.Equals(item.Key, choice.Key));
        if (selected && next.Count < TaskbarStatusPolicy.MaximumBatteryCount)
        {
            next.Add(new TaskbarBatteryPreference { Key = choice.Key, Name = choice.Name });
        }
        return next;
    }

    private static List<TaskbarBatteryPreference> CloneTaskbarBatteryDisplay(
        IReadOnlyList<TaskbarBatteryPreference> source) =>
        source.Select(item => new TaskbarBatteryPreference { Key = item.Key, Name = item.Name })
            .ToList();
}
