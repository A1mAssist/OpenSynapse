using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed partial class OpenRazerDeviceService
{
    // The keyboard middleware addresses the knob as LED class 3 / id 0x24; the probe
    // needs a concrete selector while the public methods keep the device's values.
    private const byte KeyboardKnobLedClass = 0x03;
    private const byte KeyboardKnobLedId = 0x24;

    public async Task<RazerKnobConfiguration> GetKnobConfigurationAsync(
        OpenRazerDeviceConnection connection,
        byte ledClass,
        byte ledId,
        CancellationToken cancellationToken = default)
    {
        RequireReady(connection);
        var request = RazerKnobProtocol.CreateGetConfigurationRequest(
            MiddlewareTransactionId, ledClass, ledId);
        var response = await ExecuteAsync(connection,
            new OpenRazerRequest("razer_knob_get_configuration", request),
            cancellationToken).ConfigureAwait(false);
        return RazerKnobProtocol.ParseConfiguration(response, request);
    }

    public Task SetKnobConfigurationAsync(
        OpenRazerDeviceConnection connection,
        byte ledClass,
        byte ledId,
        IReadOnlyList<RazerKnobStage> stages,
        CancellationToken cancellationToken = default)
    {
        RequireReady(connection);
        ArgumentNullException.ThrowIfNull(stages);
        return ExecuteWithoutResultAsync(connection,
            new OpenRazerRequest("razer_knob_set_configuration",
                RazerKnobProtocol.CreateSetConfigurationRequest(
                    MiddlewareTransactionId, ledClass, ledId, stages.ToArray())),
            cancellationToken);
    }

    public async Task<RazerKnobModeState> GetKnobModeAsync(
        OpenRazerDeviceConnection connection,
        byte ledClass,
        byte ledId,
        CancellationToken cancellationToken = default)
    {
        RequireReady(connection);
        var request = RazerKnobProtocol.CreateGetModeRequest(
            MiddlewareTransactionId, ledClass, ledId);
        var response = await ExecuteAsync(connection,
            new OpenRazerRequest("razer_knob_get_mode", request),
            cancellationToken).ConfigureAwait(false);
        return RazerKnobProtocol.ParseMode(response, request);
    }

    public Task SetKnobModeAsync(
        OpenRazerDeviceConnection connection,
        byte ledClass,
        byte ledId,
        RazerKnobStage stage,
        CancellationToken cancellationToken = default)
    {
        RequireReady(connection);
        return ExecuteWithoutResultAsync(connection,
            new OpenRazerRequest("razer_knob_set_mode",
                RazerKnobProtocol.CreateSetModeRequest(
                    MiddlewareTransactionId, ledClass, ledId, stage)),
            cancellationToken);
    }

    public async Task<RazerAnalogKeyActuationTable> GetAnalogKeyActuationPointsAsync(
        OpenRazerDeviceConnection connection,
        byte dataType,
        byte classId,
        IReadOnlyList<byte> keyIds,
        CancellationToken cancellationToken = default)
    {
        RequireReady(connection);
        var request = RazerAnalogKeyProtocol.CreateGetMultiAnalogKeyActuationPointRequest(
            MiddlewareTransactionId, dataType, classId, keyIds);
        var response = await ExecuteAsync(connection,
            new OpenRazerRequest("razer_analog_get_actuation_points", request),
            cancellationToken).ConfigureAwait(false);
        return RazerAnalogKeyProtocol.ParseMultiAnalogKeyActuationPoints(response);
    }

    public Task SetAnalogKeyActuationPointsAsync(
        OpenRazerDeviceConnection connection,
        byte dataType,
        byte classId,
        IReadOnlyList<RazerAnalogActuationPoint> points,
        CancellationToken cancellationToken = default)
    {
        RequireReady(connection);
        return ExecuteWithoutResultAsync(connection,
            new OpenRazerRequest("razer_analog_set_actuation_points",
                RazerAnalogKeyProtocol.CreateSetMultiAnalogKeyActuationPointRequest(
                    MiddlewareTransactionId, dataType, classId, points)),
            cancellationToken);
    }

    public async Task<RazerAnalogKeyRapidTriggerTable> GetAnalogKeyRapidTriggersAsync(
        OpenRazerDeviceConnection connection,
        byte dataType,
        byte classId,
        IReadOnlyList<byte> keyIds,
        CancellationToken cancellationToken = default)
    {
        RequireReady(connection);
        var request = RazerAnalogKeyProtocol.CreateGetMultiAnalogKeyRapidTriggerRequest(
            MiddlewareTransactionId, dataType, classId, keyIds);
        var response = await ExecuteAsync(connection,
            new OpenRazerRequest("razer_analog_get_rapid_triggers", request),
            cancellationToken).ConfigureAwait(false);
        return RazerAnalogKeyProtocol.ParseMultiAnalogKeyRapidTriggers(response);
    }

    public Task SetAnalogKeyRapidTriggersAsync(
        OpenRazerDeviceConnection connection,
        byte dataType,
        byte classId,
        IReadOnlyList<RazerAnalogRapidTrigger> settings,
        CancellationToken cancellationToken = default)
    {
        RequireReady(connection);
        return ExecuteWithoutResultAsync(connection,
            new OpenRazerRequest("razer_analog_set_rapid_triggers",
                RazerAnalogKeyProtocol.CreateSetMultiAnalogKeyRapidTriggerRequest(
                    MiddlewareTransactionId, dataType, classId, settings)),
            cancellationToken);
    }

    public async Task<RazerAnalogKeyContinuousRapidTrigger> GetAnalogKeyContinuousRapidTriggerAsync(
        OpenRazerDeviceConnection connection,
        byte classId,
        CancellationToken cancellationToken = default)
    {
        RequireReady(connection);
        var request = RazerAnalogKeyProtocol.CreateGetAnalogKeyContinuousRapidTriggerRequest(
            MiddlewareTransactionId, classId);
        var response = await ExecuteAsync(connection,
            new OpenRazerRequest("razer_analog_get_continuous_rapid_trigger", request),
            cancellationToken).ConfigureAwait(false);
        return RazerAnalogKeyProtocol.ParseAnalogKeyContinuousRapidTrigger(response);
    }

    public Task SetAnalogKeyContinuousRapidTriggerAsync(
        OpenRazerDeviceConnection connection,
        byte classId,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        RequireReady(connection);
        return ExecuteWithoutResultAsync(connection,
            new OpenRazerRequest("razer_analog_set_continuous_rapid_trigger",
                RazerAnalogKeyProtocol.CreateSetAnalogKeyContinuousRapidTriggerRequest(
                    MiddlewareTransactionId, classId, enabled)),
            cancellationToken);
    }

    public async Task<RazerAnalogKeyModTapTable> GetAnalogKeyModTapAssignmentsAsync(
        OpenRazerDeviceConnection connection,
        byte classId,
        IReadOnlyList<byte> keyIds,
        CancellationToken cancellationToken = default)
    {
        RequireReady(connection);
        var request = RazerAnalogKeyProtocol.CreateGetMultiModTapKeyAssignmentRequest(
            MiddlewareTransactionId, classId, keyIds);
        var response = await ExecuteAsync(connection,
            new OpenRazerRequest("razer_analog_get_mod_tap_assignments", request),
            cancellationToken).ConfigureAwait(false);
        return RazerAnalogKeyProtocol.ParseMultiModTapKeyAssignments(response);
    }

    public Task SetAnalogKeyModTapAssignmentsAsync(
        OpenRazerDeviceConnection connection,
        byte classId,
        IReadOnlyList<RazerAnalogModTapAssignment> assignments,
        CancellationToken cancellationToken = default)
    {
        RequireReady(connection);
        return ExecuteWithoutResultAsync(connection,
            new OpenRazerRequest("razer_analog_set_mod_tap_assignments",
                RazerAnalogKeyProtocol.CreateSetMultiModTapKeyAssignmentRequest(
                    MiddlewareTransactionId, classId, assignments)),
            cancellationToken);
    }

    public async Task<RazerAnalogKeyAssignmentTable> GetAnalogKeyAssignmentsAsync(
        OpenRazerDeviceConnection connection,
        byte classId,
        IReadOnlyList<RazerAnalogKeyAssignmentSelector> selectors,
        CancellationToken cancellationToken = default)
    {
        RequireReady(connection);
        var request = RazerAnalogKeyProtocol.CreateGetMultiKeyAssignmentRequest(
            MiddlewareTransactionId, classId, selectors);
        var response = await ExecuteAsync(connection,
            new OpenRazerRequest("razer_analog_get_key_assignments", request),
            cancellationToken).ConfigureAwait(false);
        return RazerAnalogKeyProtocol.ParseMultiKeyAssignments(response);
    }

    public Task SetAnalogKeyAssignmentsAsync(
        OpenRazerDeviceConnection connection,
        byte classId,
        IReadOnlyList<RazerAnalogKeyAssignment> assignments,
        CancellationToken cancellationToken = default)
    {
        RequireReady(connection);
        return ExecuteWithoutResultAsync(connection,
            new OpenRazerRequest("razer_analog_set_key_assignments",
                RazerAnalogKeyProtocol.CreateSetMultiKeyAssignmentRequest(
                    MiddlewareTransactionId, classId, assignments)),
            cancellationToken);
    }

    public async Task<RazerAnalogKeyBottomDeadZone> GetAnalogKeyBottomDeadZoneAsync(
        OpenRazerDeviceConnection connection,
        byte classId,
        CancellationToken cancellationToken = default)
    {
        RequireReady(connection);
        var request = RazerAnalogKeyProtocol.CreateGetAnalogKeyBottomDeadZoneRequest(
            MiddlewareTransactionId, classId);
        var response = await ExecuteAsync(connection,
            new OpenRazerRequest("razer_analog_get_bottom_dead_zone", request),
            cancellationToken).ConfigureAwait(false);
        return RazerAnalogKeyProtocol.ParseAnalogKeyBottomDeadZone(response);
    }

    public Task SetAnalogKeyBottomDeadZoneAsync(
        OpenRazerDeviceConnection connection,
        byte classId,
        byte bottomDeadZone,
        CancellationToken cancellationToken = default)
    {
        RequireReady(connection);
        return ExecuteWithoutResultAsync(connection,
            new OpenRazerRequest("razer_analog_set_bottom_dead_zone",
                RazerAnalogKeyProtocol.CreateSetAnalogKeyBottomDeadZoneRequest(
                    MiddlewareTransactionId, classId, bottomDeadZone)),
            cancellationToken);
    }

    public async Task<byte> GetAnalogReportModeAsync(
        OpenRazerDeviceConnection connection,
        CancellationToken cancellationToken = default)
    {
        RequireReady(connection);
        var request = RazerAnalogKeyProtocol.CreateGetAnalogReportModeRequest(MiddlewareTransactionId);
        var response = await ExecuteAsync(connection,
            new OpenRazerRequest("razer_analog_get_report_mode", request),
            cancellationToken).ConfigureAwait(false);
        return RazerAnalogKeyProtocol.ParseAnalogReportMode(response);
    }

    public Task SetAnalogReportModeAsync(
        OpenRazerDeviceConnection connection,
        byte reportMode,
        CancellationToken cancellationToken = default)
    {
        RequireReady(connection);
        return ExecuteWithoutResultAsync(connection,
            new OpenRazerRequest("razer_analog_set_report_mode",
                RazerAnalogKeyProtocol.CreateSetAnalogReportModeRequest(
                    MiddlewareTransactionId, reportMode)),
            cancellationToken);
    }

    public async Task<RazerAnalogKeyKillSwitch> GetKeyboardKillSwitchAsync(
        OpenRazerDeviceConnection connection,
        byte classId,
        CancellationToken cancellationToken = default)
    {
        RequireReady(connection);
        var request = RazerAnalogKeyProtocol.CreateGetKeyboardKillSwitchRequest(
            MiddlewareTransactionId, classId);
        var response = await ExecuteAsync(connection,
            new OpenRazerRequest("razer_keyboard_get_kill_switch", request),
            cancellationToken).ConfigureAwait(false);
        return RazerAnalogKeyProtocol.ParseKeyboardKillSwitch(response);
    }

    public Task SetKeyboardKillSwitchAsync(
        OpenRazerDeviceConnection connection,
        byte classId,
        bool enabled,
        byte keyId1,
        byte keyId2,
        CancellationToken cancellationToken = default)
    {
        RequireReady(connection);
        return ExecuteWithoutResultAsync(connection,
            new OpenRazerRequest("razer_keyboard_set_kill_switch",
                RazerAnalogKeyProtocol.CreateSetKeyboardKillSwitchRequest(
                    MiddlewareTransactionId, classId, enabled, keyId1, keyId2)),
            cancellationToken);
    }

    internal async Task ProbeKnobAsync(
        OpenRazerDeviceDefinition definition,
        string devicePath,
        ISet<OpenRazerBackendCapability> result,
        CancellationToken cancellationToken)
    {
        if (definition.Category is not DeviceCategory.Keyboard)
        {
            return;
        }

        var request = RazerKnobProtocol.CreateGetModeRequest(
            MiddlewareTransactionId, KeyboardKnobLedClass, KeyboardKnobLedId);
        if (await ProbeMiddlewareAsync(definition.ProductId, devicePath, definition.DeviceWait,
                "keyboard.knobMode", request, response =>
                {
                    _ = RazerKnobProtocol.ParseMode(response, request);
                    return true;
                }, cancellationToken).ConfigureAwait(false))
        {
            result.Add(OpenRazerBackendCapability.KnobRead);
            result.Add(OpenRazerBackendCapability.KnobWrite);
        }
    }

    internal async Task ProbeAnalogKeyAsync(
        OpenRazerDeviceDefinition definition,
        string devicePath,
        ISet<OpenRazerBackendCapability> result,
        CancellationToken cancellationToken)
    {
        if (definition.Category is not DeviceCategory.Keyboard)
        {
            return;
        }

        var request = RazerAnalogKeyProtocol.CreateGetAnalogReportModeRequest(MiddlewareTransactionId);
        if (await ProbeMiddlewareAsync(definition.ProductId, devicePath, definition.DeviceWait,
                "keyboard.analogKey", request, response =>
                {
                    _ = RazerAnalogKeyProtocol.ParseAnalogReportMode(response);
                    return true;
                }, cancellationToken).ConfigureAwait(false))
        {
            result.Add(OpenRazerBackendCapability.AnalogKeyRead);
            result.Add(OpenRazerBackendCapability.AnalogKeyWrite);
        }
    }
}
