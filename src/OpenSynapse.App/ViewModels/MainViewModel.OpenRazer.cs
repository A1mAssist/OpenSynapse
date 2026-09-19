using OpenSynapse.Core.Profiles;
using OpenSynapse.Windows.Devices;

namespace OpenSynapse.App.ViewModels;

public sealed partial class MainViewModel
{
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
        var selected = new OpenRazerDeviceViewModel(
            _openRazerDeviceService,
            row.Connection,
            () => _powerSourceProvider.IsPluggedIn,
            powerState => ProfileResolver.ResolveOpenRazerLighting(
                _profile,
                row.Connection.ToDescriptor(),
                powerState),
            (powerState, profile, token) => SaveOpenRazerLightingAsync(
                row.Connection,
                powerState,
                profile,
                token));
        SelectedOpenRazerKraken = null;
        SelectedOpenRazerDevice = selected;
        await selected.LoadBasicStateAsync(_openRazerSelectionCancellation.Token);
        await selected.ApplyConfiguredLightingAsync(_openRazerSelectionCancellation.Token);
    }

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
