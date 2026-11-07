using Microsoft.UI.Xaml;
using OpenSynapse.Windows.Lighting;

namespace OpenSynapse.App.Views;

public sealed partial class SettingsPage
{
    private void RefreshChromaShimStatus()
    {
        var installed = ChromaSdkShimInstaller.IsInstalled;
        ChromaShimButton.Content = AppStrings.Text(
            installed ? "ChromaShimRestoreButton.Content" : "ChromaShimInstallButton.Content");
        ChromaShimButton.IsEnabled = installed || ChromaSdkShimInstaller.CanInstall;
        ChromaShimStatusText.Text = installed
            ? AppStrings.Text("ChromaShimStatusInstalled")
            : ChromaSdkShimInstaller.CanInstall
                ? AppStrings.Text("ChromaShimStatusNotInstalled")
                : AppStrings.Text("ChromaShimStatusUnavailable");
    }

    private async void ChromaShimActionClick(object sender, RoutedEventArgs e)
    {
        ChromaShimButton.IsEnabled = false;
        ChromaShimStatusText.Text = AppStrings.Text("ChromaShimStatusBusy");
        try
        {
            var installed = ChromaSdkShimInstaller.IsInstalled;
            var code = installed
                ? await ChromaSdkShimInstaller.RestoreAsync()
                : await ChromaSdkShimInstaller.InstallAsync();
            if (code != (int)ChromaSdkShimResult.Success &&
                code != (int)ChromaSdkShimResult.Cancelled)
            {
                ViewModel.ReportApplicationError(AppStrings.FormatText(
                    installed ? "ChromaShimRestoreFailed" : "ChromaShimInstallFailed",
                    code));
            }
        }
        finally
        {
            RefreshChromaShimStatus();
        }
    }
}
