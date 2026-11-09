using System.Collections.Frozen;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed partial class RazerHidDeviceService
{
    private static readonly IReadOnlySet<RazerHidBackendCapability> OptionalStateCapabilities =
        new HashSet<RazerHidBackendCapability>
        {
            RazerHidBackendCapability.AutoSleepRead,
            RazerHidBackendCapability.AutoSleepWrite,
            RazerHidBackendCapability.TimeToDimRead,
            RazerHidBackendCapability.TimeToDimWrite,
            RazerHidBackendCapability.IndicatorLedConfigurationRead,
            RazerHidBackendCapability.IndicatorLedConfigurationWrite,
            RazerHidBackendCapability.SensorRotationRead,
            RazerHidBackendCapability.SensorRotationWrite,
        }.ToFrozenSet();

    /// <summary>
    /// Probes the optional power/indicator settings that otherwise leak from the shared
    /// middleware command table onto devices whose firmware does not implement them.
    /// </summary>
    internal async Task<IReadOnlySet<RazerHidBackendCapability>> ProbeOptionalStateCapabilitiesAsync(
        RazerHidDeviceDefinition definition,
        string devicePath,
        CancellationToken cancellationToken = default)
    {
        if (definition.Category is not (DeviceCategory.Mouse or DeviceCategory.Keyboard))
        {
            return FrozenSet<RazerHidBackendCapability>.Empty;
        }

        var result = new HashSet<RazerHidBackendCapability>();

        if (definition.Category == DeviceCategory.Mouse)
        {
            if (await ProbeSensorRotationAsync(definition, devicePath, cancellationToken)
                    .ConfigureAwait(false))
            {
                result.Add(RazerHidBackendCapability.SensorRotationRead);
                result.Add(RazerHidBackendCapability.SensorRotationWrite);
            }
        }

        if (definition.HasSourceCapability("get_auto_sleep") &&
            definition.Transactions.ContainsKey("razer_chroma_misc_get_auto_sleep") &&
            await ProbeMiddlewareAsync(definition.ProductId, devicePath, definition.DeviceWait,
                "misc.autoSleep",
                RazerHidMouseProtocol.GetAutoSleep(definition).Report,
                response =>
                {
                    _ = RazerHidMouseProtocol.ParseAutoSleepEnabled(response);
                    return true;
                }, cancellationToken).ConfigureAwait(false))
        {
            result.Add(RazerHidBackendCapability.AutoSleepRead);
            result.Add(RazerHidBackendCapability.AutoSleepWrite);
        }

        if (definition.HasSourceCapability("get_time_to_dim") &&
            definition.Transactions.ContainsKey("razer_chroma_misc_get_time_to_dim") &&
            await ProbeMiddlewareAsync(definition.ProductId, devicePath, definition.DeviceWait,
                "misc.timeToDim",
                RazerHidMouseProtocol.GetTimeToDim(definition).Report,
                response =>
                {
                    _ = RazerHidMouseProtocol.ParseTimeToDim(response);
                    return true;
                }, cancellationToken).ConfigureAwait(false))
        {
            result.Add(RazerHidBackendCapability.TimeToDimRead);
            result.Add(RazerHidBackendCapability.TimeToDimWrite);
        }

        if (definition.HasSourceCapability("get_indicator_led_configuration") &&
            definition.Transactions.ContainsKey("razer_chroma_misc_get_indicator_led_configuration") &&
            await ProbeMiddlewareAsync(definition.ProductId, devicePath, definition.DeviceWait,
                "misc.indicatorLed",
                RazerHidMouseProtocol.GetIndicatorLedConfiguration(definition).Report,
                response =>
                {
                    _ = RazerHidMouseProtocol.ParseIndicatorLedConfiguration(response);
                    return true;
                }, cancellationToken).ConfigureAwait(false))
        {
            result.Add(RazerHidBackendCapability.IndicatorLedConfigurationRead);
            result.Add(RazerHidBackendCapability.IndicatorLedConfigurationWrite);
        }

        return result.ToFrozenSet();
    }

    private Task<bool> ProbeSensorRotationAsync(
        RazerHidDeviceDefinition definition,
        string devicePath,
        CancellationToken cancellationToken)
    {
        var request = RazerProximitySensorProtocol.CreateGetSensorRotationRequest(
            MiddlewareTransactionId);
        return ProbeMiddlewareAsync(
            definition.ProductId,
            devicePath,
            definition.DeviceWait,
            "mouse.sensorRotation",
            request,
            response =>
            {
                _ = RazerProximitySensorProtocol.ParseSensorRotation(response, request);
                return true;
            },
            cancellationToken);
    }
}
