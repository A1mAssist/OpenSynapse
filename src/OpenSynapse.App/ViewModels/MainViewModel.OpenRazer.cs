using OpenSynapse.Core.Profiles;
using OpenSynapse.Windows.Devices;

namespace OpenSynapse.App.ViewModels;

public sealed partial class MainViewModel
{
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
        SelectedOpenRazerKraken = null;
        SelectedOpenRazerDevice = selected;
        await selected.LoadBasicStateAsync(selectionToken);
        await selected.LoadDpiStagesAsync(selectionToken);
        await selected.ApplyConfiguredLightingAsync(selectionToken);
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
                token));

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
