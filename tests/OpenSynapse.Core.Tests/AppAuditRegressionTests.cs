using System.Text.RegularExpressions;
using Xunit;

namespace OpenSynapse.Core.Tests;

// App is deliberately not linked into Core.Tests. These checks protect UI source
// contracts; interaction, clipboard and real-device acceptance remain separate.
public sealed class AppAuditRegressionTests
{
    [Fact]
    public void FanRefreshReleasesChangedOwnersWithoutReplayingStoredFixedTargets()
    {
        var source = Method(Vm("FanWorkspace.cs"), "internal async Task<BladeFanProfileApplyResult> ApplyProfileAsync");
        var release = source.IndexOf("StopAsync(\"profile-ownership-change\")", StringComparison.Ordinal);
        var early = source.IndexOf("if (!writeStoredTarget && curve is null)", StringComparison.Ordinal);
        var start = source.IndexOf("await _bladeFanRuntime.StartFixedAsync", StringComparison.Ordinal);
        Assert.True(release >= 0 && release < early && early < start);
        Assert.Contains("document.ActiveProfileName", source[..early]);
        Assert.Contains("BladeFanProfileFingerprint.MatchesRunning", source[early..start]);
        Assert.Contains("StopAsync(\"profile-refresh-release\")", source[early..start]);
        Assert.Contains("await _fan.ApplyProfileAsync(blade, powerState, cancellationToken, writeStoredTarget: false)", Vm("RefreshWorkspace.cs"));
        Assert.DoesNotContain("FanProfileRefreshCoordinator", Vm("RefreshWorkspace.cs"));
    }

    [Fact]
    public void MigrationSaveFailureCannotReplaceSuccessfullyLoadedDocument()
    {
        var source = Vm("ProfilesWorkspace.Loading.cs");
        Assert.Single(Regex.Matches(source, @"_setProfile\(ProfileDocument.CreateDefault\(\)\)").Cast<Match>());
        var success = source[source.IndexOf("_setProfile(loaded)", StringComparison.Ordinal)..];
        Assert.DoesNotContain("CreateDefault", success);
        Assert.Contains("await _profileStore.SaveAsync", success);
        Assert.Contains("ProfileSaveError", success);
        Assert.DoesNotContain("ProfileLoadError", success);
        Assert.True(source.IndexOf("return;", StringComparison.Ordinal) < source.IndexOf("_setProfile(loaded)", StringComparison.Ordinal));
    }

    [Fact]
    public void SaveContractAndDisplayRollbackPreserveFalseAndExceptionSemantics()
    {
        var root = Vm("MainViewModel.cs");
        var save = Method(root, "private async Task<bool> SaveProfileAsync");
        Assert.Contains("return false;", save);
        Assert.Contains("IOException or UnauthorizedAccessException", save);
        Assert.Contains("throw;", save);
        var operations = Method(Vm("MainViewModel.Operations.cs"), "private async Task RunDeviceOperationAsync");
        Assert.True(operations.IndexOf("_deviceOperationErrorText", StringComparison.Ordinal) < operations.IndexOf("DeviceOperationSucceeded", StringComparison.Ordinal));
        Assert.Contains("return;", operations[operations.IndexOf("_deviceOperationErrorText", StringComparison.Ordinal)..operations.IndexOf("DeviceOperationSucceeded", StringComparison.Ordinal)]);
        var display = Method(Vm("DisplayWorkspace.cs"), "public async Task ApplyInternalDisplayRefreshRateAsync");
        Assert.Equal(2, Regex.Matches(display, "finally").Count);
        Assert.Equal(2, Regex.Matches(display, "profile.RefreshRateHertz = previous").Count);
        Assert.DoesNotContain("_setProfile", display);
    }

    [Fact]
    public void AllConnectedDeviceSourcesUseTheSameNavigationDispatcher()
    {
        var rows = Vm("ConnectedDeviceRowViewModel.cs");
        var source = App("Views/DevicesPage.RazerHid.cs");
        var dispatch = Method(source, "private async Task SelectDeviceSourceAsync");
        var types = Regex.Matches(rows, @"public ConnectedDeviceRowViewModel\(\s*(\w+)\s+")
            .Select(match => match.Groups[1].Value).Distinct().ToArray();
        Assert.True(types.Length >= 12);
        foreach (var type in types) Assert.Contains("case " + type, dispatch);
        var click = Method(source, "private async void ConnectedDeviceCardClick");
        Assert.Contains("await SelectDeviceSourceAsync(button.Tag)", click);
        Assert.DoesNotContain("switch", click);
        Assert.Contains("ReportApplicationError", click);
        Assert.Contains("SelectDeviceSourceAsync", App("Views/DevicesPage.xaml.cs"));
        Assert.Contains("RequestDeviceNavigation", App("App.xaml.cs"));
    }

    [Fact]
    public void CopyDiagnosticsRefreshesEveryBackendAndRetainsObservedDisconnectErrors()
    {
        var root = Vm("MainViewModel.Devices.cs");
        var provider = Method(root, "private IEnumerable<AccessoryDiagnostic> ReadAccessoryDiagnostics");
        foreach (var backend in new[] { "BlackShark", "HeadsetAudio", "Keyboard", "Gamepad", "Chroma", "BLE", "AudioAWKittyBLE", "CoolingPad", "Kiyo", "Hue" })
            Assert.Contains("\"" + backend + "\"", provider);
        Assert.DoesNotContain("ConnectedDevices", provider);
        Assert.Contains("Mixer053E?.ErrorText", provider);
        var refresh = Method(root, "private async Task RefreshAttachedDevicesAsync");
        Assert.True(refresh.IndexOf("CaptureAccessorySnapshot", StringComparison.Ordinal) < refresh.IndexOf("await RefreshBlackSharkAsync", StringComparison.Ordinal));
        Assert.Contains("RefreshAccessoryRows();", Method(Vm("DiagnosticsWorkspace.cs"), "public string CreateReport"));
        var snapshots = Vm("DiagnosticsWorkspace.Accessories.cs");
        Assert.Contains("DiagnosticPreviouslyObserved", snapshots);
        Assert.Contains("row.Error is null", snapshots);
        Assert.Contains("row with { Error = previous.Error }", snapshots);
        Assert.Contains("StringComparer.OrdinalIgnoreCase", snapshots);
        Assert.DoesNotContain("if (Rows.Count > 0", Vm("DiagnosticsWorkspace.RazerHid.cs"));
        Assert.Contains("openRazerErrorCount + _accessoryErrorCount()", Vm("RefreshWorkspace.cs"));
    }

    [Fact]
    public void BrightnessRepliesCannotPublishOverNewInputOrDropReturnToConfirmedValue()
    {
        var queue = Method(Vm("DisplayWorkspace.cs"), "internal Task QueueInternalDisplayBrightnessAsync");
        Assert.Contains("_desiredInternalDisplayBrightness ?? _internalDisplayBrightnessInFlight ??", queue);
        Assert.Contains("double.IsFinite(percent)", queue);
        var writer = Vm("DisplayWorkspace.Brightness.cs");
        Assert.Contains("_internalDisplayBrightnessInFlight = requested", writer);
        Assert.Contains("if (_desiredInternalDisplayBrightness is null)", writer);
        Assert.DoesNotContain("_desiredInternalDisplayBrightness = null", writer[writer.IndexOf("catch (Exception", StringComparison.Ordinal)..]);
        var failure = writer[writer.IndexOf("catch (Exception", StringComparison.Ordinal)..writer.IndexOf("finally", StringComparison.Ordinal)];
        Assert.True(failure.IndexOf("continue;", StringComparison.Ordinal) < failure.IndexOf("InternalDisplayBrightnessPercent =", StringComparison.Ordinal));
        var completion = writer.IndexOf("_confirmedInternalDisplayBrightnessPercent =", StringComparison.Ordinal);
        var guard = writer.IndexOf("if (_desiredInternalDisplayBrightness is null)", completion, StringComparison.Ordinal);
        var publish = writer.IndexOf("InternalDisplayBrightnessPercent = _confirmed", completion, StringComparison.Ordinal);
        Assert.True(guard < publish);
    }

    [Fact]
    public void TrayResultsJoinByStableKeyAndRejectOldMenuGenerations()
    {
        var read = Vm("TrayWorkspace.cs");
        Assert.Contains("manifest:{row.Id}", read);
        Assert.Contains("openrazer:{row.Connection.InstanceId}", read);
        Assert.Contains("foreach (var row in BatteryDevices().ToArray())", read);
        Assert.Contains("Id = descriptor.Id", Vm("DeviceRowViewModel.cs"));
        var apply = App("TrayMenuWindow.Batteries.cs");
        Assert.Contains("ToLookup(device => device.Key, StringComparer.OrdinalIgnoreCase)", apply);
        Assert.Contains("byKey?[_batteryKeys[index]]", apply);
        Assert.Contains("Length: 1", apply);
        Assert.Contains("!ReferenceEquals(_batteryRead, cancellation)", apply);
        Assert.Contains("cancellation.IsCancellationRequested", apply);
        Assert.Contains("!_menuOpen", apply);
        Assert.DoesNotContain("results[index]", apply);
    }

    [Fact]
    public void MainViewModelGroupFitsBudgetAndCompositionCallersUseWorkspaces()
    {
        var directory = Path.Combine(RepoLayout.Root, "src", "OpenSynapse.App", "ViewModels");
        var lines = Directory.EnumerateFiles(directory, "MainViewModel*.cs").Sum(file => File.ReadLines(file).Count());
        Assert.True(lines <= 1443, $"MainViewModel: {lines} > 1443");
        var root = Vm("MainViewModel.cs");
        Assert.Contains("public UsbAccessoriesWorkspace Accessories", root);
        Assert.Contains("public CpuPowerLimitWorkspace CpuPower", root);
        Assert.DoesNotContain("public RazerHeadsetAudioViewModel", root);
        Assert.Contains("CpuPower.ReplayCpuPowerLimitAfterResumeAsync", App("DisplayPowerNotification.cs"));
        Assert.Contains("CpuPower.RefreshCpuPowerLimitSafelyAsync", App("MainWindow.xaml.cs"));
        Assert.Contains("CpuPower?.NotifyBusyChanged()", root);
        Assert.Contains("await Accessories.DisposeAsync()", root);
        var xaml = App("Views/DevicesPage.xaml");
        Assert.Contains("{Binding Accessories.SelectedHeadsetAudio}", xaml);
        Assert.Contains("{Binding CpuPower.CpuPowerLimitIndex", xaml);
        Assert.Contains("SpecialLightingService", Vm("UsbAccessoriesWorkspace.cs"));
        Assert.Contains("Onboard.LocalMacroProvider", Vm("UsbAccessoriesWorkspace.cs"));
        foreach (var file in new[] { "UsbAccessoriesWorkspace.cs", "CpuPowerLimitWorkspace.cs" })
            Assert.True(File.ReadLines(Path.Combine(directory, file)).Count() <= 600);
    }

    [Fact]
    public void BehaviorLoadFailureLogsOnlyTheFailureAndRecoveryPath()
    {
        Assert.Contains("LoadBehaviorSettings();", App("App.xaml.cs"));
        var source = Method(App("App.Lifecycle.cs"), "private void LoadBehaviorSettings");
        var load = source.IndexOf("_behaviorSettings = AppBehaviorSettings.Load();", StringComparison.Ordinal);
        var recovery = source.IndexOf("if (_behaviorSettings.LoadError is { } loadError)", load, StringComparison.Ordinal);
        Assert.True(load >= 0 && recovery > load);
        var block = Method(source[(recovery + "if (_behaviorSettings.LoadError is { } loadError)".Length)..], string.Empty);
        Assert.Contains("_diagnosticLog.TryWrite(\"behavior\"", block);
        Assert.Contains("RecoveryFilePath", block);
        Assert.Contains("unavailable; saving blocked", block);
        Assert.DoesNotContain("HueAppKey", block);
        Assert.DoesNotContain("Serialize", block);
    }

    [Fact]
    public void RazerHidTimeoutsRemainRetryable()
    {
        var helper = Vm("RazerHidDeviceViewModel.Helpers.cs");
        var failure = Method(helper, "private void HandleFailure");
        Assert.True(failure.IndexOf("exception is RazerDeviceTimeoutException", StringComparison.Ordinal) <
            failure.IndexOf("_unsupported.Add(unsupported)", StringComparison.Ordinal));
        Assert.DoesNotContain("_unsupported.Add(unanswered)", failure);
        Assert.Contains("exception is not RazerDeviceTimeoutException",
            Method(helper, "private async Task ReadZoneAsync"));
        Assert.Contains("exception is not RazerDeviceTimeoutException",
            Vm("RazerHidDeviceViewModel.Lighting.cs"));
        Assert.Contains("catch (RazerDeviceTimeoutException)",
            Vm("RazerHidKrakenViewModel.cs"));
    }

    [Fact]
    public void PerformanceModeTilesHaveNoHelpTooltips()
    {
        var xaml = App("Views/DevicesPage.xaml");
        Assert.DoesNotContain("AutomationProperties.HelpText=\"{Binding Description}\"", xaml);
        Assert.DoesNotContain("Blade.PerformanceModeHelpText", xaml);
        Assert.DoesNotContain("Blade.FanSummaryHelpText", xaml);
    }

    [Fact]
    public void BladeCardStaysAheadOfReadyPeripherals()
    {
        var source = Method(Vm("MainViewModel.Devices.cs"), "private IReadOnlyList<ConnectedDeviceRowViewModel> BuildConnectedDevices");
        var laptop = source.IndexOf("device.Category == DeviceCategory.Laptop ? 0", StringComparison.Ordinal);
        var ready = source.IndexOf("device.IsReady ? 1 : 2", StringComparison.Ordinal);
        Assert.True(laptop >= 0 && ready > laptop);
        Assert.Contains(".ThenBy(device => device.SortOrder)", source[laptop..]);
    }

    [Fact]
    public void BladeDiagnosticsListStartupAnimationOnlyWhereTheManifestDeclaresIt()
    {
        var source = Method(Vm("DeviceRowViewModel.cs"), "private static IReadOnlyList<DeviceProtocolDetailViewModel> CreateProtocolDetails");
        var guard = source.IndexOf("SupportsStartupAnimation(descriptor.ProductId)", StringComparison.Ordinal);
        var add = source.IndexOf("Add(\"Text_AA91A1FF\"", StringComparison.Ordinal);
        Assert.True(guard >= 0 && add > guard, "02B7 must not advertise an undeclared startup-animation row.");
    }

    [Fact]
    public void FirstPaintShowsBladeAsDiscoveredInsteadOfProtocolFailure()
    {
        var row = Vm("DeviceRowViewModel.cs");
        Assert.Contains("bool telemetryPending = false", row);
        Assert.Contains("_capabilityState = 4", row);
        var refresh = Vm("RefreshWorkspace.cs");
        Assert.Contains("var pending = telemetry is null;", refresh);
        Assert.Contains("new DeviceRowViewModel(device, telemetry, pending)", refresh);
        Assert.Contains("snapshot = snapshot with { Devices = ActiveDescriptors };", refresh);
    }

    [Fact]
    public void PeriodicReadoutsRebuildStaleDeviceRowsAfterAFailedFullRefresh()
    {
        var source = Method(Vm("RefreshWorkspace.cs"), "private async Task RefreshDeviceReadoutsAsync");
        var apply = source.IndexOf("_applyDeviceTelemetry(telemetry)", StringComparison.Ordinal);
        var recover = source.IndexOf("RefreshDeviceRowsIfStale(telemetry)", StringComparison.Ordinal);
        Assert.True(apply >= 0 && recover > apply);
        var policy = Method(Vm("RefreshWorkspace.cs"), "private void RefreshDeviceRowsIfStale");
        Assert.Contains("row.CapabilitySummary != summary", policy);
        Assert.Contains("PopulateDeviceRows(descriptors, telemetry)", policy);
    }

    [Fact]
    public void MainWindowShutdownAlwaysTerminatesTheProcess()
    {
        var source = Method(App("App.Lifecycle.cs"), "private async Task CloseApplicationAsync");
        var shutdown = source.IndexOf("await GetShutdownTask(viewModel)", StringComparison.Ordinal);
        var exit = source.IndexOf("Environment.Exit(0)", StringComparison.Ordinal);
        Assert.True(shutdown >= 0 && exit > shutdown);
        Assert.Contains(".WaitAsync(ExitCleanupTimeout + TimeSpan.FromSeconds(3))", source[shutdown..exit]);
        Assert.Contains("finally", source[shutdown..exit]);
        Assert.Contains("Interlocked.Exchange(ref _processExitRequested, 1)", source);
    }

    private static string Vm(string file) => App("ViewModels/" + file);
    private static string App(string file) => File.ReadAllText(Path.Combine(RepoLayout.Root, "src/OpenSynapse.App", file));

    private static string Method(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, "Missing method: " + signature);
        var opening = source.IndexOf('{', start);
        var expression = source.IndexOf("=>", start, StringComparison.Ordinal);
        if (expression >= 0 && (opening < 0 || expression < opening))
        {
            // A block body can contain a semicolon in a comment; an expression body ends
            // with a line whose trimmed text is the terminator.
            for (var lineStart = expression; lineStart < source.Length;)
            {
                var lineEnd = source.IndexOf('\n', lineStart);
                if (lineEnd < 0) lineEnd = source.Length;
                var line = source.AsSpan(lineStart, lineEnd - lineStart).TrimEnd();
                if (line.EndsWith(";", StringComparison.Ordinal))
                {
                    return source[start..(lineEnd + (lineEnd < source.Length ? 1 : 0))];
                }
                lineStart = lineEnd + 1;
            }
            throw new InvalidOperationException("Unbalanced expression method: " + signature);
        }
        var depth = 0;
        for (var index = opening; index < source.Length; index++)
        {
            depth += source[index] switch { '{' => 1, '}' => -1, _ => 0 };
            if (depth == 0) return source[opening..(index + 1)];
        }
        throw new InvalidOperationException("Unbalanced method: " + signature);
    }
}


