using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using OpenSynapse.App.ViewModels;
using OpenSynapse.Core.Storage;
using OpenSynapse.Windows.Lighting;
using System.Net.Sockets;
using System.Reflection;
using Velopack;
using Velopack.Sources;

namespace OpenSynapse.App.Views;

public sealed partial class SettingsPage : UserControl
{
    private const string UpdateRepositoryUrl = "https://github.com/A1mAssist/OpenSynapse";

    private readonly UpdateManager _updateManager = new(
        new GithubSource(UpdateRepositoryUrl, null, prerelease: false));
    private readonly DispatcherQueueTimer _chromaRestStatusTimer;
    private ChromaSourceSnapshot[]? _lastChromaSources;
    private bool _chromaSourceChangePending;
    private bool _languageSelectionReady;
    private bool _behaviorUiReady;
    private long _lastChromaFrames;
    private DateTimeOffset _lastChromaSample = DateTimeOffset.UtcNow;
    private bool _updateUiReady;
    private bool _updateBusy;
    private UpdateInfo? _availableUpdate;
    private VelopackAsset? _downloadedUpdate;

    internal MainWindow Host { get; set; } = null!;

    internal bool AutomaticUpdatesEnabled => AutomaticUpdatesToggle.IsOn;

    private MainViewModel ViewModel => (MainViewModel)DataContext;
    private CancellationToken Lifetime => Host.LifetimeToken;

    public SettingsPage()
    {
        InitializeComponent();
        _chromaRestStatusTimer = DispatcherQueue.CreateTimer();
        _chromaRestStatusTimer.Interval = TimeSpan.FromSeconds(1);
        _chromaRestStatusTimer.Tick += OnChromaRestStatusTick;
    }

    internal void Initialize()
    {
        SelectLanguage(AppLanguageSettings.Current);
        _languageSelectionReady = true;
    }

    internal void InitializeBehaviorSettingsUi()
    {
        var settings = Host.BehaviorSettings;
        _behaviorUiReady = false;
        ModeNotificationToggle.IsOn = settings.ModeChangeNotificationsEnabled;
        LowBatteryNotificationToggle.IsOn = settings.LowBatteryNotificationsEnabled;
        TrayBatteryToggle.IsOn = settings.TrayBatteryLevelsEnabled;
        ChromaRestToggle.IsOn = settings.ExperimentalChromaRestEnabled;
        ViewModel.Lighting.SetChromaIntegrationEnabled(settings.ExperimentalChromaRestEnabled);
        ChromaRestoreToggle.IsOn = settings.RestoreLightingAfterChromaSession;
        RefreshChromaRestStatus();
        _behaviorUiReady = true;
    }

    internal void InitializeUpdateUi()
    {
        AutomaticUpdatesToggle.IsOn = AppUpdateSettings.AutomaticUpdatesEnabled;
        _downloadedUpdate = _updateManager.UpdatePendingRestart;
        _updateUiReady = true;
        RefreshUpdateUiText();
    }

    internal void RefreshUpdateUiText()
    {
        if (!_updateManager.IsInstalled)
        {
            UpdateStatusText.Text = AppStrings.Text("UpdateInstallerRequired");
            CheckUpdateButton.IsEnabled = false;
            AutomaticUpdatesToggle.IsEnabled = false;
            UpdateActionButton.Visibility = Visibility.Collapsed;
            return;
        }

        CheckUpdateButton.IsEnabled = !_updateBusy;
        if (_downloadedUpdate is not null)
        {
            UpdateStatusText.Text = AppStrings.FormatText("UpdateReady",
                _downloadedUpdate.Version);
            UpdateActionButton.Content = AppStrings.Text("Text_8EA990FD");
            UpdateActionButton.Visibility = Visibility.Visible;
            UpdateActionButton.IsEnabled = !_updateBusy;
            return;
        }

        if (_availableUpdate is not null)
        {
            UpdateStatusText.Text = AppStrings.FormatText("UpdateAvailable",
                _availableUpdate.TargetFullRelease.Version);
            UpdateActionButton.Content = AppStrings.Text("Text_B7E1FDB9");
            UpdateActionButton.Visibility = Visibility.Visible;
            UpdateActionButton.IsEnabled = !_updateBusy;
            return;
        }

        var currentVersion = _updateManager.CurrentVersion?.ToString() ??
            Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "--";
        UpdateStatusText.Text = AppStrings.FormatText("CurrentVersion",
            currentVersion);
        UpdateActionButton.Visibility = Visibility.Collapsed;
    }

    internal void UpdateChromaRestStatusTimer(bool appVisible)
    {
        if (appVisible && Visibility == Visibility.Visible)
        {
            _chromaRestStatusTimer.Start();
        }
        else
        {
            _chromaRestStatusTimer.Stop();
        }
    }

    internal void Shutdown()
    {
        _chromaRestStatusTimer.Stop();
        _chromaRestStatusTimer.Tick -= OnChromaRestStatusTick;
    }

    internal async Task CheckForUpdatesAsync(bool downloadAutomatically)
    {
        if (_updateBusy || !_updateManager.IsInstalled)
        {
            return;
        }

        SetUpdateBusy(true, AppStrings.Text("Text_68AA1281"));
        try
        {
            _availableUpdate = await _updateManager.CheckForUpdatesAsync();
            AppUpdateSettings.MarkCheckCompleted();
            if (_availableUpdate is null)
            {
                UpdateStatusText.Text = AppStrings.Text("Text_98A8DE82");
                UpdateActionButton.Visibility = Visibility.Collapsed;
            }
            else if (downloadAutomatically)
            {
                UpdateStatusText.Text = AppStrings.Text("Text_07BF2232");
                await DownloadUpdateAsync();
            }

            SetUpdateBusy(false);
            if (_availableUpdate is not null || _downloadedUpdate is not null)
            {
                RefreshUpdateUiText();
            }
        }
        catch (OperationCanceledException) when (Lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            UpdateStatusText.Text = AppStrings.FormatText("UpdateCheckFailed",
                exception.Message);
        }
        finally
        {
            SetUpdateBusy(false);
        }
    }

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
            ViewModel.RefreshLocalization();
            Host.RefreshLocalizationAfterLanguageChange();
            _lastChromaSources = null;
            RefreshChromaRestStatus();
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
            ViewModel.ReportApplicationError(AppStrings.FormatText("LanguageSettingError",
                exception.Message));
        }
    }

    private void ShowIntroductionClick(object sender, RoutedEventArgs e) => Host.ShowIntroduction();

    private void OpenAboutClick(object sender, RoutedEventArgs e) => Host.ShowAboutWindow();

    private List<(ComboBox ComboBox, int SelectedIndex)> CaptureComboBoxSelections()
    {
        var selections = new List<(ComboBox, int)>();
        CaptureComboBoxSelections(Host.RootLayoutElement, selections);
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

    private void SelectLanguage(string language)
    {
        AppLanguageComboBox.SelectedItem = AppLanguageComboBox.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(item => StringComparer.Ordinal.Equals(item.Tag as string, language)) ??
            AppLanguageComboBox.Items[0];
        Host.RefreshLightingPresetOptions();
    }

    private void RefreshChromaRestStatus()
    {
        RefreshChromaSources();
        if (!Host.BehaviorSettings.ExperimentalChromaRestEnabled)
        {
            ChromaRestStatusText.Text = AppStrings.Text("ChromaRestStatusDisabled");
            return;
        }

        var snapshot = Host.GetChromaRestSnapshot?.Invoke() ?? default;
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
        ChromaSourceSection.Visibility = Host.BehaviorSettings.ExperimentalChromaRestEnabled
            ? Visibility.Visible : Visibility.Collapsed;
        if (_chromaSourceChangePending) return;
        var sources = (Host.GetChromaRestSnapshot?.Invoke().Sources ?? [])
            .Concat(Host.BehaviorSettings.ChromaBlockedSources.Select(title => new ChromaSourceSnapshot(title, false, false, false)))
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
                Foreground = (Brush)Application.Current.Resources["MutedBrush"],
            });
            row.Children.Add(text);
            var toggle = new ToggleSwitch
            {
                IsOn = source.Allowed, OnContent = string.Empty, OffContent = string.Empty,
                MinWidth = 44, VerticalAlignment = VerticalAlignment.Center,
                IsEnabled = Host.SetChromaSourceAllowed is not null,
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
                    if (Host.SetChromaSourceAllowed is not null)
                        await Host.SetChromaSourceAllowed(source.Title, toggle.IsOn);
                }
                catch (Exception exception)
                {
                    ViewModel.ReportApplicationError(AppStrings.FormatText("BehaviorSettingError", exception.Message));
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
        var settings = Host.BehaviorSettings;
        var previous = settings.LowBatteryNotificationsEnabled;
        try
        {
            settings.LowBatteryNotificationsEnabled = toggle.IsOn;
            settings.Save();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            InvalidOperationException or System.Security.SecurityException)
        {
            settings.LowBatteryNotificationsEnabled = previous;
            _behaviorUiReady = false;
            toggle.IsOn = previous;
            _behaviorUiReady = true;
            ViewModel.ReportApplicationError(AppStrings.FormatText("BehaviorSettingError", exception.Message));
        }
    }

    private void TrayBatteryToggled(object sender, RoutedEventArgs e)
    {
        if (!_behaviorUiReady || sender is not ToggleSwitch toggle) return;
        var settings = Host.BehaviorSettings;
        var previous = settings.TrayBatteryLevelsEnabled;
        try
        {
            settings.TrayBatteryLevelsEnabled = toggle.IsOn;
            settings.Save();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            InvalidOperationException or System.Security.SecurityException)
        {
            settings.TrayBatteryLevelsEnabled = previous;
            _behaviorUiReady = false;
            toggle.IsOn = previous;
            _behaviorUiReady = true;
            ViewModel.ReportApplicationError(AppStrings.FormatText("BehaviorSettingError", exception.Message));
        }
    }

    private void OnChromaRestStatusTick(DispatcherQueueTimer sender, object args) =>
        RefreshChromaRestStatus();

    private async void ChromaRestToggled(object sender, RoutedEventArgs e)
    {
        if (!_behaviorUiReady || sender is not ToggleSwitch toggle)
        {
            return;
        }

        var settings = Host.BehaviorSettings;
        var previous = settings.ExperimentalChromaRestEnabled;
        try
        {
            settings.ExperimentalChromaRestEnabled = toggle.IsOn;
            settings.Save();
            ViewModel.Lighting.SetChromaIntegrationEnabled(toggle.IsOn);
            if (Host.SetChromaRestEnabled is not null)
            {
                await Host.SetChromaRestEnabled(toggle.IsOn);
            }
            RefreshChromaRestStatus();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
            InvalidOperationException or SocketException or System.Security.SecurityException)
        {
            settings.ExperimentalChromaRestEnabled = previous;
            ViewModel.Lighting.SetChromaIntegrationEnabled(previous);
            _behaviorUiReady = false;
            toggle.IsOn = previous;
            _behaviorUiReady = true;
            ViewModel.ReportApplicationError(AppStrings.FormatText("BehaviorSettingError", exception.Message));
        }
    }

    private void ChromaRestoreToggled(object sender, RoutedEventArgs e)
    {
        if (!_behaviorUiReady || sender is not ToggleSwitch toggle)
        {
            return;
        }

        var settings = Host.BehaviorSettings;
        var previous = settings.RestoreLightingAfterChromaSession;
        try
        {
            settings.RestoreLightingAfterChromaSession = toggle.IsOn;
            settings.Save();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
            InvalidOperationException or System.Security.SecurityException)
        {
            settings.RestoreLightingAfterChromaSession = previous;
            _behaviorUiReady = false;
            toggle.IsOn = previous;
            _behaviorUiReady = true;
            ViewModel.ReportApplicationError(AppStrings.FormatText("BehaviorSettingError", exception.Message));
        }
    }

    private void ModeNotificationToggled(object sender, RoutedEventArgs e)
    {
        if (!_behaviorUiReady || sender is not ToggleSwitch toggle)
        {
            return;
        }

        var settings = Host.BehaviorSettings;
        var previous = settings.ModeChangeNotificationsEnabled;
        try
        {
            settings.ModeChangeNotificationsEnabled = toggle.IsOn;
            settings.Save();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
            InvalidOperationException or System.Security.SecurityException)
        {
            settings.ModeChangeNotificationsEnabled = previous;
            _behaviorUiReady = false;
            toggle.IsOn = previous;
            _behaviorUiReady = true;
            ViewModel.ReportApplicationError(AppStrings.FormatText("BehaviorSettingError",
                exception.Message));
        }
    }

    private async void StartupToggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch toggle && toggle.IsOn != ViewModel.Profiles.IsStartupEnabled)
        {
            await ViewModel.Profiles.SetStartupEnabledAsync(toggle.IsOn, Lifetime);
        }
    }

    private async void SilentStartupToggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch toggle && toggle.IsOn != ViewModel.Profiles.IsSilentStartupEnabled)
        {
            await ViewModel.Profiles.SetSilentStartupEnabledAsync(toggle.IsOn, Lifetime);
        }
    }

    private async void AutomaticUpdatesToggled(object sender, RoutedEventArgs e)
    {
        if (!_updateUiReady || sender is not ToggleSwitch toggle)
        {
            return;
        }

        try
        {
            AppUpdateSettings.AutomaticUpdatesEnabled = toggle.IsOn;
            if (toggle.IsOn && _updateManager.IsInstalled && _availableUpdate is null && _downloadedUpdate is null)
            {
                await CheckForUpdatesAsync(downloadAutomatically: true);
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
            System.Security.SecurityException)
        {
            _updateUiReady = false;
            toggle.IsOn = AppUpdateSettings.AutomaticUpdatesEnabled;
            _updateUiReady = true;
            UpdateStatusText.Text = AppStrings.FormatText("UpdateSettingError",
                exception.Message);
        }
    }

    private async void CheckForUpdatesClick(object sender, RoutedEventArgs e) =>
        await CheckForUpdatesAsync(AutomaticUpdatesToggle.IsOn);

    private async void UpdateActionClick(object sender, RoutedEventArgs e)
    {
        if (_updateBusy)
        {
            return;
        }

        if (_downloadedUpdate is null)
        {
            SetUpdateBusy(true, AppStrings.Text("Text_07BF2232"));
            try
            {
                await DownloadUpdateAsync();
                SetUpdateBusy(false);
                RefreshUpdateUiText();
            }
            catch (OperationCanceledException) when (Lifetime.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                UpdateStatusText.Text = AppStrings.FormatText("UpdateDownloadFailed",
                    exception.Message);
            }
            finally
            {
                SetUpdateBusy(false);
            }
            return;
        }

        SetUpdateBusy(true, AppStrings.Text("Text_B1A6427D"));
        try
        {
            await ((App)Application.Current).ApplyUpdateAndRestartAsync(
                _updateManager,
                _downloadedUpdate);
        }
        catch (Exception exception)
        {
            SetUpdateBusy(false);
            UpdateStatusText.Text = AppStrings.FormatText("UpdateApplyFailed",
                exception.Message);
        }
    }

    private async Task DownloadUpdateAsync()
    {
        if (_availableUpdate is null)
        {
            return;
        }

        UpdateProgressBar.Visibility = Visibility.Visible;
        UpdateProgressBar.Value = 0;
        await _updateManager.DownloadUpdatesAsync(
            _availableUpdate,
            progress => DispatcherQueue.TryEnqueue(() => UpdateProgressBar.Value = progress),
            Lifetime);
        _downloadedUpdate = _availableUpdate.TargetFullRelease;
        _availableUpdate = null;
        UpdateProgressBar.Visibility = Visibility.Collapsed;
    }

    private void SetUpdateBusy(bool busy, string? status = null)
    {
        _updateBusy = busy;
        CheckUpdateButton.IsEnabled = !busy && _updateManager.IsInstalled;
        UpdateActionButton.IsEnabled = !busy;
        AutomaticUpdatesToggle.IsEnabled = !busy;
        if (status is not null)
        {
            UpdateStatusText.Text = status;
        }
        if (!busy)
        {
            UpdateProgressBar.Visibility = Visibility.Collapsed;
        }
    }
}
