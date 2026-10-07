using OpenSynapse.Core.Profiles;
using OpenSynapse.Core.Devices;
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
        DeviceSelectorItems.Add(new DeviceSelectorItemViewModel(
            DeviceSelectorItemKind.Blade,
            BladeDeviceName,
            "\uE7F8"));
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
        var selected = CreateOpenRazerDeviceViewModel(row.Connection);
        row.AttachDetail(selected);
        SelectedOpenRazerKraken = null;
        SelectedOpenRazerDevice = selected;
        await selected.LoadBasicStateAsync(selectionToken);
        await selected.LoadDpiStagesAsync(selectionToken);
        await selected.ApplyConfiguredLightingAsync(selectionToken);
        RefreshDeviceOverviewVisibility();
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

            var device = CreateOpenRazerDeviceViewModel(connection);
            await device.ApplyConfiguredLightingAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private OpenRazerDeviceViewModel CreateOpenRazerDeviceViewModel(
        OpenRazerDeviceConnection connection) =>
        new(
            _openRazerDeviceService ?? throw new InvalidOperationException("OpenRazer service is unavailable."),
            connection,
            () => _powerSourceProvider.IsPluggedIn,
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

    private async Task<bool> SaveOpenRazerLightingAsync(
        OpenRazerDeviceConnection connection,
        bool? powerState,
        LightingProfile profile,
        CancellationToken cancellationToken)
    {
        var active = GetActiveProfile();
        var key = ProfileResolver.GetDeviceKey(connection.ToDescriptor());
        Dictionary<string, LightingProfile>? target = powerState switch
        {
            true => active.PluggedIn.OpenRazerLighting,
            false => active.OnBattery.OpenRazerLighting,
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

    public void SelectOpenRazerKraken(OpenRazerKrakenDeviceRowViewModel row)
    {
        if (_openRazerSpecialLightingService is null) return;
        _openRazerSelectionCancellation?.Cancel();
        _openRazerSelectionCancellation?.Dispose();
        _openRazerSelectionCancellation = null;
        SelectedOpenRazerDevice = null;
        SelectedOpenRazerKraken = new OpenRazerKrakenViewModel(
            _openRazerSpecialLightingService, row.Connection);
    }
}
