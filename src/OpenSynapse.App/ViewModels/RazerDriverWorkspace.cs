using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using OpenSynapse.Core.Diagnostics;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Lifecycle;

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
    private readonly HashSet<ushort> _installedProductIds = [];
    private bool _isBusy;
    private string _statusText = string.Empty;

    public RazerDriverWorkspace(LocalDiagnosticLog diagnosticLog)
    {
        _diagnosticLog = diagnosticLog;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<RazerDriverRowViewModel> Missing => _missing;

    public bool HasMissing => _missing.Count > 0;

    public bool CanInstall => !_isBusy && _missing.Count > 0;

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
            var ready = BuildReadySet(endpoints);

            var rows = new List<RazerDriverRowViewModel>();
            foreach (var group in interfaces.GroupBy(static item => item.ProductId))
            {
                var productId = group.Key;
                if (productId == RazerFilterDriverAdmission.ArgbProductId)
                {
                    continue;
                }

                // Only mice and keyboards use the RZCONTROL endpoint this driver adds.
                if (!RazerFilterDriverAdmission.NeedsFilterDriver(productId))
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

                var candidates = RazerDriverPackageCatalog.GetCandidateProductIds(productId, package);
                // The endpoint only proves the package reached the device stack. A mouse
                // endpoint can exist while its keyboard collections still run on kbdhid,
                // and macro injection needs those collections. Keep the row so the page
                // can say "filter missing" and offer the repair path.
                var hasEndpoint = candidates.Any(ready.Contains);
                if (hasEndpoint && RazerFilterInfBinder.InspectAttachment(candidates).IsAttached)
                {
                    continue;
                }

                var name = group.Select(static item => item.ProductName)
                    .FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));
                var row = new RazerDriverRowViewModel(name ?? package.Name, productId, package, candidates);
                if (hasEndpoint || _installedProductIds.Overlaps(candidates) || IsPackageInstalled(candidates))
                {
                    // The package is installed but the endpoint is still missing. Start
                    // the per-device service once, then report whether the filter is
                    // actually attached or only staged.
                    row.IsInstalled = true;
                    await EnsureDriverServicesStartedAsync(candidates, cancellationToken)
                        .ConfigureAwait(true);
                    var attachment = RazerFilterInfBinder.InspectAttachment(candidates);
                    _diagnosticLog.TryWrite(
                        "razer-driver",
                        $"Razer filter {productId} refresh attached={attachment.IsAttached}; " +
                        $"keyboard={attachment.AttachedKeyboardInstances}/{attachment.KeyboardInstances}; " +
                        $"missing: {string.Join(", ", RazerFilterInfBinder.FindUnfilteredKeyboardInstances(candidates))}");
                    row.StatusText = attachment.IsAttached
                        ? AppStrings.Text("RazerDriverEndpointPending")
                        : AppStrings.Text("RazerDriverFilterMissing");
                }
                rows.Add(row);
            }

            _missing.Clear();
            foreach (var row in rows.OrderBy(static row => row.Name, StringComparer.OrdinalIgnoreCase))
            {
                _missing.Add(row);
            }

            StatusText = _missing.Count == 0
                ? AppStrings.Text("RazerDriverAllReady")
                : _missing.All(static row => row.IsInstalled)
                    ? AppStrings.FormatText("RazerDriverPendingCount", _missing.Count)
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
        try
        {
            var commonProgress = new Progress<RazerDriverInstallProgress>(update =>
                StatusText = update.Stage switch
                {
                    RazerDriverInstallStage.DownloadingCommon =>
                        AppStrings.Text("RazerDriverCommonDownloading"),
                    _ => AppStrings.Text("RazerDriverCommonInstalling"),
                });
            var commonViaService = await OpenSynapseServiceClient
                .TryInstallRazerCommonAsync(cancellationToken)
                .ConfigureAwait(true);
            var common = commonViaService is { } commonInstalled
                ? new RazerDriverInstallResult(
                    commonInstalled,
                    commonInstalled
                        ? RazerDriverInstallFailure.None
                        : RazerDriverInstallFailure.ExitCode)
                : await RazerCommonDriverInstaller
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

            if (await RazerCommonDriverInstaller.EnsureStartedAsync(cancellationToken)
                    .ConfigureAwait(true) is { } serviceError)
            {
                _diagnosticLog.TryWrite(
                    "razer-driver",
                    $"RzCommon start failed: {serviceError}");
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
                var rowsForPackage = _missing.Where(row =>
                        string.Equals(row.Package?.Url, package.Url, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                StatusText = AppStrings.FormatText("RazerDriverInstalling", package.Name);
                var serviceResult = await OpenSynapseServiceClient
                    .TryInstallRazerDeviceDriverAsync(rowsForPackage[0].ProductIdValue, cancellationToken)
                    .ConfigureAwait(true);
                var result = serviceResult ??
                    await RazerDriverInstaller.InstallAsync(package, progress, cancellationToken)
                        .ConfigureAwait(true);
                if (result.Success)
                {
                    installed++;
                    foreach (var row in rowsForPackage)
                    {
                        row.IsInstalled = true;
                        foreach (var productId in row.CandidateProductIds)
                        {
                            _installedProductIds.Add(productId);
                        }

                        await EnsureDriverServicesStartedAsync(
                            row.CandidateProductIds, cancellationToken).ConfigureAwait(true);
                        row.EndpointVerified = await WaitForEndpointAsync(
                            row.CandidateProductIds, cancellationToken).ConfigureAwait(true);
                        var attachment = RazerFilterInfBinder.InspectAttachment(row.CandidateProductIds);
                        if (!row.EndpointVerified || !attachment.IsAttached)
                        {
                            // A silent vendor install only stages the filter INF. A staged
                            // INF never reaches the device stack, so bind it explicitly;
                            // a reboot alone cannot do this.
                            var serviceBind = await OpenSynapseServiceClient
                                .TryBindRazerFilterAsync(row.ProductIdValue, cancellationToken)
                                .ConfigureAwait(true);
                            var bind = serviceBind ?? await RazerFilterInfBinder
                                .ApplyAsync(row.ProductIdValue, cancellationToken)
                                .ConfigureAwait(true);
                            if (bind.Status == RazerFilterBindStatus.NoStagedInf)
                            {
                                row.StatusText = AppStrings.Text("RazerDriverNoStagedInf");
                                _diagnosticLog.TryWrite(
                                    "razer-driver",
                                    $"Razer filter bind {row.ProductId}: no staged INF");
                                continue;
                            }

                            if (bind.Status == RazerFilterBindStatus.Failed)
                            {
                                // Another staged INF may still have attached; the
                                // attachment check below decides what to tell the user.
                                _diagnosticLog.TryWrite(
                                    "razer-driver",
                                    $"Razer filter bind {row.ProductId}: {bind.Error ?? "unknown"}");
                            }

                            await EnsureDriverServicesStartedAsync(
                                row.CandidateProductIds, cancellationToken).ConfigureAwait(true);
                            attachment = RazerFilterInfBinder.InspectAttachment(row.CandidateProductIds);
                            _diagnosticLog.TryWrite(
                                "razer-driver",
                                $"Razer filter {row.ProductId} attached={attachment.IsAttached}; " +
                                $"keyboard={attachment.AttachedKeyboardInstances}/{attachment.KeyboardInstances}; " +
                                $"instances: {string.Join(", ", RazerFilterInfBinder.FindInstances(row.CandidateProductIds))}; " +
                                $"missing: {string.Join(", ", RazerFilterInfBinder.FindUnfilteredKeyboardInstances(row.CandidateProductIds))}");
                            if (!attachment.IsAttached)
                            {
                                row.StatusText = AppStrings.Text("RazerDriverFilterMissing");
                                continue;
                            }

                            row.EndpointVerified = await WaitForEndpointAsync(
                                row.CandidateProductIds, cancellationToken, attempts: 10)
                                .ConfigureAwait(true);
                            if (!row.EndpointVerified)
                            {
                                row.StatusText = AppStrings.Text("RazerDriverEndpointPending");
                                continue;
                            }
                        }

                        row.StatusText = AppStrings.Text("RazerDriverInstalledOnline");
                        _missing.Remove(row);
                    }
                }
                else
                {
                    var message = DescribeFailure(result);
                    foreach (var row in rowsForPackage)
                    {
                        row.StatusText = message;
                    }
                }
            }

            StatusText = _missing.Count == 0
                ? AppStrings.FormatText("RazerDriverInstallSummary", installed)
                : AppStrings.FormatText("RazerDriverInstallSummaryPartial", installed, _missing.Count);
            OnPropertyChanged(nameof(HasMissing));
            OnPropertyChanged(nameof(CanInstall));
            _diagnosticLog.TryWrite("razer-driver", StatusText);
        }
        catch (OperationCanceledException)
        {
            StatusText = AppStrings.Text("RazerDriverInstallCancelled");
        }
        catch (DedicatedGpuOperationUncertainException)
        {
            StatusText = AppStrings.Text("RazerDriverInstallUncertain");
            _diagnosticLog.TryWrite("razer-driver", StatusText);
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
                result.Error is { Length: > 0 } error
                    ? AppStrings.FormatText("RazerDriverInstallFailed", error)
                    : AppStrings.FormatText("RazerDriverInstallExitCode", $"0x{result.ExitCode:X8}"),
            RazerDriverInstallFailure.InvalidPackage =>
                AppStrings.Text("RazerDriverInstallInvalidPackage"),
            RazerDriverInstallFailure.DownloadFailed =>
                AppStrings.Text("RazerDriverDownloadFailed"),
            RazerDriverInstallFailure.StartFailed =>
                AppStrings.Text("RazerDriverInstallStartFailed"),
            _ => AppStrings.FormatText("RazerDriverInstallFailed", string.Empty),
        };

    /// <summary>
    /// The vendor package keeps its uninstaller under this per-product folder,
    /// which is the only durable trace of an install whose endpoint is still
    /// pending a restart. Synapse 3 writes ProgramData; the old per-user folder
    /// stays as a compatibility fallback.
    /// </summary>
    private static bool IsPackageInstalled(IReadOnlyList<ushort> candidates) =>
        RazerDriverIdentity.IsPackageInstalled(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            candidates);

    private static HashSet<ushort> BuildReadySet(IEnumerable<string> endpoints) =>
        RazerDriverIdentity.ExpandReady(
            endpoints
                .Select(RazerDriverIdentity.TryParseEndpointProductId)
                .Where(static id => id is not null)
                .Select(static id => id!.Value));

    private static async Task EnsureDriverServicesStartedAsync(
        IReadOnlyList<ushort> candidates,
        CancellationToken cancellationToken)
    {
        foreach (var productId in candidates)
        {
            var serviceName = $"RzDev_{productId:X4}";
            try
            {
                if (await OpenSynapseServiceClient
                        .TryStartDriverServiceAsync(serviceName, cancellationToken)
                        .ConfigureAwait(true))
                {
                    return;
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // The service did not answer in time; try the in-process start.
            }

            if (WindowsServiceControl.TryStart(serviceName, out _))
            {
                return;
            }
        }
    }

    private static async Task<bool> WaitForEndpointAsync(
        IReadOnlyList<ushort> candidates,
        CancellationToken cancellationToken,
        int attempts = 6)
    {
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            var endpoints = await RazerFilterEndpointDiscovery
                .DiscoverAllAsync(cancellationToken).ConfigureAwait(false);
            if (endpoints.Any(path => candidates.Any(productId =>
                    RazerFilterEndpointDiscovery.MatchesProduct(path, productId))))
            {
                return true;
            }

            if (attempt < attempts - 1)
            {
                await Task.Delay(500, cancellationToken).ConfigureAwait(false);
            }
        }

        return false;
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
        RazerDriverPackage package,
        IReadOnlyList<ushort> candidateProductIds)
    {
        Name = name;
        ProductId = productId.ToString("X4");
        ProductIdValue = productId;
        Package = package;
        CandidateProductIds = candidateProductIds;
        Detail = AppStrings.FormatText("RazerDriverDetail", ProductId, package.Version);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name { get; }
    public string ProductId { get; }
    public string Detail { get; }
    internal ushort ProductIdValue { get; }
    internal RazerDriverPackage? Package { get; }
    internal IReadOnlyList<ushort> CandidateProductIds { get; }
    internal bool IsInstalled { get; set; }
    internal bool EndpointVerified { get; set; }

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
