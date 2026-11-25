using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using OpenSynapse.App.ViewModels;
using OpenSynapse.Core.Devices;

namespace OpenSynapse.App;

public sealed partial class MainWindow
{
    private readonly SemaphoreSlim _deviceNavigationGate = new(1, 1);
    private readonly Dictionary<string, double> _deviceScrollOffsets = new(StringComparer.OrdinalIgnoreCase);
    private bool _syncingDeviceNavigation;
    private bool _deviceNavigationRefreshPending;
    private int _deviceNavigationRevision;
    private string? _selectedDeviceKey;
    private string? _requestedDeviceKey;
    private object? _displayedDeviceSource;

    private static string DeviceNavigationKey(object source) =>
        $"{source.GetType().Name}:{MainViewModel.DeviceSourceIdentity(source, source.GetType().Name)}";

    private ConnectedDeviceRowViewModel? DefaultNavigationDevice() =>
        DevicesNavigationItem.MenuItems.OfType<NavigationViewItem>()
            .Select(item => (ConnectedDeviceRowViewModel)item.Tag)
            .FirstOrDefault(row => StringComparer.OrdinalIgnoreCase.Equals(
                DeviceNavigationKey(row.Source), _requestedDeviceKey ?? _selectedDeviceKey))
        ?? DevicesNavigationItem.MenuItems.OfType<NavigationViewItem>()
            .Select(item => (ConnectedDeviceRowViewModel)item.Tag).FirstOrDefault();

    private NavigationViewItem? FindDeviceNavigationItem(object source) =>
        DevicesNavigationItem.MenuItems.OfType<NavigationViewItem>()
            .FirstOrDefault(item => item.Tag is ConnectedDeviceRowViewModel row &&
                StringComparer.OrdinalIgnoreCase.Equals(DeviceNavigationKey(row.Source), DeviceNavigationKey(source)));

    private void RefreshDeviceNavigation()
    {
        if (_lifetime.IsCancellationRequested) return;
        // Discovery can complete during an awaited device read; finish that selection first.
        if (_deviceNavigationGate.CurrentCount == 0)
        {
            _deviceNavigationRefreshPending = true;
            return;
        }
        var existing = DevicesNavigationItem.MenuItems.OfType<NavigationViewItem>()
            .ToDictionary(item => DeviceNavigationKey(((ConnectedDeviceRowViewModel)item.Tag).Source),
                StringComparer.OrdinalIgnoreCase);
        var desired = new List<NavigationViewItem>();
        foreach (var row in _viewModel.ConnectedDevices)
        {
            if (_viewModel.IsLiteMode && row.Category == DeviceCategory.Laptop) continue;
            var key = DeviceNavigationKey(row.Source);
            if (!existing.TryGetValue(key, out var item))
            {
                item = new NavigationViewItem
                {
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Icon = new FontIcon
                    {
                        Glyph = row.IconGlyph,
                        FontFamily = (FontFamily)Application.Current.Resources["IconFontFamily"],
                        FontSize = 16,
                    },
                };
                AutomationProperties.SetAutomationId(item, $"DeviceNavigation_{key}");
            }
            item.Tag = row;
            var label = item.Content as TextBlock ?? new TextBlock
            {
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxLines = 1,
            };
            label.Text = row.Name.StartsWith("Razer ", StringComparison.Ordinal)
                ? row.Name[6..] : row.Name;
            item.Content = label;
            AutomationProperties.SetName(item, row.Name);
            ToolTipService.SetToolTip(item, row.Name);
            desired.Add(item);
        }

        _syncingDeviceNavigation = true;
        try
        {
            // Keep existing containers when discovery only refreshes the row snapshots.
            if (!DevicesNavigationItem.MenuItems.SequenceEqual(desired))
            {
                DevicesNavigationItem.MenuItems.Clear();
                foreach (var item in desired) DevicesNavigationItem.MenuItems.Add(item);
            }
            if (_currentPage == "devices")
            {
                RootNavigationView.SelectedItem = _displayedDeviceSource is { } source
                    ? FindDeviceNavigationItem(source) ?? DevicesNavigationItem
                    : DevicesNavigationItem;
            }
        }
        finally { _syncingDeviceNavigation = false; }

        if (_currentPage != "devices") return;
        if (DefaultNavigationDevice() is { } target)
        {
            if (!ReferenceEquals(_displayedDeviceSource, target.Source))
                _ = SelectDeviceNavigationAsync(target.Source);
        }
        else
        {
            ++_deviceNavigationRevision;
            _displayedDeviceSource = null;
            _selectedDeviceKey = null;
            _devicesPage?.SelectDevice("none");
        }
    }

    private async Task SelectDeviceNavigationAsync(object source)
    {
        var revision = ++_deviceNavigationRevision;
        _requestedDeviceKey = DeviceNavigationKey(source);
        try
        {
            await _deviceNavigationGate.WaitAsync(_lifetime.Token);
            try
            {
                if (revision != _deviceNavigationRevision || _currentPage != "devices") return;
                var page = DevicesPage;
                var key = DeviceNavigationKey(source);
                if (!ReferenceEquals(_displayedDeviceSource, source) || string.IsNullOrEmpty(page.CurrentDeviceRoute()))
                {
                    if (_displayedDeviceSource is not null && _selectedDeviceKey is { } previous)
                        _deviceScrollOffsets[previous] = page.ScrollOffset;
                    page.IsEnabled = false;
                    try
                    {
                        await page.ShowDeviceAsync(source);
                        if (!ReferenceEquals(_devicesPage, page)) return;
                        _displayedDeviceSource = source;
                        _selectedDeviceKey = key;
                        page.RestoreScrollOffset(_deviceScrollOffsets.GetValueOrDefault(key));
                    }
                    finally { page.IsEnabled = true; }
                }
                if (revision != _deviceNavigationRevision || _currentPage != "devices") return;
                _syncingDeviceNavigation = true;
                try { RootNavigationView.SelectedItem = FindDeviceNavigationItem(source) ?? DevicesNavigationItem; }
                finally { _syncingDeviceNavigation = false; }
                page.RefreshAdvancedFeaturesVisibility();
                UpdatePerformanceSamplingState();
            }
            finally
            {
                _deviceNavigationGate.Release();
                if (_deviceNavigationRefreshPending)
                {
                    _deviceNavigationRefreshPending = false;
                    _dispatcherQueue.TryEnqueue(RefreshDeviceNavigation);
                }
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception exception) when (RuntimeErrors.IsExpected(exception))
        {
            _viewModel.ReportApplicationError(exception.Message);
        }
    }
}
