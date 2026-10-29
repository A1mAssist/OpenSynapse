using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Dispatching;
using OpenSynapse.App.Runtime;
using OpenSynapse.Core.Diagnostics;
using OpenSynapse.Core.Macros;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Core.Sensors;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

public sealed class OpenRazerWorkspace : INotifyPropertyChanged, IAsyncDisposable
{
    private readonly OpenRazerDeviceService? _deviceService;
    private readonly OpenRazerChromaFrameSink? _chromaFrameSink;
    private readonly OpenRazerSpecialLightingService? _specialLightingService;
    private readonly Func<PowerSourceKind> _currentPowerSource;
    private readonly Func<ProfileDocument> _getProfile;
    private readonly Func<ProfileDefinition> _getActiveProfile;
    private readonly Func<CancellationToken, Task<bool>> _saveProfileAsync;
    private readonly LocalDiagnosticLog _diagnosticLog;
    private readonly Func<bool> _chromaIntegrationEnabled;
    private readonly Func<IReadOnlyList<MacroDefinition>> _localMacros;
    private readonly Func<CancellationToken, Task> _refreshDevicesAsync;
    private readonly Action _refreshDeviceOverview;
    private readonly Action<string> _setDeviceOperationError;
    private readonly DispatcherQueue? _dispatcherQueue;
    private IReadOnlyList<OpenRazerSpecialLightingConnection> _krakenConnections = [];
    private CancellationTokenSource? _selectionCancellation;
    // A device whose mode was already repaired must not be rewritten on every device-tree
    // change; three HID round trips at the 31 ms device wait is not free.
    private readonly HashSet<string> _driverModeChecked = new(StringComparer.OrdinalIgnoreCase);
    private OpenRazerDeviceViewModel? _selectedDevice;
    private OpenRazerKrakenViewModel? _selectedKraken;

    public OpenRazerWorkspace(
        OpenRazerDeviceService? deviceService,
        OpenRazerSpecialLightingService? specialLightingService,
        Func<PowerSourceKind> currentPowerSource,
        Func<ProfileDocument> getProfile,
        Func<ProfileDefinition> getActiveProfile,
        Func<CancellationToken, Task<bool>> saveProfileAsync,
        LocalDiagnosticLog diagnosticLog,
        Func<bool> chromaIntegrationEnabled,
        Func<IReadOnlyList<MacroDefinition>> localMacros,
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
        _refreshDevicesAsync = refreshDevicesAsync;
        _refreshDeviceOverview = refreshDeviceOverview;
        _setDeviceOperationError = setDeviceOperationError;
        _dispatcherQueue = dispatcherQueue;
        if (_deviceService is not null)
        {
            _chromaFrameSink = new OpenRazerChromaFrameSink(
                _deviceService,
                () => CurrentConnections,
                IsLightingEnabled,
                IsChromaOverrideEnabled);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<OpenRazerDeviceRowViewModel> Devices { get; } = new();
    public ObservableCollection<OpenRazerKrakenDeviceRowViewModel> KrakenDevices { get; } = new();

    public OpenRazerDeviceViewModel? SelectedDevice
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

    public OpenRazerKrakenViewModel? SelectedKraken
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

    internal IReadOnlyList<OpenRazerDeviceConnection> CurrentConnections =>
        Devices.Select(row => row.Connection).ToArray();

    internal OpenRazerSpecialLightingService? SpecialLightingService => _specialLightingService;

    internal Task<bool> ApplyChromaFrameAsync(
        IReadOnlyList<RazerRgb> sourceFrame,
        CancellationToken cancellationToken = default) =>
        _chromaFrameSink?.ApplyAsync(sourceFrame, cancellationToken) ??
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
            await RestoreDriverModeAsync(connections, cancellationToken);
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
                    Devices.Add(new OpenRazerDeviceRowViewModel(connection));
                }
                var selectedRow = Devices.FirstOrDefault(row =>
                    StringComparer.OrdinalIgnoreCase.Equals(row.Connection.InstanceId, selectedInstanceId));
                if (selectedRow is null)
                {
                    if (SelectedDevice is { } removedDevice)
                    {
                        await removedDevice.DisposeAsync().ConfigureAwait(false);
                    }
                    SelectedDevice = null;
                }
                else
                {
                    await SelectDeviceAsync(selectedRow, cancellationToken);
                }
            }
            else if (SelectedDevice is { } selectedDevice)
            {
                // A device paused after a transport drop stays paused across rescans,
                // because every read path returns early while the flag is set. Clear it
                // so the rescan the user just asked for can actually recover.
                selectedDevice.ClearRequiresRescan();
                await selectedDevice.LoadBasicStateAsync(cancellationToken);
                await selectedDevice.LoadSoundbarAsync(cancellationToken);
            }
        }

        if (_specialLightingService is not null)
        {
            var connections = await _specialLightingService.DiscoverAsync(cancellationToken);
            _krakenConnections = connections.Where(connection =>
                    connection.Kind is OpenRazerSpecialLightingKind.Kraken37 or OpenRazerSpecialLightingKind.KrakenV4)
                .OrderBy(connection => connection.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
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
                    KrakenDevices.Add(new OpenRazerKrakenDeviceRowViewModel(connection));
                }
                var selectedKraken = KrakenDevices.FirstOrDefault(row =>
                    StringComparer.OrdinalIgnoreCase.Equals(row.Connection.InstanceId, selectedKrakenId));
                SelectedKraken = selectedKraken is null
                    ? null
                    : new OpenRazerKrakenViewModel(_specialLightingService, selectedKraken.Connection);
                if (SelectedKraken is { } kraken)
                {
                    await kraken.LoadSerialAsync(cancellationToken);
                }
            }
        }
    }

    public async Task SelectDeviceAsync(
        OpenRazerDeviceRowViewModel row,
        CancellationToken cancellationToken = default)
    {
        if (_deviceService is null)
        {
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
        var selected = CreateDeviceViewModel(row.Connection);
        row.AttachDetail(selected);
        SelectedKraken = null;
        SelectedDevice = selected;
        await selected.LoadBasicStateAsync(selectionToken);
        await selected.LoadSoundbarAsync(selectionToken);
        await selected.LoadDpiStagesAsync(selectionToken);
        await selected.ApplyConfiguredLightingAsync(selectionToken);
        NotifyDeviceOverview();
    }

    public async Task SelectKrakenAsync(OpenRazerKrakenDeviceRowViewModel row)
    {
        if (_specialLightingService is null) return;
        _selectionCancellation?.Cancel();
        _selectionCancellation?.Dispose();
        _selectionCancellation = null;
        if (SelectedDevice is { } previous)
        {
            await previous.DisposeAsync().ConfigureAwait(false);
        }
        SelectedDevice = null;
        SelectedKraken = new OpenRazerKrakenViewModel(
            _specialLightingService, row.Connection);
    }

    /// <summary>
    /// Scans the selected OpenRazer device, so a model the catalog does not describe
    /// can be mapped without touching another connected device.
    /// </summary>
    internal async Task<string> ScanSelectedDeviceAsync(CancellationToken cancellationToken = default)
    {
        if (_deviceService is null)
        {
            throw new InvalidOperationException("OpenRazer support is unavailable in this build.");
        }

        // Discovery is event driven, so the list can still be empty right after a wake.
        await _refreshDevicesAsync(cancellationToken).ConfigureAwait(false);
        var connection = SelectedDevice?.Connection;
        if (connection is null || !connection.IsReady)
        {
            throw new InvalidOperationException("No OpenRazer device is selected.");
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

        foreach (var connection in CurrentConnections)
        {
            if (!connection.IsReady)
            {
                continue;
            }

            await using var device = CreateDeviceViewModel(connection);
            await device.ApplyConfiguredLightingAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    internal OpenRazerKrakenViewModel? CreateKrakenLighting(ushort productId)
    {
        if (_specialLightingService is null) return null;
        return _krakenConnections.FirstOrDefault(item => item.ProductId == productId) is { } kraken
            ? new OpenRazerKrakenViewModel(_specialLightingService, kraken)
            : null;
    }

    internal async Task<int?> ReadBatteryPercentAsync(
        OpenRazerDeviceRowViewModel row,
        CancellationToken cancellationToken)
    {
        if (_deviceService is null) return null;
        try
        {
            return await _deviceService
                .GetBatteryPercentAsync(row.Connection, cancellationToken)
                .ConfigureAwait(false);
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
        if (SelectedDevice is { } device)
        {
            await device.DisposeAsync().ConfigureAwait(false);
            SelectedDevice = null;
        }
        SelectedKraken = null;
    }

    private async Task RestoreDriverModeAsync(
        IReadOnlyList<OpenRazerDeviceConnection> connections,
        CancellationToken cancellationToken)
    {
        if (_deviceService is null)
        {
            return;
        }

        foreach (var connection in connections)
        {
            if (!connection.IsReady ||
                !connection.Capabilities.Contains(OpenRazerBackendCapability.DeviceModeWrite) ||
                _driverModeChecked.Contains(connection.InstanceId))
            {
                continue;
            }

            try
            {
                await _deviceService.EnsureDriverModeAsync(connection, cancellationToken)
                    .ConfigureAwait(false);
                _driverModeChecked.Add(connection.InstanceId);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (
                exception is NotSupportedException or InvalidOperationException or
                System.ComponentModel.Win32Exception or System.IO.IOException)
            {
                _diagnosticLog.TryWrite("openrazer-driver-mode",
                    $"1532:{connection.Definition.ProductId:X4} restore failed: {exception.Message}");
                ReportOperationError(AppStrings.FormatText("OpenRazerDriverModeRestoreFailed",
                    connection.Definition.DisplayName, exception.Message));
            }
        }
    }

    private OpenRazerDeviceViewModel CreateDeviceViewModel(
        OpenRazerDeviceConnection connection)
    {
        OpenRazerDeviceViewModel device = new(
            _deviceService ?? throw new InvalidOperationException("OpenRazer service is unavailable."),
            connection,
            _currentPowerSource,
            powerState => ProfileResolver.ResolveOpenRazerLighting(
                _getProfile(),
                connection.ToDescriptor(),
                powerState),
            (powerState, profile, token) => SaveLightingAsync(
                connection,
                powerState,
                profile,
                token),
            () => IsLightingEnabled(connection),
            () => IsChromaOverrideEnabled(connection),
            (lightingEnabled, chromaOverrideEnabled, token) => SaveLightingSettingsAsync(
                connection,
                lightingEnabled,
                chromaOverrideEnabled,
                token),
            _chromaIntegrationEnabled());
        device.Onboard.LocalMacroProvider = _localMacros;
        return device;
    }

    private async Task<bool> SaveLightingAsync(
        OpenRazerDeviceConnection connection,
        PowerSourceKind powerState,
        LightingProfile profile,
        CancellationToken cancellationToken)
    {
        var active = _getActiveProfile();
        var key = ProfileResolver.GetDeviceKey(connection.ToDescriptor());
        Dictionary<string, LightingProfile>? target = powerState switch
        {
            PowerSourceKind.FullPowerAdapter => active.PluggedIn.OpenRazerLighting,
            PowerSourceKind.LowPowerAdapter => active.LowPowerAdapter.OpenRazerLighting,
            PowerSourceKind.Battery => active.OnBattery.OpenRazerLighting,
            _ => null,
        };

        LightingProfile? previous = null;
        var hadPrevious = false;
        if (target is null)
        {
            if (!active.Devices.TryGetValue(key, out var settings))
            {
                settings = new DeviceProfileSettings();
                active.Devices[key] = settings;
            }

            previous = CloneLightingProfile(settings.Lighting);
            settings.Lighting = CloneLightingProfile(profile);
        }
        else
        {
            hadPrevious = target.TryGetValue(key, out previous);
            target[key] = CloneLightingProfile(profile);
        }

        if (await _saveProfileAsync(cancellationToken))
        {
            return true;
        }

        if (target is null)
        {
            active.Devices[key].Lighting = previous ?? new LightingProfile();
        }
        else if (hadPrevious && previous is not null)
        {
            target[key] = previous;
        }
        else
        {
            target.Remove(key);
        }

        return false;
    }

    internal bool IsLightingEnabled(OpenRazerDeviceConnection connection) =>
        GetDeviceSettings(connection).LightingEnabled ?? true;

    internal bool IsChromaOverrideEnabled(OpenRazerDeviceConnection connection) =>
        GetDeviceSettings(connection).ChromaOverrideEnabled ?? true;

    private async Task<bool> SaveLightingSettingsAsync(
        OpenRazerDeviceConnection connection,
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

    private DeviceProfileSettings GetDeviceSettings(OpenRazerDeviceConnection connection)
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
        OpenRazerDeviceConnection previous,
        OpenRazerDeviceConnection current) =>
        StringComparer.OrdinalIgnoreCase.Equals(previous.InstanceId, current.InstanceId) &&
        StringComparer.OrdinalIgnoreCase.Equals(previous.DevicePath, current.DevicePath) &&
        StringComparer.Ordinal.Equals(previous.Definition.DisplayName, current.Definition.DisplayName) &&
        previous.EndpointState == current.EndpointState &&
        StringComparer.Ordinal.Equals(previous.Error, current.Error);

    private static bool KrakenConnectionMatches(
        OpenRazerSpecialLightingConnection previous,
        OpenRazerSpecialLightingConnection current) =>
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
