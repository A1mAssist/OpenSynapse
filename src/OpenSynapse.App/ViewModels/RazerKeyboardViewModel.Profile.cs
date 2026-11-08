using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Core.Sensors;
using OpenSynapse.Windows.Devices;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerKeyboardViewModel
{
    [NotProfileScoped]
    public async Task ApplyPersistedProfileAsync(
        PowerSourceKind powerSource,
        CancellationToken cancellationToken = default)
    {
        if (_getProfile?.Invoke() is not { } document)
        {
            return;
        }

        var descriptor = new DeviceDescriptor(
            ProfileDeviceKey,
            _connection.Name,
            0x1532,
            _connection.ProductId,
            DeviceAccessState.Available,
            DeviceCapabilityState.PendingValidation,
            0,
            0,
            0,
            "keyboard",
            DeviceCategory.Keyboard);
        var keyboard = ProfileResolver.ResolveWithDeviceKey(
            document, descriptor, powerSource, ProfileDeviceKey).Keyboard;

        if (keyboard.GamingModeEnabled is bool gameMode && _hasGameMode && gameMode != GameModeEnabled)
        {
            GameModeEnabled = gameMode;
            await WriteProfileSettingAsync(
                "keyboard.game-mode",
                (service, token) => service.SetGameModeAsync(gameMode, token),
                cancellationToken);
        }

        if (keyboard.PollingRateHertz is int rate && _hasPolling)
        {
            var index = Array.IndexOf(PollingRates, rate);
            if (index >= 0 && index != SelectedPollingIndex)
            {
                SelectedPollingIndex = index;
                await WriteProfileSettingAsync(
                    "keyboard.polling-rate",
                    (service, token) => service.SetPollingRateAsync(rate, token),
                    cancellationToken);
            }
        }
    }

    private async Task WriteProfileSettingAsync(
        string key,
        Func<RazerKeyboardService, CancellationToken, Task> write,
        CancellationToken cancellationToken)
    {
        try
        {
            await write(OpenService(), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            _diagnostic?.Invoke($"profile {key}: {exception.Message}");
        }
    }

    private async Task SaveKeyboardSettingAsync(
        Action<KeyboardProfileSettings> edit,
        Action<KeyboardProfileSettings> revert,
        Action? revertUi,
        Func<CancellationToken, Task>? revertHardware,
        CancellationToken cancellationToken)
    {
        if (_getProfile is null || _saveProfile is null)
        {
            return;
        }

        var document = _getProfile();
        var created = !document.Devices.TryGetValue(ProfileDeviceKey, out var device);
        device ??= new DeviceProfileSettings();
        edit(device.Keyboard);
        if (created)
        {
            document.Devices[ProfileDeviceKey] = device;
        }

        var saved = false;
        try
        {
            saved = await _saveProfile(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch
        {
            saved = false;
        }
        if (saved)
        {
            return;
        }

        revert(device.Keyboard);
        if (created)
        {
            document.Devices.Remove(ProfileDeviceKey);
        }
        revertUi?.Invoke();
        if (revertHardware is not null)
        {
            try
            {
                await revertHardware(CancellationToken.None);
            }
            catch
            {
                // The save failure is the error the user must see.
            }
        }
        ErrorText = AppStrings.Text("RazerHidSettingProfileSaveFailed");
    }
}
