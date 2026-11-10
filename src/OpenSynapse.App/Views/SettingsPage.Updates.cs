using System.ComponentModel;
using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace OpenSynapse.App.Views;

public sealed partial class SettingsPage
{
    private Task? _startupUpdateCheck;
    private bool _updatePromptVisible;
    private string? _promptedUpdateVersion;

    internal void StartStartupUpdateCheck()
    {
        if (!AutomaticUpdatesEnabled || _downloadedUpdate is not null ||
            !AppUpdateSettings.AutomaticCheckDue || !_updateManager.IsInstalled)
        {
            return;
        }

        _startupUpdateCheck = CheckForUpdatesAsync(downloadAutomatically: false);
    }

    internal async Task PromptForStartupUpdateAsync()
    {
        if (_updatePromptVisible || Host.Content?.XamlRoot is not { } xamlRoot ||
            !Host.AppWindow.IsVisible)
        {
            return;
        }

        if (_downloadedUpdate is not null)
        {
            var downloadedVersion = _downloadedUpdate.Version.ToString();
            if (_promptedUpdateVersion == downloadedVersion)
            {
                return;
            }

            _updatePromptVisible = true;
            try
            {
                var restartDialog = new ContentDialog
                {
                    XamlRoot = xamlRoot,
                    Title = AppStrings.Text("UpdateRestartTitle"),
                    Content = AppStrings.FormatText("UpdateRestartBody", downloadedVersion),
                    PrimaryButtonText = AppStrings.Text("UpdateRestartNow"),
                    CloseButtonText = AppStrings.Text("UpdateRestartLater"),
                };
                var result = await restartDialog.ShowAsync();
                _promptedUpdateVersion = downloadedVersion;
                if (result == ContentDialogResult.Primary)
                {
                    await ApplyDownloadedUpdateAsync();
                }
            }
            finally
            {
                _updatePromptVisible = false;
            }
            return;
        }

        if (_startupUpdateCheck is null)
        {
            return;
        }

        try
        {
            await _startupUpdateCheck;
        }
        catch (OperationCanceledException) when (Lifetime.IsCancellationRequested)
        {
            return;
        }
        catch
        {
            return;
        }

        if (_availableUpdate is null)
        {
            return;
        }

        var version = _availableUpdate.TargetFullRelease.Version.ToString();
        if (version == AppUpdateSettings.SkippedUpdateVersion ||
            version == _promptedUpdateVersion)
        {
            return;
        }

        var currentVersion = _updateManager.CurrentVersion?.ToString() ??
            Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "--";
        var bodyKey = IsMachineWideInstall ? "UpdatePromptBodyElevated" : "UpdatePromptBody";
        _updatePromptVisible = true;
        try
        {
            var dialog = new ContentDialog
            {
                XamlRoot = xamlRoot,
                Title = AppStrings.FormatText("UpdatePromptTitle", version),
                Content = AppStrings.FormatText(bodyKey, currentVersion),
                PrimaryButtonText = AppStrings.Text("UpdatePromptNow"),
                SecondaryButtonText = AppStrings.Text("UpdatePromptSkip"),
                CloseButtonText = AppStrings.Text("UpdatePromptLater"),
            };
            var result = await dialog.ShowAsync();
            _promptedUpdateVersion = version;
            if (result == ContentDialogResult.Primary)
            {
                Host.RequestNavigation("settings");
                await DownloadAndInstallAsync();
            }
            else if (result == ContentDialogResult.Secondary)
            {
                AppUpdateSettings.SkipUpdateVersion(version);
            }
        }
        finally
        {
            _updatePromptVisible = false;
        }
    }

    private async Task DownloadAndInstallAsync()
    {
        await DownloadUpdateFromUiAsync();
        if (_downloadedUpdate is null || Host.Content?.XamlRoot is not { } xamlRoot ||
            !Host.AppWindow.IsVisible)
        {
            return;
        }

        var restartDialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = AppStrings.Text("UpdateRestartTitle"),
            Content = AppStrings.FormatText("UpdateRestartBody", _downloadedUpdate.Version),
            PrimaryButtonText = AppStrings.Text("UpdateRestartNow"),
            CloseButtonText = AppStrings.Text("UpdateRestartLater"),
        };
        if (await restartDialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await ApplyDownloadedUpdateAsync();
        }
    }

    private async Task DownloadUpdateFromUiAsync()
    {
        if (_availableUpdate is null)
        {
            return;
        }

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
    }

    private async Task ApplyDownloadedUpdateAsync()
    {
        if (_downloadedUpdate is null)
        {
            return;
        }

        SetUpdateBusy(true, AppStrings.Text("Text_B1A6427D"));
        try
        {
            await ((App)Application.Current).ApplyUpdateAndRestartAsync(
                _updateManager,
                _downloadedUpdate);
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            SetUpdateBusy(false);
            UpdateStatusText.Text = AppStrings.Text("UpdateElevationCancelled");
        }
        catch (Exception exception)
        {
            SetUpdateBusy(false);
            UpdateStatusText.Text = AppStrings.FormatText("UpdateApplyFailed",
                exception.Message);
        }
    }
}
