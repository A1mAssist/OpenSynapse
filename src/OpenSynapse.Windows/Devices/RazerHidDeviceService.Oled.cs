using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed partial class RazerHidDeviceService
{
    public async Task<byte> GetOledBrightnessAsync(
        RazerHidDeviceConnection connection,
        CancellationToken cancellationToken = default)
    {
        RequireReady(connection);
        var request = RazerOledProtocol.CreateGetBrightnessRequest(MiddlewareTransactionId);
        var response = await ExecuteAsync(connection,
            new RazerHidRequest("razer_oled_get_brightness", request), cancellationToken).ConfigureAwait(false);
        return RazerOledProtocol.ReadBrightness(response);
    }

    public Task SetOledBrightnessAsync(
        RazerHidDeviceConnection connection,
        byte brightness,
        CancellationToken cancellationToken = default)
    {
        RequireReady(connection);
        return ExecuteWithoutResultAsync(connection,
            new RazerHidRequest("razer_oled_set_brightness",
                RazerOledProtocol.CreateSetBrightnessRequest(MiddlewareTransactionId, brightness)),
            cancellationToken);
    }

    public async Task<byte> GetOledLanguageAsync(
        RazerHidDeviceConnection connection,
        CancellationToken cancellationToken = default)
    {
        RequireReady(connection);
        var request = RazerOledProtocol.CreateGetLanguageRequest(MiddlewareTransactionId);
        var response = await ExecuteAsync(connection,
            new RazerHidRequest("razer_oled_get_language", request), cancellationToken).ConfigureAwait(false);
        return RazerOledProtocol.ReadLanguage(response);
    }

    public Task SetOledLanguageAsync(
        RazerHidDeviceConnection connection,
        byte language,
        CancellationToken cancellationToken = default)
    {
        RequireReady(connection);
        return ExecuteWithoutResultAsync(connection,
            new RazerHidRequest("razer_oled_set_language",
                RazerOledProtocol.CreateSetLanguageRequest(MiddlewareTransactionId, language)),
            cancellationToken);
    }

    public Task SetOledHomeScreenActiveItemAsync(
        RazerHidDeviceConnection connection,
        byte activeItem,
        CancellationToken cancellationToken = default)
    {
        RequireReady(connection);
        return ExecuteWithoutResultAsync(connection,
            new RazerHidRequest("razer_oled_set_home_screen_active_item",
                RazerOledProtocol.CreateSetHomeScreenActiveItemRequest(MiddlewareTransactionId, activeItem)),
            cancellationToken);
    }

    public async Task<RazerOledScreensaverOption> GetOledScreensaverOptionAsync(
        RazerHidDeviceConnection connection,
        CancellationToken cancellationToken = default)
    {
        RequireReady(connection);
        var request = RazerOledProtocol.CreateGetScreensaverOptionRequest(MiddlewareTransactionId);
        var response = await ExecuteAsync(connection,
            new RazerHidRequest("razer_oled_get_screensaver_option", request), cancellationToken).ConfigureAwait(false);
        return RazerOledProtocol.ReadScreensaverOption(response);
    }

    public Task SetOledHomeScreenImageStateAsync(
        RazerHidDeviceConnection connection,
        byte[] imageStates,
        CancellationToken cancellationToken = default)
    {
        RequireReady(connection);
        return ExecuteWithoutResultAsync(connection,
            new RazerHidRequest("razer_oled_set_home_screen_image_state",
                RazerOledProtocol.CreateSetHomeScreenImageStateRequest(MiddlewareTransactionId, imageStates)),
            cancellationToken);
    }

    public async Task<byte[]> GetOledHomeScreenAnimationStateAsync(
        RazerHidDeviceConnection connection,
        CancellationToken cancellationToken = default)
    {
        RequireReady(connection);
        var request = RazerOledProtocol.CreateGetHomeScreenAnimationStateRequest(MiddlewareTransactionId);
        var response = await ExecuteAsync(connection,
            new RazerHidRequest("razer_oled_get_home_screen_animation_state", request),
            cancellationToken).ConfigureAwait(false);
        return RazerOledProtocol.ReadHomeScreenAnimationState(response);
    }

    public Task SetOledTimeToDimAsync(
        RazerHidDeviceConnection connection,
        byte minutes,
        CancellationToken cancellationToken = default)
    {
        RequireReady(connection);
        return ExecuteWithoutResultAsync(connection,
            new RazerHidRequest("razer_oled_set_time_to_dim",
                RazerOledProtocol.CreateSetTimeToDimRequest(MiddlewareTransactionId, minutes)),
            cancellationToken);
    }

    public Task SetOledLowBatteryWarningPercentageAsync(
        RazerHidDeviceConnection connection,
        byte percentage,
        CancellationToken cancellationToken = default)
    {
        RequireReady(connection);
        return ExecuteWithoutResultAsync(connection,
            new RazerHidRequest("razer_oled_set_low_battery_warning_percentage",
                RazerOledProtocol.CreateSetLowBatteryWarningPercentageRequest(MiddlewareTransactionId, percentage)),
            cancellationToken);
    }

    public Task SetOledUpdateVolumeAsync(
        RazerHidDeviceConnection connection,
        byte volume,
        bool muted,
        CancellationToken cancellationToken = default)
    {
        RequireReady(connection);
        return ExecuteWithoutResultAsync(connection,
            new RazerHidRequest("razer_oled_set_update_volume",
                RazerOledProtocol.CreateSetUpdateVolumeRequest(MiddlewareTransactionId, volume, muted)),
            cancellationToken);
    }

    public async Task<RazerChromaBrightnessStatus> GetOledChromaBrightnessStatusAsync(
        RazerHidDeviceConnection connection,
        byte profileId,
        CancellationToken cancellationToken = default)
    {
        RequireReady(connection);
        var request = RazerOledProtocol.CreateGetChromaBrightnessStatusRequest(
            MiddlewareTransactionId, profileId);
        var response = await ExecuteAsync(connection,
            new RazerHidRequest("razer_oled_get_chroma_brightness_status", request),
            cancellationToken).ConfigureAwait(false);
        return RazerOledProtocol.ReadChromaBrightnessStatus(response);
    }

    public Task SetOledChromaBrightnessStatusAsync(
        RazerHidDeviceConnection connection,
        byte profileId,
        byte regionId,
        byte status,
        CancellationToken cancellationToken = default)
    {
        RequireReady(connection);
        return ExecuteWithoutResultAsync(connection,
            new RazerHidRequest("razer_oled_set_chroma_brightness_status",
                RazerOledProtocol.CreateSetChromaBrightnessStatusRequest(
                    MiddlewareTransactionId, profileId, regionId, status)),
            cancellationToken);
    }

    internal async Task ProbeOledAsync(
        RazerHidDeviceDefinition definition,
        string devicePath,
        ISet<RazerHidBackendCapability> result,
        CancellationToken cancellationToken)
    {
        if (definition.Category != DeviceCategory.Keyboard)
        {
            return;
        }

        var request = RazerOledProtocol.CreateGetBrightnessRequest(MiddlewareTransactionId);
        if (await ProbeMiddlewareAsync(
                definition.ProductId,
                devicePath,
                definition.DeviceWait,
                "keyboard.oledBrightness",
                request,
                response =>
                {
                    _ = RazerOledProtocol.ReadBrightness(response);
                    return true;
                },
                cancellationToken).ConfigureAwait(false))
        {
            result.Add(RazerHidBackendCapability.OledRead);
            result.Add(RazerHidBackendCapability.OledWrite);
        }
    }
}
