using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using OpenSynapse.App.Runtime;
using OpenSynapse.Core.Devices;
using System.Net.Sockets;

namespace OpenSynapse.App;

public sealed partial class MainWindow
{
    private readonly AppBehaviorSettings _behaviorSettings;
    private readonly Func<bool, Task>? _setChromaRestEnabled;
    private readonly Func<ChromaRestSnapshot>? _getChromaRestSnapshot;
    private readonly Func<string, bool, Task>? _setChromaSourceAllowed;
    private ChromaSourceSnapshot[]? _lastChromaSources;
    private bool _chromaSourceChangePending;
    private readonly DispatcherQueueTimer _chromaRestStatusTimer;
    private bool _languageSelectionReady;
    private bool _behaviorUiReady;
    private int[] _refreshRateCycleOptions = [];
    private long _lastChromaFrames;
    private DateTimeOffset _lastChromaSample = DateTimeOffset.UtcNow;

    private void LanguageSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_languageSelectionReady ||
            sender is not ComboBox { SelectedItem: ComboBoxItem { Tag: string language } } ||
            language == AppLanguageSettings.Current)
        {
            return;
        }

        try
        {
            var comboSelections = CaptureComboBoxSelections();
            _languageSelectionReady = false;
            AppLanguageSettings.Save(language);
            AppStrings.Reset();
            _viewModel.RefreshLocalization();
            Localized.RefreshTree(RootLayout);
        _lastChromaSources = null;
            RefreshChromaRestStatus();
            _aboutWindow?.RefreshLocalization();
            ((App)Application.Current).RefreshTrayLocalization();
            RefreshIntroductionLocalization();
            RefreshUpdateUiText();
            SelectLanguage(language);
            RestoreComboBoxSelections(comboSelections);
            _languageSelectionReady = true;
        }
        catch (Exception exception)
        {
            _languageSelectionReady = false;
            SelectLanguage(AppLanguageSettings.Current);
            _languageSelectionReady = true;
            _viewModel.ReportApplicationError(AppStrings.FormatText("LanguageSettingError",
                exception.Message));
        }
    }

    private List<(ComboBox ComboBox, int SelectedIndex)> CaptureComboBoxSelections()
    {
        var selections = new List<(ComboBox, int)>();
        CaptureComboBoxSelections(RootLayout, selections);
        return selections;
    }

    private void CaptureComboBoxSelections(
        DependencyObject element,
        ICollection<(ComboBox ComboBox, int SelectedIndex)> selections)
    {
        if (element is ComboBox comboBox && comboBox != AppLanguageComboBox && comboBox.SelectedIndex >= 0)
        {
            selections.Add((comboBox, comboBox.SelectedIndex));
        }

        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(element); index++)
        {
            CaptureComboBoxSelections(VisualTreeHelper.GetChild(element, index), selections);
        }
    }

    private static void RestoreComboBoxSelections(
        IEnumerable<(ComboBox ComboBox, int SelectedIndex)> selections)
    {
        foreach (var (comboBox, selectedIndex) in selections)
        {
            if (selectedIndex < comboBox.Items.Count)
            {
                comboBox.SelectedIndex = selectedIndex;
            }
        }
    }

    private void InitializeBehaviorSettingsUi()
    {
        ModeNotificationToggle.IsOn = _behaviorSettings.ModeChangeNotificationsEnabled;
        LowBatteryNotificationToggle.IsOn = _behaviorSettings.LowBatteryNotificationsEnabled;
        ChromaRestToggle.IsOn = _behaviorSettings.ExperimentalChromaRestEnabled;
        _viewModel.SetChromaIntegrationEnabled(_behaviorSettings.ExperimentalChromaRestEnabled);
        ChromaRestoreToggle.IsOn = _behaviorSettings.RestoreLightingAfterChromaSession;
        RefreshChromaRestStatus();
        foreach (var checkBox in PerformanceCycleModesPanel.Children.OfType<CheckBox>())
        {
            checkBox.IsChecked = checkBox.Tag is string tag &&
                Enum.TryParse<BladePerformanceMode>(tag, out var mode) &&
                _viewModel.BladePerformanceCycleModes.Contains(mode);
        }
        RebuildRefreshRateCycleOptions();
        _behaviorUiReady = true;
    }

    private void RefreshChromaRestStatus()
    {
        RefreshChromaSources();
        if (!_behaviorSettings.ExperimentalChromaRestEnabled)
        {
            ChromaRestStatusText.Text = AppStrings.Text("ChromaRestStatusDisabled");
            return;
        }

        var snapshot = _getChromaRestSnapshot?.Invoke() ?? default;
        if (!snapshot.IsRunning)
        {
            ChromaRestStatusText.Text = AppStrings.Text("ChromaRestStatusUnavailable");
            return;
        }

        var endpoint = "127.0.0.1:54235";
        var now = DateTimeOffset.UtcNow;
        var elapsed = Math.Max((now - _lastChromaSample).TotalSeconds, 0.001);
        var framesPerSecond = Math.Max(0, snapshot.FramesAccepted - _lastChromaFrames) / elapsed;
        _lastChromaFrames = snapshot.FramesAccepted;
        _lastChromaSample = now;
        ChromaRestStatusText.Text = snapshot.ActiveSessionTitle is { Length: > 0 } title
            ? AppStrings.FormatText("ChromaRestStatusSession", endpoint, title, framesPerSecond, snapshot.FramesSkipped)
            : AppStrings.FormatText("ChromaRestStatusReady", endpoint);
    }

    private void RefreshChromaSources()
    {
        ChromaSourceSection.Visibility = _behaviorSettings.ExperimentalChromaRestEnabled
            ? Visibility.Visible : Visibility.Collapsed;
        if (_chromaSourceChangePending) return;
        var sources = (_getChromaRestSnapshot?.Invoke().Sources ?? [])
            .Concat(_behaviorSettings.ChromaBlockedSources.Select(title => new ChromaSourceSnapshot(title, false, false, false)))
            .DistinctBy(source => source.Title, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(source => source.Active)
            .ThenBy(source => source.Title, StringComparer.OrdinalIgnoreCase).ToArray();
        if (_lastChromaSources?.SequenceEqual(sources) == true) return;
        _lastChromaSources = sources;
        ChromaSourcesPanel.Children.Clear();
        ChromaSourcesEmptyText.Visibility = sources.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var source in sources)
        {
            var row = new Grid { ColumnSpacing = 16 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var text = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = source.Title, TextWrapping = TextWrapping.Wrap });
            text.Children.Add(new TextBlock
            {
                Text = AppStrings.Text(!source.Allowed ? "ChromaSourceBlocked" : source.Active
                    ? "ChromaSourceActive" : source.Connected ? "ChromaSourceConnected" : "ChromaSourceOffline"),
                FontSize = 12,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["MutedBrush"],
            });
            row.Children.Add(text);
            var toggle = new ToggleSwitch
            {
                IsOn = source.Allowed, OnContent = string.Empty, OffContent = string.Empty,
                MinWidth = 44, VerticalAlignment = VerticalAlignment.Center,
                IsEnabled = _setChromaSourceAllowed is not null,
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(toggle,
                AppStrings.FormatText("ChromaSourceAllowName", source.Title));
            toggle.Toggled += async (_, _) =>
            {
                _chromaSourceChangePending = true;
                ChromaRestToggle.IsEnabled = false;
                foreach (var control in ChromaSourcesPanel.Children.OfType<Grid>()
                    .SelectMany(grid => grid.Children.OfType<ToggleSwitch>())) control.IsEnabled = false;
                try
                {
                    if (_setChromaSourceAllowed is not null)
                        await _setChromaSourceAllowed(source.Title, toggle.IsOn);
                }
                catch (Exception exception)
                {
                    _viewModel.ReportApplicationError(AppStrings.FormatText("BehaviorSettingError", exception.Message));
                }
                finally
                {
                    _chromaSourceChangePending = false;
                    ChromaRestToggle.IsEnabled = true;
                    _lastChromaSources = null;
                    RefreshChromaRestStatus();
                }
            };
            Grid.SetColumn(toggle, 1);
            row.Children.Add(toggle);
            ChromaSourcesPanel.Children.Add(row);
        }
    }

    private void LowBatteryNotificationToggled(object sender, RoutedEventArgs e)
    {
        if (!_behaviorUiReady || sender is not ToggleSwitch toggle) return;
        var previous = _behaviorSettings.LowBatteryNotificationsEnabled;
        try
        {
            _behaviorSettings.LowBatteryNotificationsEnabled = toggle.IsOn;
            _behaviorSettings.Save();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            InvalidOperationException or System.Security.SecurityException)
        {
            _behaviorSettings.LowBatteryNotificationsEnabled = previous;
            _behaviorUiReady = false;
            toggle.IsOn = previous;
            _behaviorUiReady = true;
            _viewModel.ReportApplicationError(AppStrings.FormatText("BehaviorSettingError", exception.Message));
        }
    }

    private void OnChromaRestStatusTick(DispatcherQueueTimer sender, object args) =>
        RefreshChromaRestStatus();

    private void UpdateChromaRestStatusTimer()
    {
        if (AppWindow.IsVisible && SettingsPage.Visibility == Visibility.Visible)
        {
            _chromaRestStatusTimer.Start();
        }
        else
        {
            _chromaRestStatusTimer.Stop();
        }
    }

    private async void ChromaRestToggled(object sender, RoutedEventArgs e)
    {
        if (!_behaviorUiReady || sender is not ToggleSwitch toggle)
        {
            return;
        }

        var previous = _behaviorSettings.ExperimentalChromaRestEnabled;
        try
        {
            _behaviorSettings.ExperimentalChromaRestEnabled = toggle.IsOn;
            _behaviorSettings.Save();
            _viewModel.SetChromaIntegrationEnabled(toggle.IsOn);
            if (_setChromaRestEnabled is not null)
            {
                await _setChromaRestEnabled(toggle.IsOn);
            }
            RefreshChromaRestStatus();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
            InvalidOperationException or SocketException or System.Security.SecurityException)
        {
            _behaviorSettings.ExperimentalChromaRestEnabled = previous;
            _viewModel.SetChromaIntegrationEnabled(previous);
            _behaviorUiReady = false;
            toggle.IsOn = previous;
            _behaviorUiReady = true;
            _viewModel.ReportApplicationError(AppStrings.FormatText("BehaviorSettingError", exception.Message));
        }
    }

    private void ChromaRestoreToggled(object sender, RoutedEventArgs e)
    {
        if (!_behaviorUiReady || sender is not ToggleSwitch toggle)
        {
            return;
        }

        var previous = _behaviorSettings.RestoreLightingAfterChromaSession;
        try
        {
            _behaviorSettings.RestoreLightingAfterChromaSession = toggle.IsOn;
            _behaviorSettings.Save();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
            InvalidOperationException or System.Security.SecurityException)
        {
            _behaviorSettings.RestoreLightingAfterChromaSession = previous;
            _behaviorUiReady = false;
            toggle.IsOn = previous;
            _behaviorUiReady = true;
            _viewModel.ReportApplicationError(AppStrings.FormatText("BehaviorSettingError", exception.Message));
        }
    }

    private void RebuildRefreshRateCycleOptions(bool force = false)
    {
        var rates = _viewModel.InternalDisplayRefreshRates.Distinct().Order().ToArray();
        if (!force && _refreshRateCycleOptions.SequenceEqual(rates))
        {
            return;
        }
        _refreshRateCycleOptions = rates;
        var selected = _viewModel.InternalDisplayRefreshRateCycleHertz is { Count: > 0 } configured
            ? rates.Where(configured.Contains).ToHashSet()
            : rates.ToHashSet();
        if (selected.Count == 0)
        {
            selected = rates.ToHashSet();
        }

        RefreshRateCycleModesPanel.Children.Clear();
        RefreshRateCycleModesPanel.ColumnDefinitions.Clear();
        for (var index = 0; index < rates.Length; index++)
        {
            RefreshRateCycleModesPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var checkBox = new CheckBox
            {
                Tag = rates[index],
                Content = $"{rates[index]} Hz",
                IsChecked = selected.Contains(rates[index]),
            };
            checkBox.Checked += RefreshRateCycleModeChanged;
            checkBox.Unchecked += RefreshRateCycleModeChanged;
            Grid.SetColumn(checkBox, index);
            RefreshRateCycleModesPanel.Children.Add(checkBox);
        }

        RefreshRateCycleSection.Visibility = rates.Length > 1 ? Visibility.Visible : Visibility.Collapsed;
        if (selected.Count > 0)
        {
            _viewModel.SetInternalDisplayRefreshRateCycle(selected);
        }
    }

    private void ModeNotificationToggled(object sender, RoutedEventArgs e)
    {
        if (!_behaviorUiReady || sender is not ToggleSwitch toggle)
        {
            return;
        }

        var previous = _behaviorSettings.ModeChangeNotificationsEnabled;
        try
        {
            _behaviorSettings.ModeChangeNotificationsEnabled = toggle.IsOn;
            _behaviorSettings.Save();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
            InvalidOperationException or System.Security.SecurityException)
        {
            _behaviorSettings.ModeChangeNotificationsEnabled = previous;
            _behaviorUiReady = false;
            toggle.IsOn = previous;
            _behaviorUiReady = true;
            _viewModel.ReportApplicationError(AppStrings.FormatText("BehaviorSettingError",
                exception.Message));
        }
    }

    private async void PerformanceCycleModeChanged(object sender, RoutedEventArgs e)
    {
        if (!_behaviorUiReady)
        {
            return;
        }

        var selected = PerformanceCycleModesPanel.Children
            .OfType<CheckBox>()
            .Where(checkBox => checkBox.IsChecked == true)
            .Select(checkBox => checkBox.Tag as string)
            .Select(tag => Enum.TryParse<BladePerformanceMode>(tag, out var mode) ? mode : (BladePerformanceMode?)null)
            .OfType<BladePerformanceMode>()
            .ToHashSet();
        if (selected.Count == 0 && sender is CheckBox lastCheckBox)
        {
            _behaviorUiReady = false;
            lastCheckBox.IsChecked = true;
            _behaviorUiReady = true;
            return;
        }

        if (!await _viewModel.SavePerformanceCycleModesAsync(selected, _lifetime.Token))
        {
            _behaviorUiReady = false;
            InitializeBehaviorSettingsUi();
        }
    }

    private async void RefreshRateCycleModeChanged(object sender, RoutedEventArgs e)
    {
        if (!_behaviorUiReady)
        {
            return;
        }

        var selected = RefreshRateCycleModesPanel.Children
            .OfType<CheckBox>()
            .Where(checkBox => checkBox.IsChecked == true && checkBox.Tag is int)
            .Select(checkBox => (int)checkBox.Tag)
            .ToHashSet();
        if (selected.Count == 0 && sender is CheckBox lastCheckBox)
        {
            _behaviorUiReady = false;
            lastCheckBox.IsChecked = true;
            _behaviorUiReady = true;
            return;
        }

        if (!await _viewModel.SaveRefreshRateCycleAsync(selected, _lifetime.Token))
        {
            _behaviorUiReady = false;
            RebuildRefreshRateCycleOptions(force: true);
            _behaviorUiReady = true;
        }
    }

    private void SelectLanguage(string language)
    {
        AppLanguageComboBox.SelectedItem = AppLanguageComboBox.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(item => StringComparer.Ordinal.Equals(item.Tag as string, language)) ??
            AppLanguageComboBox.Items[0];
        RefreshLightingPresetOptions();
    }

    private async void StartupToggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch toggle && toggle.IsOn != _viewModel.IsStartupEnabled)
        {
            await _viewModel.SetStartupEnabledAsync(toggle.IsOn, _lifetime.Token);
        }
    }

    private async void SilentStartupToggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch toggle && toggle.IsOn != _viewModel.IsSilentStartupEnabled)
        {
            await _viewModel.SetSilentStartupEnabledAsync(toggle.IsOn, _lifetime.Token);
        }
    }
}
