using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Reflection;
using Velopack;
using Velopack.Sources;

namespace OpenSynapse.App;

public sealed partial class MainWindow
{
    private const string UpdateRepositoryUrl = "https://github.com/A1mAssist/OpenSynapse";
    private readonly UpdateManager _updateManager = new(
        new GithubSource(UpdateRepositoryUrl, null, prerelease: false));
    private bool _updateUiReady;
    private bool _updateBusy;
    private UpdateInfo? _availableUpdate;
    private VelopackAsset? _downloadedUpdate;

    private void InitializeUpdateUi()
    {
        AutomaticUpdatesToggle.IsOn = AppUpdateSettings.AutomaticUpdatesEnabled;
        _downloadedUpdate = _updateManager.UpdatePendingRestart;
        _updateUiReady = true;
        RefreshUpdateUiText();
    }

    private void RefreshUpdateUiText()
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
            UpdateActionButton.Content = AppStrings.Text("更新并重启");
            UpdateActionButton.Visibility = Visibility.Visible;
            UpdateActionButton.IsEnabled = !_updateBusy;
            return;
        }

        if (_availableUpdate is not null)
        {
            UpdateStatusText.Text = AppStrings.FormatText("UpdateAvailable",
                _availableUpdate.TargetFullRelease.Version);
            UpdateActionButton.Content = AppStrings.Text("下载更新");
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

    private async Task CheckForUpdatesAsync(bool downloadAutomatically)
    {
        if (_updateBusy || !_updateManager.IsInstalled)
        {
            return;
        }

        SetUpdateBusy(true, AppStrings.Text("正在检查更新"));
        try
        {
            _availableUpdate = await _updateManager.CheckForUpdatesAsync();
            AppUpdateSettings.MarkCheckCompleted();
            if (_availableUpdate is null)
            {
                UpdateStatusText.Text = AppStrings.Text("当前已是最新版本");
                UpdateActionButton.Visibility = Visibility.Collapsed;
            }
            else if (downloadAutomatically)
            {
                UpdateStatusText.Text = AppStrings.Text("正在下载更新");
                await DownloadUpdateAsync();
            }

            SetUpdateBusy(false);
            if (_availableUpdate is not null || _downloadedUpdate is not null)
            {
                RefreshUpdateUiText();
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
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

    private async void UpdateActionClick(object sender, RoutedEventArgs e)
    {
        if (_updateBusy)
        {
            return;
        }

        if (_downloadedUpdate is null)
        {
            SetUpdateBusy(true, AppStrings.Text("正在下载更新"));
            try
            {
                await DownloadUpdateAsync();
                SetUpdateBusy(false);
                RefreshUpdateUiText();
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
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

        SetUpdateBusy(true, AppStrings.Text("正在安全退出并更新"));
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
            progress => _dispatcherQueue.TryEnqueue(() => UpdateProgressBar.Value = progress),
            _lifetime.Token);
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
