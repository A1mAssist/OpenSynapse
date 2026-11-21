using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Dispatching;
using OpenSynapse.App.Runtime;
using OpenSynapse.Core.Diagnostics;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Macros;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Core.Sensors;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHidWorkspace : INotifyPropertyChanged, IAsyncDisposable
{
    /// <summary>Set while the Lighting Studio output pump owns the devices.</summary>
    internal Func<bool>? StudioLightingActive { get; set; }

    private readonly RazerHidDeviceService? _deviceService;
    private readonly RazerHidChromaFrameSink? _chromaFrameSink;
    private readonly RazerHidSpecialLightingService? _specialLightingService;
    private readonly Func<PowerSourceKind> _currentPowerSource;
    private readonly Func<ProfileDocument> _getProfile;
    private readonly Func<ProfileDefinition> _getActiveProfile;
    private readonly Func<CancellationToken, Task<bool>> _saveProfileAsync;
    private readonly LocalDiagnosticLog _diagnosticLog;
    private readonly Func<bool> _chromaIntegrationEnabled;
    private readonly Func<IReadOnlyList<MacroDefinition>> _localMacros;
    private readonly Func<string, MacroInput, MacroTriggerMode, MacroPlaybackMode, int, bool,
        MacroSoftwareBinding?, CancellationToken, Task<string?>>? _softwareMacroBinder;
    private readonly Func<string, MacroInput, bool, CancellationToken, Task<string?>>? _softwareMacroClearer;
    private readonly Func<MachineSettings> _getMachineSettings;
    private readonly Func<CancellationToken, Task<bool>> _saveMachineSettingsAsync;
    private readonly Func<CancellationToken, Task> _refreshDevicesAsync;
    private readonly Action _refreshDeviceOverview;
    private readonly Action<string> _setDeviceOperationError;
    private readonly DispatcherQueue? _dispatcherQueue;
    // Device rows are rebuilt on the UI thread, but private-key triggers and the
    // lighting pump read connections from their own threads.
    private volatile RazerHidDeviceConnection[] _connectionSnapshot = [];
    private IReadOnlyList<RazerHidSpecialLightingConnection> _krakenConnections = [];
    private CancellationTokenSource? _selectionCancellation;
    // Software frame-pump effects outlive a single apply call, so their view model must
    // stay alive instead of being disposed right after the profile is restored.
    private readonly RazerHidSoftwareLightingOwner _softwareLightingOwner;
    private readonly StudioDeviceVisualLoader _matrixLayoutLoader = new();
    private readonly RazerMousePrivateKeyRuntime _privateKeyRuntime;
    private RazerHidDeviceViewModel? _selectedDevice;
    private RazerHidKrakenViewModel? _selectedKraken;

    public RazerHidWorkspace(
        RazerHidDeviceService? deviceService,
        RazerHidSpecialLightingService? specialLightingService,
        Func<PowerSourceKind> currentPowerSource,
        Func<ProfileDocument> getProfile,
        Func<ProfileDefinition> getActiveProfile,
        Func<CancellationToken, Task<bool>> saveProfileAsync,
        LocalDiagnosticLog diagnosticLog,
        Func<bool> chromaIntegrationEnabled,
        Func<IReadOnlyList<MacroDefinition>> localMacros,
        Func<string, MacroInput, MacroTriggerMode, MacroPlaybackMode, int, bool,
            MacroSoftwareBinding?, CancellationToken, Task<string?>>? softwareMacroBinder,
        Func<string, MacroInput, bool, CancellationToken, Task<string?>>? softwareMacroClearer,
        Func<MachineSettings> getMachineSettings,
        Func<CancellationToken, Task<bool>> saveMachineSettingsAsync,
        Func<CancellationToken, Task> refreshDevicesAsync,
        Action refreshDeviceOverview,
        Action<string> setDeviceOperationError,
        DispatcherQueue? dispatcherQueue)
    {
        _deviceService = deviceService;
        _specialLightingService = specialLightingService;
        _currentPowerSource = currentPowerSource;
        _getProfile = getProfile;
        _getActiveProfile = getActiveProfile;
        _saveProfileAsync = saveProfileAsync;
        _diagnosticLog = diagnosticLog;
        _chromaIntegrationEnabled = chromaIntegrationEnabled;
        _localMacros = localMacros;
        _softwareMacroBinder = softwareMacroBinder;
        _softwareMacroClearer = softwareMacroClearer;
        _getMachineSettings = getMachineSettings;
        _saveMachineSettingsAsync = saveMachineSettingsAsync;
        _refreshDevicesAsync = refreshDevicesAsync;
        _refreshDeviceOverview = refreshDeviceOverview;
        _setDeviceOperationError = setDeviceOperationError;
        _dispatcherQueue = dispatcherQueue;
        _softwareLightingOwner = new RazerHidSoftwareLightingOwner(CreateDeviceViewModel);
        _privateKeyRuntime = new RazerMousePrivateKeyRuntime(
            message => diagnosticLog.TryWrite("mouse-private-key", message));
        if (_deviceService is not null)
        {
            _chromaFrameSink = new RazerHidChromaFrameSink(
                _deviceService,
                () => CurrentConnections,
                IsLightingEnabled,
                IsChromaOverrideEnabled);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<RazerHidDeviceRowViewModel> Devices { get; } = new();
    public ObservableCollection<RazerHidKrakenDeviceRowViewModel> KrakenDevices { get; } = new();

    public RazerHidDeviceViewModel? SelectedDevice
    {
        get => _selectedDevice;
        private set
        {
            if (SetField(ref _selectedDevice, value))
            {
                OnPropertyChanged(nameof(CanScanHidCommands));
            }
        }
    }

    public bool CanScanHidCommands => SelectedDevice is { IsReady: true };

    public RazerHidKrakenViewModel? SelectedKraken
    {
        get => _selectedKraken;
        private set => SetField(ref _selectedKraken, value);
    }

    internal int ErrorCount
    {
        get
        {
            var count = Devices.Count(row => row.HasError);
            if (SelectedDevice is { HasError: true } selected &&
                !Devices.Any(row => StringComparer.OrdinalIgnoreCase.Equals(
                    row.Connection.InstanceId, selected.InstanceId) && row.HasError))
            {
                count++;
            }

            count += KrakenDevices.Count(row => !string.IsNullOrWhiteSpace(row.Error));
            if (SelectedKraken?.HasError == true &&
                !KrakenDevices.Any(row => StringComparer.OrdinalIgnoreCase.Equals(
                    row.Connection.InstanceId, SelectedKraken.InstanceId) &&
                    !string.IsNullOrWhiteSpace(row.Error)))
            {
                count++;
            }

            return count;
        }
    }

    internal RazerHidDeviceService? DeviceService => _deviceService;
    internal IReadOnlyList<RazerHidSpecialLightingConnection> KrakenConnections => _krakenConnections;

    internal IReadOnlyList<RazerHidDeviceConnection> CurrentConnections =>
        _connectionSnapshot;
    internal RazerHidDeviceConnection? ResolveConnection(DeviceDescriptor descriptor) =>
        CurrentConnections.FirstOrDefault(connection =>
            connection.Definition.ProductId == descriptor.ProductId &&
            connection.Definition.Category == descriptor.Category &&
            (descriptor.Id.Contains("/" + Uri.EscapeDataString(connection.InstanceId), StringComparison.OrdinalIgnoreCase) || StringComparer.OrdinalIgnoreCase.Equals(connection.DevicePath, descriptor.Id)));

    internal Task<bool> ApplyChromaFrameAsync(
        IReadOnlyList<RazerRgb> sourceFrame,
        CancellationToken cancellationToken = default) =>
        _chromaFrameSink?.ApplyAsync(sourceFrame, cancellationToken) ??
        Task.FromResult(false);

    internal Task<bool> ApplyChromaFrameAsync(
        IReadOnlyList<RazerRgb> sourceFrame,
        int sourceRows,
        int sourceColumns,
        DeviceCategory category,
        CancellationToken cancellationToken = default) =>
        _chromaFrameSink?.ApplyAsync(sourceFrame, sourceRows, sourceColumns, category, cancellationToken) ??
        Task.FromResult(false);

    internal void RefreshLocalization()
    {
        foreach (var row in Devices)
        {
            row.RefreshLocalization();
        }
        foreach (var row in KrakenDevices)
        {
            row.RefreshLocalization();
        }
        SelectedDevice?.RefreshLocalization();
        SelectedKraken?.RefreshLocalization();
    }

    internal async Task RefreshAsync(CancellationToken cancellationToken)
    {
        if (_deviceService is not null)
        {
            var connections = (await _deviceService.DiscoverAsync(cancellationToken))
                .OrderBy(connection => DeviceUiCatalog.DeviceCategoryOrder(connection.Definition.Category))
                .ThenBy(connection => connection.Definition.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            connections = RazerHidDeviceConnectionSiblings.Collapse(connections);
            _connectionSnapshot = connections;
            await MigrateProfileInstanceKeysAsync(connections, cancellationToken).ConfigureAwait(true);
            await RestoreDriverModeAsync(connections, cancellationToken);
            await RestoreAnalogJoystickModesAsync(connections, cancellationToken);
            await SyncPrivateKeyDriverModeAsync(cancellationToken);
            await RestoreInitialLightingAsync(connections, cancellationToken).ConfigureAwait(true);
            // Rebuild bound collections and keep the refresh loop's UI context.
            await _softwareLightingOwner.PruneAsync(connections, cancellationToken).ConfigureAwait(true);
            if (Devices.Count != connections.Length ||
                !Devices.Zip(connections)
                    .All(pair => ConnectionMatches(pair.First.Connection, pair.Second)))
            {
                var selectedInstanceId = SelectedDevice?.InstanceId;
                _selectionCancellation?.Cancel();
                _selectionCancellation?.Dispose();
                _selectionCancellation = null;
                Devices.Clear();
                foreach (var connection in connections)
                {
                    Devices.Add(new RazerHidDeviceRowViewModel(connection));
                }
                var selectedRow = Devices.FirstOrDefault(row =>
                    StringComparer.OrdinalIgnoreCase.Equals(row.Connection.InstanceId, selectedInstanceId));
                if (selectedRow is null)
                {
                    if (SelectedDevice is { } removedDevice)
                    {
                        await removedDevice.DisposeAsync().ConfigureAwait(true);
                    }
                    SelectedDevice = null;
                }
                else
                {
                    await SelectDeviceAsync(selectedRow, cancellationToken, forceReload: true);
                }
            }
            else if (SelectedDevice is { } selectedDevice)
            {
                // A device paused after a transport drop stays paused across rescans,
                // because every read path returns early while the flag is set. Clear it
                // so the rescan the user just asked for can actually recover.
                selectedDevice.ClearRequiresRescan();
                await selectedDevice.LoadBasicStateAsync(cancellationToken);
                await selectedDevice.LoadSlowCardsAsync(cancellationToken: cancellationToken);
                await selectedDevice.LoadSoundbarAsync(cancellationToken);
            }
        }

        if (_specialLightingService is not null)
        {
            var connections = await _specialLightingService.DiscoverAsync(cancellationToken);
            CacheArgb(connections);
            var previousKrakenConnections = _krakenConnections;
            _krakenConnections = connections.Where(connection =>
                    connection.Kind is RazerHidSpecialLightingKind.Kraken37 or
                        RazerHidSpecialLightingKind.KrakenV4 or
                        RazerHidSpecialLightingKind.KrystalT2 or
                        RazerHidSpecialLightingKind.Protocol40Chroma)
                .OrderBy(connection => connection.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            foreach (var removed in previousKrakenConnections.Where(previous =>
                !_krakenConnections.Any(current =>
                    StringComparer.OrdinalIgnoreCase.Equals(current.InstanceId, previous.InstanceId))))
            {
                await _specialLightingService.ResetSessionAsync(
                    removed.DevicePath, cancellationToken);
            }
            // Headsets that already own an audio page get the lighting card there, so a
            // second lighting-only row for the same headset would be a duplicate.
            var krakenConnections = _krakenConnections
                .Where(connection => !connection.IsMergedIntoAudioPage)
                .ToArray();
            if (KrakenDevices.Count != krakenConnections.Length ||
                !KrakenDevices.Zip(krakenConnections)
                    .All(pair => KrakenConnectionMatches(pair.First.Connection, pair.Second)))
            {
                var selectedKrakenId = SelectedKraken?.InstanceId;
                KrakenDevices.Clear();
                foreach (var connection in krakenConnections)
                {
                    KrakenDevices.Add(new RazerHidKrakenDeviceRowViewModel(connection));
                }
                var selectedKraken = KrakenDevices.FirstOrDefault(row =>
                    StringComparer.OrdinalIgnoreCase.Equals(row.Connection.InstanceId, selectedKrakenId));
                SelectedKraken = selectedKraken is null
                    ? null
                    : new RazerHidKrakenViewModel(
                        _specialLightingService,
                        selectedKraken.Connection,
                        _getProfile,
                        _saveProfileAsync);
                if (SelectedKraken is { } kraken)
                {
                    await kraken.LoadSerialAsync(cancellationToken);
                }
            }
        }

        NotifyDeviceOverview();
    }

    public async Task SelectDeviceAsync(
        RazerHidDeviceRowViewModel row,
        CancellationToken cancellationToken = default,
        bool forceReload = false)
    {
        if (_deviceService is null)
        {
            return;
        }

        // Tab switches re-enter this method with the same row. Reuse the live view
        // model instead of disposing it and reading every hardware value again.
        if (!forceReload &&
            SelectedDevice is { } selectedDevice &&
            StringComparer.OrdinalIgnoreCase.Equals(
                selectedDevice.InstanceId, row.Connection.InstanceId) &&
            StringComparer.OrdinalIgnoreCase.Equals(
                selectedDevice.Connection.DevicePath, row.Connection.DevicePath))
        {
            row.AttachDetail(selectedDevice);
            SelectedKraken = null;
            NotifyDeviceOverview();
            return;
        }

        _selectionCancellation?.Cancel();
        _selectionCancellation?.Dispose();
        _selectionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var selectionToken = _selectionCancellation.Token;
        var previous = SelectedDevice;
        if (previous is not null)
        {
            await previous.DisposeAsync();
        }
        var selected = _softwareLightingOwner.Take(row.Connection.InstanceId) ??
            CreateDeviceViewModel(row.Connection);
        row.AttachDetail(selected);
        SelectedKraken = null;
        SelectedDevice = selected;
        try
        {
            await selected.LoadBasicStateAsync(selectionToken);
            await selected.LoadSlowCardsAsync(force: true, selectionToken);
            await selected.LoadSoundbarAsync(selectionToken);
            await selected.LoadDpiStagesAsync(selectionToken);
            await selected.ApplyConfiguredLightingAsync(selectionToken);
            NotifyDeviceOverview();
        }
        catch (OperationCanceledException) when (selectionToken.IsCancellationRequested)
        {
            // A newer device click or a page navigation superseded this selection.
            // Its replacement owns the selected view model now.
        }
    }

    public async Task SelectKrakenAsync(RazerHidKrakenDeviceRowViewModel row)
    {
        if (_specialLightingService is null) return;
        _selectionCancellation?.Cancel();
        _selectionCancellation?.Dispose();
        _selectionCancellation = null;
        if (SelectedDevice is { } previous)
        {
            await previous.DisposeAsync().ConfigureAwait(true);
        }
        SelectedDevice = null;
        SelectedKraken = new RazerHidKrakenViewModel(
            _specialLightingService,
            row.Connection,
            _getProfile,
            _saveProfileAsync);
        await SelectedKraken.LoadSerialAsync();
    }

    [NotProfileScoped]
    internal async Task ApplyKrakenProfilesAsync(
        PowerSourceKind powerSource,
        CancellationToken cancellationToken)
    {
        if (_specialLightingService is null)
        {
            return;
        }

        foreach (var row in KrakenDevices.ToArray())
        {
            try
            {
                var viewModel = new RazerHidKrakenViewModel(
                    _specialLightingService, row.Connection, _getProfile, _saveProfileAsync);
                await viewModel.LoadSerialAsync(cancellationToken, powerSource);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _diagnosticLog.TryWrite("kraken", $"profile replay failed: {exception.Message}");
            }
        }
    }

    /// <summary>
    /// Scans the selected RazerHid device, so a model the catalog does not describe
    /// can be mapped without touching another connected device.
    /// </summary>
    internal async Task<string> ScanSelectedDeviceAsync(CancellationToken cancellationToken = default)
    {
        if (_deviceService is null)
        {
            throw new InvalidOperationException("RazerHid support is unavailable in this build.");
        }

        // Discovery is event driven, so the list can still be empty right after a wake.
        await _refreshDevicesAsync(cancellationToken).ConfigureAwait(false);
        var connection = SelectedDevice?.Connection;
        if (connection is null || !connection.IsReady)
        {
            throw new InvalidOperationException("No RazerHid device is selected.");
        }

        var scan = await _deviceService
            .ScanCommandSpaceAsync(connection, cancellationToken).ConfigureAwait(false);
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "OpenSynapse");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(
            directory,
            $"hid-scan-{connection.Definition.ProductId:X4}-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
        await File.WriteAllTextAsync(path, scan, cancellationToken).ConfigureAwait(false);
        return path;
    }

    internal async Task RestoreLightingAfterExternalAsync(
        CancellationToken cancellationToken = default)
    {
        if (_deviceService is null)
        {
            return;
        }

        await _softwareLightingOwner.RestoreAsync(
            CurrentConnections, SelectedDevice, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>A Chroma session ended; hand back the keyboards it left on custom frames.</summary>
    internal Task RestoreChromaFrameSinkAsync(CancellationToken cancellationToken = default) =>
        _chromaFrameSink?.RestoreAsync(cancellationToken) ?? Task.CompletedTask;

    /// <summary>
    /// Session ending runs on the UI thread, so the HID writes happen on a worker
    /// with a bounded wait. A device that does not answer must not delay shutdown.
    /// </summary>
    internal void TurnOffLightingForShutdown(TimeSpan timeout)
    {
        if (_deviceService is null)
        {
            return;
        }

        var connections = CurrentConnections;
        var selected = SelectedDevice;
        var task = Task.Run(() => TurnOffLightingForShutdownAsync(
            connections, selected, CancellationToken.None));
        try
        {
            if (!task.Wait(timeout))
            {
                _diagnosticLog.TryWrite(
                    "lighting-shutdown",
                    $"timed out after {timeout.TotalSeconds:0.#}s.");
            }
        }
        catch (AggregateException exception)
        {
            _diagnosticLog.TryWrite(
                "lighting-shutdown",
                $"failed: {exception.GetBaseException().Message}");
        }
    }

    private async Task TurnOffLightingForShutdownAsync(
        IReadOnlyList<RazerHidDeviceConnection> connections,
        RazerHidDeviceViewModel? selected,
        CancellationToken cancellationToken)
    {
        try
        {
            await _softwareLightingOwner.StopAllAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            _diagnosticLog.TryWrite(
                "lighting-shutdown",
                $"software runtime stop failed: {exception.Message}");
        }

        if (selected is not null)
        {
            try
            {
                await selected.StopLightingRuntimeAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                _diagnosticLog.TryWrite(
                    "lighting-shutdown",
                    $"selected runtime stop failed: {exception.Message}");
            }
        }

        var service = _deviceService;
        if (service is null)
        {
            return;
        }

        var turnedOff = 0;
        foreach (var connection in connections.Where(connection =>
                     connection.IsReady &&
                     connection.Definition.Category != DeviceCategory.Laptop))
        {
            foreach (var (zone, capabilities) in connection.LightingZones)
            {
                try
                {
                    if (capabilities.CanWriteState)
                    {
                        await service.SetLedStateAsync(
                            connection,
                            connection.Definition.DefaultStorage,
                            zone,
                            false,
                            cancellationToken).ConfigureAwait(false);
                        turnedOff++;
                    }
                    else if (capabilities.LightingEffects.Contains(RazerHidLightingEffect.Off))
                    {
                        await service.SetLightingAsync(
                            connection,
                            new RazerHidLightingSettings(RazerHidLightingEffect.Off, Zone: zone),
                            cancellationToken).ConfigureAwait(false);
                        turnedOff++;
                    }
                }
                catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
                {
                    _diagnosticLog.TryWrite(
                        "lighting-shutdown",
                        $"1532:{connection.Definition.ProductId:X4} {zone}: {exception.Message}");
                }
            }
        }

        _diagnosticLog.TryWrite(
            "lighting-shutdown",
            $"off requests sent for {turnedOff} zone(s) across " +
            $"{connections.Count(connection => connection.IsReady)} ready device(s).");
    }

    internal RazerHidKrakenViewModel? CreateKrakenLighting(ushort productId)
    {
        if (_specialLightingService is null) return null;
        return _krakenConnections.FirstOrDefault(item => item.ProductId == productId) is { } kraken
            ? new RazerHidKrakenViewModel(
                _specialLightingService,
                kraken,
                _getProfile,
                _saveProfileAsync)
            : null;
    }

    internal RazerHidSpecialLightingConnection? FindKrakenConnection(ushort productId) =>
        _krakenConnections.FirstOrDefault(item => item.ProductId == productId);

    internal Task ResetSpecialLightingSessionAsync(
        string? devicePath,
        CancellationToken cancellationToken = default) =>
        _specialLightingService?.ResetSessionAsync(devicePath, cancellationToken) ??
        Task.CompletedTask;

    internal async Task<int?> ReadBatteryPercentAsync(
        RazerHidDeviceRowViewModel row,
        CancellationToken cancellationToken)
    {
        if (_deviceService is null) return null;
        try
        {
            var percent = await _deviceService
                .GetBatteryPercentAsync(row.Connection, cancellationToken)
                .ConfigureAwait(false);
            row.SetBatteryPercent(percent);
            if (row.Connection.Capabilities.Contains(RazerHidBackendCapability.ChargingRead))
            {
                try
                {
                    row.SetCharging(await _deviceService
                        .GetChargingAsync(row.Connection, cancellationToken)
                        .ConfigureAwait(false));
                }
                catch (Exception) when (!cancellationToken.IsCancellationRequested)
                {
                    // The charging query is optional; keep the last known state.
                }
            }
            return percent;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // No answer means no percentage for this row; the menu still lists it.
            return null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        _selectionCancellation?.Cancel();
        _selectionCancellation?.Dispose();
        await _privateKeyRuntime.ResetAsync().ConfigureAwait(false);
        await _softwareLightingOwner.DisposeAsync().ConfigureAwait(false);
        if (SelectedDevice is { AnalogJoystickActive: true } joystickDevice)
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await joystickDevice.DisableAnalogJoystickOnExitAsync(timeout.Token)
                    .ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                _diagnosticLog.TryWrite("analog-joystick",
                    $"shutdown restore failed: {exception.GetType().Name}: {exception.Message}");
            }
        }
        if (SelectedDevice is { } device)
        {
            await device.DisposeAsync().ConfigureAwait(false);
            SelectedDevice = null;
        }
        SelectedKraken = null;
        _matrixLayoutLoader.Dispose();
    }

    /// <summary>
    /// Replays the firmware default for a private key that no macro claimed. Only
    /// the DPI keys need it: in driver mode the device stops stepping the stages.
    /// </summary>
    internal async Task HandleUnhandledRazerKeyAsync(MacroInput input)
    {
        if (input.Kind != MacroInputKind.RazerKey ||
            input.DeviceId is not (> 0 and <= 0xFFFF) ||
            input.RazerKeyCode is not (32 or 33 or 82))
        {
            return;
        }

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await _privateKeyRuntime
                .HandleAsync((ushort)input.DeviceId, (byte)input.RazerKeyCode, timeout.Token)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is OperationCanceledException or Win32Exception or IOException)
        {
            _diagnosticLog.TryWrite(
                "mouse-private-key",
                $"default action for key 0x{input.RazerKeyCode:X2} failed: {exception.Message}");
        }
    }

    private async Task RestoreDriverModeAsync(
        IReadOnlyList<RazerHidDeviceConnection> connections,
        CancellationToken cancellationToken)
    {
        if (_deviceService is null)
        {
            return;
        }

        PruneDriverModeChecked(connections);

        foreach (var connection in connections)
        {
            if (!connection.IsReady ||
                !connection.Capabilities.Contains(RazerHidBackendCapability.DeviceModeWrite) ||
                _driverModeChecked.ContainsKey(connection.InstanceId))
            {
                continue;
            }

            try
            {
                await _deviceService.EnsureDriverModeAsync(connection, cancellationToken)
                    .ConfigureAwait(false);
                _driverModeChecked.TryAdd(connection.InstanceId, 0);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (
                exception is NotSupportedException or InvalidOperationException or
                System.ComponentModel.Win32Exception or System.IO.IOException)
            {
                _diagnosticLog.TryWrite("razer-driver-mode",
                    $"1532:{connection.Definition.ProductId:X4} restore failed: {exception.Message}");
                ReportOperationError(AppStrings.FormatText("RazerHidDriverModeRestoreFailed",
                    connection.Definition.DisplayName, exception.Message));
            }
        }
    }

    internal bool IsLightingEnabled(RazerHidDeviceConnection connection) =>
        GetDeviceSettings(connection).LightingEnabled ?? true;

    internal bool IsChromaOverrideEnabled(RazerHidDeviceConnection connection) =>
        GetDeviceSettings(connection).ChromaOverrideEnabled ?? true;

    private async Task<bool> SaveLightingSettingsAsync(
        RazerHidDeviceConnection connection,
        bool lightingEnabled,
        bool chromaOverrideEnabled,
        CancellationToken cancellationToken)
    {
        var active = _getActiveProfile();
        var key = ProfileResolver.GetDeviceKey(connection.ToDescriptor());
        if (!active.Devices.TryGetValue(key, out var settings))
        {
            settings = new DeviceProfileSettings();
            active.Devices[key] = settings;
        }

        var previousLightingEnabled = settings.LightingEnabled;
        var previousChromaOverrideEnabled = settings.ChromaOverrideEnabled;
        settings.LightingEnabled = lightingEnabled;
        settings.ChromaOverrideEnabled = chromaOverrideEnabled;
        if (await _saveProfileAsync(cancellationToken))
        {
            return true;
        }

        settings.LightingEnabled = previousLightingEnabled;
        settings.ChromaOverrideEnabled = previousChromaOverrideEnabled;
        return false;
    }

    private DeviceProfileSettings GetDeviceSettings(RazerHidDeviceConnection connection)
    {
        var key = ProfileResolver.GetDeviceKey(connection.ToDescriptor());
        return _getActiveProfile().Devices.TryGetValue(key, out var settings)
            ? settings
            : new DeviceProfileSettings();
    }

    private static LightingProfile CloneLightingProfile(LightingProfile profile) => new()
    {
        Effect = profile.Effect,
        Parameters = new Dictionary<string, string>(profile.Parameters, StringComparer.OrdinalIgnoreCase),
    };

    private static bool ConnectionMatches(
        RazerHidDeviceConnection previous,
        RazerHidDeviceConnection current) =>
        StringComparer.OrdinalIgnoreCase.Equals(previous.InstanceId, current.InstanceId) &&
        StringComparer.OrdinalIgnoreCase.Equals(previous.DevicePath, current.DevicePath) &&
        StringComparer.Ordinal.Equals(previous.Definition.DisplayName, current.Definition.DisplayName) &&
        previous.EndpointState == current.EndpointState &&
        previous.Capabilities.SetEquals(current.Capabilities) &&
        StringComparer.Ordinal.Equals(previous.Error, current.Error);

    private static bool KrakenConnectionMatches(
        RazerHidSpecialLightingConnection previous,
        RazerHidSpecialLightingConnection current) =>
        StringComparer.OrdinalIgnoreCase.Equals(previous.InstanceId, current.InstanceId) &&
        StringComparer.OrdinalIgnoreCase.Equals(previous.DevicePath, current.DevicePath) &&
        StringComparer.Ordinal.Equals(previous.Error, current.Error);

    private void NotifyDeviceOverview()
    {
        if (_dispatcherQueue is { HasThreadAccess: false } dispatcher)
        {
            dispatcher.TryEnqueue(() => _refreshDeviceOverview());
            return;
        }

        _refreshDeviceOverview();
    }

    private void ReportOperationError(string message)
    {
        if (_dispatcherQueue is { HasThreadAccess: false } dispatcher)
        {
            dispatcher.TryEnqueue(() => _setDeviceOperationError(message));
            return;
        }

        _setDeviceOperationError(message);
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        if (_dispatcherQueue is { HasThreadAccess: false } dispatcher)
        {
            dispatcher.TryEnqueue(() => PropertyChanged?.Invoke(
                this, new PropertyChangedEventArgs(propertyName)));
            return;
        }

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
