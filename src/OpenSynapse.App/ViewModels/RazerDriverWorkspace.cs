using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using OpenSynapse.Core.Diagnostics;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;

namespace OpenSynapse.App.ViewModels;

/// <summary>
/// Finds connected Razer peripherals whose RZCONTROL filter driver is absent
/// and installs the official per-device package. Playback still falls back to
/// SendInput when a device has no package, but every catalogued peripheral can
/// get the same driver path Synapse uses.
/// </summary>
public sealed class RazerDriverWorkspace : INotifyPropertyChanged
{
    private readonly LocalDiagnosticLog _diagnosticLog;
    private readonly ObservableCollection<RazerDriverRowViewModel> _missing = [];
    private bool _isBusy;
    private string _statusText = string.Empty;

    public RazerDriverWorkspace(LocalDiagnosticLog diagnosticLog)
    {
        _diagnosticLog = diagnosticLog;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<RazerDriverRowViewModel> Missing => _missing;

    public bool HasMissing => _missing.Count > 0;

    public bool CanInstall => !_isBusy && _missing.Any(static row => !row.IsInstalled);

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (_isBusy == value) return;
            _isBusy = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanInstall));
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set
        {
            if (_statusText == value) return;
            _statusText = value;
            OnPropertyChanged();
        }
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (_isBusy)
        {
            return;
        }

        IsBusy = true;
        StatusText = AppStrings.Text("RazerDriverChecking");
        try
        {
            var interfaces = await WindowsHidDiscovery.FindVendorInterfacesAsync(
                0x1532,
                cancellationToken).ConfigureAwait(true);
            var endpoints = await RazerFilterEndpointDiscovery.DiscoverAllAsync(cancellationToken)
                .ConfigureAwait(true);
            var ready = endpoints
                .Select(TryGetProductId)
                .Where(static id => id is not null)
                .Select(static id => id!.Value)
                .ToHashSet();

            var rows = new List<RazerDriverRowViewModel>();
            foreach (var group in interfaces.GroupBy(static item => item.ProductId))
            {
                var productId = group.Key;
                if (productId == RazerFilterDriverAdmission.ArgbProductId || ready.Contains(productId))
                {
                    continue;
                }

                if (RazerDeviceRegistry.BuiltIn.Find(0x1532, productId)?.Category == DeviceCategory.Laptop)
                {
                    // Blade models have their own Fn driver card with model-specific guidance.
                    continue;
                }

                if (!RazerDriverPackageCatalog.TryResolve(productId, out var package))
                {
                    continue;
                }

                var name = group.Select(static item => item.ProductName)
                    .FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));
                rows.Add(new RazerDriverRowViewModel(
                    name ?? package.Name,
                    productId,
                    package));
            }

            _missing.Clear();
            foreach (var row in rows.OrderBy(static row => row.Name, StringComparer.OrdinalIgnoreCase))
            {
                _missing.Add(row);
            }

            StatusText = _missing.Count == 0
                ? AppStrings.Text("RazerDriverAllReady")
                : AppStrings.FormatText("RazerDriverMissingCount", _missing.Count);
            _diagnosticLog.TryWrite("razer-driver", StatusText);
            OnPropertyChanged(nameof(HasMissing));
            OnPropertyChanged(nameof(CanInstall));
        }
        catch (OperationCanceledException)
        {
            StatusText = string.Empty;
        }
        catch (Exception exception) when (
            exception is IOException or InvalidOperationException or
                UnauthorizedAccessException or System.ComponentModel.Win32Exception or
                System.Text.Json.JsonException)
        {
            StatusText = AppStrings.FormatText("RazerDriverCheckFailed", exception.Message);
            _diagnosticLog.TryWrite("razer-driver", StatusText);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task InstallAsync(CancellationToken cancellationToken = default)
    {
        if (_isBusy || _missing.Count == 0)
        {
            return;
        }

        IsBusy = true;
        var installed = 0;
        var failed = 0;
        try
        {
            var commonProgress = new Progress<RazerDriverInstallProgress>(update =>
                StatusText = update.Stage switch
                {
                    RazerDriverInstallStage.DownloadingCommon =>
                        AppStrings.Text("RazerDriverCommonDownloading"),
                    _ => AppStrings.Text("RazerDriverCommonInstalling"),
                });
            var common = await RazerCommonDriverInstaller
                .InstallAsync(commonProgress, cancellationToken)
                .ConfigureAwait(true);
            if (!common.Success)
            {
                var commonMessage = DescribeFailure(common);
                foreach (var row in _missing)
                {
                    row.StatusText = commonMessage;
                }

                StatusText = commonMessage;
                _diagnosticLog.TryWrite("razer-driver", StatusText);
                return;
            }

            var packages = _missing
                .Where(static row => row.Package is not null)
                .Select(static row => row.Package!)
                .DistinctBy(static package => package.Url, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            foreach (var package in packages)
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (var row in _missing.Where(row =>
                             string.Equals(row.Package?.Url, package.Url, StringComparison.OrdinalIgnoreCase)))
                {
                    row.StatusText = AppStrings.Text("RazerDriverWaiting");
                }

                var progress = new Progress<RazerDriverInstallProgress>(update =>
                    StatusText = update.Stage switch
                    {
                        RazerDriverInstallStage.Downloading =>
                            AppStrings.FormatText("RazerDriverDownloading", update.PackageName),
                        RazerDriverInstallStage.DownloadingCommon =>
                            AppStrings.Text("RazerDriverCommonDownloading"),
                        RazerDriverInstallStage.InstallingCommon =>
                            AppStrings.Text("RazerDriverCommonInstalling"),
                        _ => AppStrings.FormatText("RazerDriverInstalling", update.PackageName),
                    });
                var result = await RazerDriverInstaller.InstallAsync(package, progress, cancellationToken)
                    .ConfigureAwait(true);
                if (result.Success)
                {
                    installed++;
                    foreach (var row in _missing.Where(row =>
                                 string.Equals(row.Package?.Url, package.Url, StringComparison.OrdinalIgnoreCase)))
                    {
                        row.IsInstalled = true;
                        row.StatusText = AppStrings.Text("RazerDriverInstalledRestart");
                    }
                }
                else
                {
                    failed++;
                    var message = DescribeFailure(result);
                    foreach (var row in _missing.Where(row =>
                                 string.Equals(row.Package?.Url, package.Url, StringComparison.OrdinalIgnoreCase)))
                    {
                        row.StatusText = message;
                    }
                }
            }

            StatusText = failed == 0
                ? AppStrings.FormatText("RazerDriverInstallSummary", installed)
                : AppStrings.FormatText("RazerDriverInstallSummaryPartial", installed, failed);
            _diagnosticLog.TryWrite("razer-driver", StatusText);
        }
        catch (OperationCanceledException)
        {
            StatusText = AppStrings.Text("RazerDriverInstallCancelled");
        }
        catch (Exception exception) when (
            exception is IOException or InvalidOperationException or
                UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            StatusText = AppStrings.FormatText("RazerDriverInstallFailed", exception.Message);
            _diagnosticLog.TryWrite("razer-driver", StatusText);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string DescribeFailure(RazerDriverInstallResult result) =>
        result.Failure switch
        {
            RazerDriverInstallFailure.Cancelled =>
                AppStrings.Text("RazerDriverInstallCancelled"),
            RazerDriverInstallFailure.Timeout =>
                AppStrings.Text("RazerDriverInstallTimeout"),
            RazerDriverInstallFailure.ExitCode =>
                AppStrings.FormatText("RazerDriverInstallExitCode", $"0x{result.ExitCode:X8}"),
            RazerDriverInstallFailure.InvalidPackage =>
                AppStrings.Text("RazerDriverInstallInvalidPackage"),
            RazerDriverInstallFailure.DownloadFailed =>
                AppStrings.Text("RazerDriverDownloadFailed"),
            RazerDriverInstallFailure.StartFailed =>
                AppStrings.Text("RazerDriverInstallStartFailed"),
            _ => AppStrings.FormatText("RazerDriverInstallFailed", string.Empty),
        };

    private static ushort? TryGetProductId(string path)
    {
        const string marker = "PID_";
        var index = path.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0 || index + marker.Length + 4 > path.Length)
        {
            return null;
        }

        return ushort.TryParse(
            path.AsSpan(index + marker.Length, 4),
            System.Globalization.NumberStyles.HexNumber,
            null,
            out var productId)
            ? productId
            : null;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class RazerDriverRowViewModel : INotifyPropertyChanged
{
    private string _statusText = AppStrings.Text("RazerDriverMissingStatus");

    internal RazerDriverRowViewModel(
        string name,
        ushort productId,
        RazerDriverPackage package)
    {
        Name = name;
        ProductId = productId.ToString("X4");
        Package = package;
        Detail = AppStrings.FormatText("RazerDriverDetail", ProductId, package.Version);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name { get; }
    public string ProductId { get; }
    public string Detail { get; }
    internal RazerDriverPackage? Package { get; }
    internal bool IsInstalled { get; set; }

    public string StatusText
    {
        get => _statusText;
        internal set
        {
            if (_statusText == value) return;
            _statusText = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusText)));
        }
    }
}
