using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHidDeviceViewModel
{
    private DeviceProfileSettings? ResolveDeviceProfileSettings()
    {
        if (_getActiveProfile is null)
        {
            return null;
        }

        var active = _getActiveProfile();
        var key = ProfileResolver.GetDeviceKey(Connection.ToDescriptor());
        if (!active.Devices.TryGetValue(key, out var settings))
        {
            settings = new DeviceProfileSettings();
            active.Devices[key] = settings;
        }

        return settings;
    }

    private T ReadProfile<T>(Func<DeviceProfileSettings, T> read)
    {
        if (_getActiveProfile is null)
        {
            return default!;
        }

        var active = _getActiveProfile();
        var key = ProfileResolver.GetDeviceKey(Connection.ToDescriptor());
        return active.Devices.TryGetValue(key, out var settings) ? read(settings) : default!;
    }

    private bool IsKeyboard => Connection.Definition.Category == DeviceCategory.Keyboard;

    private static string ToProfileScrollMode(byte mode) => mode switch
    {
        1 => MouseScrollMode.FreeSpin.ToString(),
        2 => MouseScrollMode.MicroTactile.ToString(),
        _ => MouseScrollMode.Tactile.ToString(),
    };

    /// <summary>
    /// Persists a setting after its hardware write succeeded. A failed save restores
    /// the in-memory profile, the edited property, and the previous hardware value.
    /// </summary>
    private async Task SaveProfileAsync(
        Action<DeviceProfileSettings> edit,
        Action<DeviceProfileSettings> revert,
        Action? revertUi,
        Func<CancellationToken, Task>? revertHardware,
        CancellationToken cancellationToken)
    {
        if (_getActiveProfile is null || _saveProfileAsync is null)
        {
            return;
        }

        var settings = ResolveDeviceProfileSettings()!;
        edit(settings);
        if (await _saveProfileAsync(cancellationToken))
        {
            _operationStatusOverride = AppStrings.Text("RazerHidOperationSaved");
            return;
        }

        revert(settings);
        revertUi?.Invoke();
        if (revertHardware is not null)
        {
            try
            {
                await revertHardware(CancellationToken.None);
            }
            catch
            {
                // The save failure is what the user must see; a failed hardware
                // rollback must not replace it.
            }
        }

        throw new RazerHidSettingProfileSaveException("RazerHid setting could not be saved.");
    }
}

internal sealed class RazerHidSettingProfileSaveException(string message) : IOException(message);
