using System.Text.Json;
using OpenSynapse.Core.Profiles;

namespace OpenSynapse.App.ViewModels;

/// <summary>
/// Saves one transport-keyed headset entry and restores the whole entry when the
/// write fails, so the in-memory profile never disagrees with the device rollback.
/// </summary>
internal static class HeadsetProfilePersistence
{
    public static async Task<bool> SaveAsync(
        ProfileDocument? document,
        Func<CancellationToken, Task<bool>>? save,
        string deviceKey,
        Action<HeadsetProfileSettings> mutate,
        CancellationToken cancellationToken)
    {
        if (document is null || save is null)
        {
            return true;
        }

        var created = !document.Devices.TryGetValue(deviceKey, out var device);
        device ??= new DeviceProfileSettings();
        device.Headset ??= new HeadsetProfileSettings();
        var previous = JsonSerializer.SerializeToNode(device);
        mutate(device.Headset);
        if (created)
        {
            document.Devices[deviceKey] = device;
        }

        try
        {
            if (await save(cancellationToken))
            {
                return true;
            }
        }
        catch
        {
            Restore();
            throw;
        }

        Restore();
        return false;

        void Restore()
        {
            if (created)
            {
                document.Devices.Remove(deviceKey);
                return;
            }

            document.Devices[deviceKey] =
                previous?.Deserialize<DeviceProfileSettings>() ?? new DeviceProfileSettings();
        }
    }
}
