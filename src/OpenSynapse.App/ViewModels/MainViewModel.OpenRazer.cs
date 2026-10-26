using OpenSynapse.Core.Profiles;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Sensors;
using Microsoft.UI.Xaml;
using OpenSynapse.Windows.Devices;

namespace OpenSynapse.App.ViewModels;

public sealed partial class MainViewModel
{
    private IReadOnlyList<ConnectedDeviceRowViewModel>? _connectedDevicesCache;

    public IReadOnlyList<ConnectedDeviceRowViewModel> ConnectedDevices =>
        _connectedDevicesCache ??= BuildConnectedDevices();

    private IReadOnlyList<ConnectedDeviceRowViewModel> BuildConnectedDevices() =>
        Devices.Select(device => new ConnectedDeviceRowViewModel(device))
            .Concat(OpenRazerDevices.Select(device => new ConnectedDeviceRowViewModel(device)))
            .Concat(OpenRazerKrakenDevices.Select(device => new ConnectedDeviceRowViewModel(device)))
            .Concat(BlackSharkDevices.Select(device => new ConnectedDeviceRowViewModel(device)))
            .Concat(HeadsetAudioDevices.Select(device => new ConnectedDeviceRowViewModel(device)))
            .Concat(KeyboardDevices.Select(device => new ConnectedDeviceRowViewModel(device)))
            // The Viper owns a native page and still answers Chroma discovery; its lighting page
            // opens from there, so list the device once.
            .Concat(ChromaDevices
                .Where(chroma => !Devices.Any(native => native.ProductId == chroma.Connection.ProductId))
                .Select(device => new ConnectedDeviceRowViewModel(device)))
            .Concat(BleDevices.Select(device => new ConnectedDeviceRowViewModel(device)))
            .Concat([new ConnectedDeviceRowViewModel(Hue)])
            .Where(device => device.IsVisible)
            .OrderByDescending(device => device.IsReady)
            .ThenBy(device => device.SortOrder)
            .ThenBy(device => device.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public IReadOnlyList<ConnectedDeviceRowViewModel> PrimaryDevices => ConnectedDevices.Take(2).ToArray();

    public IReadOnlyList<ConnectedDeviceRowViewModel> AdditionalDevices => ConnectedDevices.Skip(2).ToArray();

    private static int DeviceCategoryOrder(DeviceCategory category) => category switch
    {
        DeviceCategory.Laptop => 0,
        DeviceCategory.Keyboard => 1,
        DeviceCategory.Mouse => 2,
        _ => 3,
    };

    public Visibility AdditionalDevicesVisibility =>
        AdditionalDevices.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;

    private void RefreshDeviceOverviewVisibility()
    {
        _connectedDevicesCache = null;
        OnPropertyChanged(nameof(PrimaryDevices));
        OnPropertyChanged(nameof(AdditionalDevices));
        OnPropertyChanged(nameof(ConnectedDevices));
        OnPropertyChanged(nameof(BladeProtocolText));
        OnPropertyChanged(nameof(BladeIdentityText));
        OnPropertyChanged(nameof(ViperProtocolText));
        OnPropertyChanged(nameof(ViperIdentityText));
        OnPropertyChanged(nameof(AdditionalDevicesVisibility));
    }

    private void RefreshDeviceSelectorItems()
    {
        DeviceSelectorItems.Clear();
        if (_deviceDescriptors.Any(device => DeviceProtocolFamilies.IsBlade(device.ProtocolFamily)))
        {
            DeviceSelectorItems.Add(new DeviceSelectorItemViewModel(
                DeviceSelectorItemKind.Blade,
                BladeDeviceName,
                "\uE7F8"));
        }
        if (ViperDeviceVisibility == Microsoft.UI.Xaml.Visibility.Visible)
        {
            DeviceSelectorItems.Add(new DeviceSelectorItemViewModel(
                DeviceSelectorItemKind.Viper,
                ViperDeviceName,
                "\uE962"));
        }

        foreach (var row in OpenRazerDevices)
        {
            DeviceSelectorItems.Add(new DeviceSelectorItemViewModel(
                DeviceSelectorItemKind.OpenRazer,
                row.Name,
                row.IconGlyph,
                row));
        }

        foreach (var row in OpenRazerKrakenDevices)
        {
            DeviceSelectorItems.Add(new DeviceSelectorItemViewModel(
                DeviceSelectorItemKind.Kraken,
                row.Name,
                row.IconGlyph,
                row));
        }

        foreach (var row in BlackSharkDevices)
        {
            DeviceSelectorItems.Add(new DeviceSelectorItemViewModel(
                DeviceSelectorItemKind.BlackShark,
                row.Name,
                row.IconGlyph,
                row));
        }

        foreach (var row in HeadsetAudioDevices)
        {
            DeviceSelectorItems.Add(new DeviceSelectorItemViewModel(
                DeviceSelectorItemKind.HeadsetAudio,
                row.Name,
                row.IconGlyph,
                row));
        }

        foreach (var row in KeyboardDevices)
        {
            DeviceSelectorItems.Add(new DeviceSelectorItemViewModel(
                DeviceSelectorItemKind.Keyboard,
                row.Name,
                row.IconGlyph,
                row));
        }

        foreach (var row in ChromaDevices)
        {
            DeviceSelectorItems.Add(new DeviceSelectorItemViewModel(
                DeviceSelectorItemKind.Chroma,
                row.Name,
                row.IconGlyph,
                row));
        }

        foreach (var row in BleDevices)
        {
            DeviceSelectorItems.Add(new DeviceSelectorItemViewModel(
                DeviceSelectorItemKind.Ble,
                row.Name,
                row.IconGlyph,
                row));
        }

        OnPropertyChanged(nameof(DeviceSelectorItems));
    }

    internal IReadOnlyList<OpenRazerDeviceConnection> CurrentOpenRazerConnections =>
        OpenRazerDevices.Select(row => row.Connection).ToArray();

    internal Task<bool> ApplyOpenRazerChromaFrameAsync(
        IReadOnlyList<OpenSynapse.Windows.Protocols.RazerRgb> sourceFrame,
        CancellationToken cancellationToken = default) =>
        _openRazerChromaFrameSink?.ApplyAsync(sourceFrame, cancellationToken) ??
        Task.FromResult(false);

    internal bool IsOpenRazerLightingEnabled(OpenRazerDeviceConnection connection) =>
        GetOpenRazerDeviceSettings(connection).LightingEnabled ?? true;

    internal bool IsOpenRazerChromaOverrideEnabled(OpenRazerDeviceConnection connection) =>
        GetOpenRazerDeviceSettings(connection).ChromaOverrideEnabled ?? true;

    internal async Task<bool> SaveOpenRazerLightingSettingsAsync(
        OpenRazerDeviceConnection connection,
        bool lightingEnabled,
        bool chromaOverrideEnabled,
        CancellationToken cancellationToken)
    {
        var active = GetActiveProfile();
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
        if (await SaveProfileAsync(cancellationToken))
        {
            return true;
        }

        settings.LightingEnabled = previousLightingEnabled;
        settings.ChromaOverrideEnabled = previousChromaOverrideEnabled;
        return false;
    }

    private DeviceProfileSettings GetOpenRazerDeviceSettings(OpenRazerDeviceConnection connection)
    {
        var key = ProfileResolver.GetDeviceKey(connection.ToDescriptor());
        return GetActiveProfile().Devices.TryGetValue(key, out var settings)
            ? settings
            : new DeviceProfileSettings();
    }

    public async Task SelectOpenRazerDeviceAsync(
        OpenRazerDeviceRowViewModel row,
        CancellationToken cancellationToken = default)
    {
        if (_openRazerDeviceService is null)
        {
            return;
        }

        _openRazerSelectionCancellation?.Cancel();
        _openRazerSelectionCancellation?.Dispose();
        _openRazerSelectionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var selectionToken = _openRazerSelectionCancellation.Token;
        var previous = SelectedOpenRazerDevice;
        if (previous is not null)
        {
            await previous.DisposeAsync().ConfigureAwait(false);
        }
        var selected = CreateOpenRazerDeviceViewModel(row.Connection);
        row.AttachDetail(selected);
        SelectedOpenRazerKraken = null;
        SelectedOpenRazerDevice = selected;
        await selected.LoadBasicStateAsync(selectionToken);
        await selected.LoadSoundbarAsync(selectionToken);
        await selected.LoadDpiStagesAsync(selectionToken);
        await selected.ApplyConfiguredLightingAsync(selectionToken);
        RefreshDeviceOverviewVisibility();
    }

    /// <summary>
    /// A wireless mouse left in normal mode never joins its 2.4 GHz link, so the connection
    /// stays dead until the mode is written back. Runs on discovery, but only writes once per
    /// instance: a mouse that keeps reading back normal mode would otherwise take three HID
    /// round trips on every unrelated device-tree change.
    /// </summary>
    internal async Task RestoreOpenRazerDriverModeAsync(
        IReadOnlyList<OpenRazerDeviceConnection> connections,
        CancellationToken cancellationToken = default)
    {
        if (_openRazerDeviceService is null)
        {
            return;
        }

        foreach (var connection in connections)
        {
            if (!connection.IsReady ||
                !connection.Capabilities.Contains(OpenRazerBackendCapability.DeviceModeWrite) ||
                _openRazerDriverModeChecked.Contains(connection.InstanceId))
            {
                continue;
            }

            try
            {
                await _openRazerDeviceService.EnsureDriverModeAsync(connection, cancellationToken)
                    .ConfigureAwait(false);
                _openRazerDriverModeChecked.Add(connection.InstanceId);
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
                SetDeviceOperationError(AppStrings.FormatText("OpenRazerDriverModeRestoreFailed",
                    connection.Definition.DisplayName, exception.Message));
            }
        }
    }

    internal async Task RestoreOpenRazerLightingAfterExternalAsync(
        CancellationToken cancellationToken = default)
    {
        if (_openRazerDeviceService is null)
        {
            return;
        }

        foreach (var connection in CurrentOpenRazerConnections)
        {
            if (!connection.IsReady)
            {
                continue;
            }

            await using var device = CreateOpenRazerDeviceViewModel(connection);
            await device.ApplyConfiguredLightingAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private OpenRazerDeviceViewModel CreateOpenRazerDeviceViewModel(
        OpenRazerDeviceConnection connection)
    {
        OpenRazerDeviceViewModel device = new(
            _openRazerDeviceService ?? throw new InvalidOperationException("OpenRazer service is unavailable."),
            connection,
            () => CurrentPowerSource,
            powerState => ProfileResolver.ResolveOpenRazerLighting(
                _profile,
                connection.ToDescriptor(),
                powerState),
            (powerState, profile, token) => SaveOpenRazerLightingAsync(
                connection,
                powerState,
                profile,
                token),
            () => IsOpenRazerLightingEnabled(connection),
            () => IsOpenRazerChromaOverrideEnabled(connection),
            (lightingEnabled, chromaOverrideEnabled, token) => SaveOpenRazerLightingSettingsAsync(
                connection,
                lightingEnabled,
                chromaOverrideEnabled,
                token),
            _chromaIntegrationEnabled);
        device.Onboard.LocalMacroProvider =
            () => Macros.Select(macro => macro.Definition).ToArray();
        return device;
    }

    private async Task<bool> SaveOpenRazerLightingAsync(
        OpenRazerDeviceConnection connection,
        PowerSourceKind powerState,
        LightingProfile profile,
        CancellationToken cancellationToken)
    {
        var active = GetActiveProfile();
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

        if (await SaveProfileAsync(cancellationToken))
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

    private static LightingProfile CloneLightingProfile(LightingProfile profile) => new()
    {
        Effect = profile.Effect,
        Parameters = new Dictionary<string, string>(profile.Parameters, StringComparer.OrdinalIgnoreCase),
    };

    public async Task SelectOpenRazerKrakenAsync(OpenRazerKrakenDeviceRowViewModel row)
    {
        if (_openRazerSpecialLightingService is null) return;
        _openRazerSelectionCancellation?.Cancel();
        _openRazerSelectionCancellation?.Dispose();
        _openRazerSelectionCancellation = null;
        if (SelectedOpenRazerDevice is { } previous)
        {
            await previous.DisposeAsync().ConfigureAwait(false);
        }
        SelectedOpenRazerDevice = null;
        SelectedOpenRazerKraken = new OpenRazerKrakenViewModel(
            _openRazerSpecialLightingService, row.Connection);
    }

    /// <summary>
    /// Scans the selected OpenRazer device, so a model the catalog does not describe
    /// can be mapped without touching another connected device.
    /// </summary>
    internal async Task<string> ScanSelectedOpenRazerDeviceAsync(CancellationToken cancellationToken = default)
    {
        if (_openRazerDeviceService is null)
        {
            throw new InvalidOperationException("OpenRazer support is unavailable in this build.");
        }

        // Discovery is event driven, so the list can still be empty right after a wake.
        await RefreshDevicesAsync(cancellationToken).ConfigureAwait(false);
        var connection = SelectedOpenRazerDevice?.Connection;
        if (connection is null || !connection.IsReady)
        {
            throw new InvalidOperationException("No OpenRazer device is selected.");
        }

        var scan = await _openRazerDeviceService
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
}

