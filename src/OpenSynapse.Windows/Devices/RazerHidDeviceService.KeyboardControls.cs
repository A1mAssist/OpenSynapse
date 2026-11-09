using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed partial class RazerHidDeviceService
{
    // The keyboard middleware addresses the knob as LED class 3 / id 0x24; the probe
    // needs a concrete selector while the public methods keep the device's values.
    private const byte KeyboardKnobLedClass = 0x03;
    private const byte KeyboardKnobLedId = 0x24;

    public async Task<RazerKnobConfiguration> GetKnobConfigurationAsync(
        RazerHidDeviceConnection connection,
        byte ledClass,
        byte ledId,
        CancellationToken cancellationToken = default)
    {
        RequireReady(connection);
        var request = RazerKnobProtocol.CreateGetConfigurationRequest(
            MiddlewareTransactionId, ledClass, ledId);
        var response = await ExecuteAsync(connection,
            new RazerHidRequest("razer_knob_get_configuration", request),
            cancellationToken).ConfigureAwait(false);
        return RazerKnobProtocol.ParseConfiguration(response, request);
    }

    public Task SetKnobConfigurationAsync(
        RazerHidDeviceConnection connection,
        byte ledClass,
        byte ledId,
        IReadOnlyList<RazerKnobStage> stages,
        CancellationToken cancellationToken = default)
    {
        RequireReady(connection);
        ArgumentNullException.ThrowIfNull(stages);
        return ExecuteWithoutResultAsync(connection,
            new RazerHidRequest("razer_knob_set_configuration",
                RazerKnobProtocol.CreateSetConfigurationRequest(
                    MiddlewareTransactionId, ledClass, ledId, stages.ToArray())),
            cancellationToken);
    }

    public async Task<RazerKnobModeState> GetKnobModeAsync(
        RazerHidDeviceConnection connection,
        byte ledClass,
        byte ledId,
        CancellationToken cancellationToken = default)
    {
        RequireReady(connection);
        var request = RazerKnobProtocol.CreateGetModeRequest(
            MiddlewareTransactionId, ledClass, ledId);
        var response = await ExecuteAsync(connection,
            new RazerHidRequest("razer_knob_get_mode", request),
            cancellationToken).ConfigureAwait(false);
        return RazerKnobProtocol.ParseMode(response, request);
    }

    public Task SetKnobModeAsync(
        RazerHidDeviceConnection connection,
        byte ledClass,
        byte ledId,
        RazerKnobStage stage,
        CancellationToken cancellationToken = default)
    {
        RequireReady(connection);
        return ExecuteWithoutResultAsync(connection,
            new RazerHidRequest("razer_knob_set_mode",
                RazerKnobProtocol.CreateSetModeRequest(
                    MiddlewareTransactionId, ledClass, ledId, stage)),
            cancellationToken);
    }

    public async Task<RazerAnalogKeyActuationTable> GetAnalogKeyActuationPointsAsync(
        RazerHidDeviceConnection connection,
        byte dataType,
        byte classId,
        IReadOnlyList<byte> keyIds,
        CancellationToken cancellationToken = default)
    {
        RequireReady(connection);
        var request = RazerAnalogKeyProtocol.CreateGetMultiAnalogKeyActuationPointRequest(
            MiddlewareTransactionId, dataType, classId, keyIds);
        var response = await ExecuteAsync(connection,
            new RazerHidRequest("razer_analog_get_actuation_points", request),
            cancellationToken).ConfigureAwait(false);
        return RazerAnalogKeyProtocol.ParseMultiAnalogKeyActuationPoints(response);
    }

    public Task SetAnalogKeyActuationPointsAsync(
        RazerHidDeviceConnection connection,
        byte dataType,
        byte classId,
        IReadOnlyList<RazerAnalogActuationPoint> points,
        CancellationToken cancellationToken = default)
    {
        RequireReady(connection);
        return ExecuteWithoutResultAsync(connection,
            new RazerHidRequest("razer_analog_set_actuation_points",
                RazerAnalogKeyProtocol.CreateSetMultiAnalogKeyActuationPointRequest(
                    MiddlewareTransactionId, dataType, classId, points)),
            cancellationToken);
    }

    public async Task<RazerAnalogKeyRapidTriggerTable> GetAnalogKeyRapidTriggersAsync(
        RazerHidDeviceConnection connection,
        byte dataType,
        byte classId,
        IReadOnlyList<byte> keyIds,
        CancellationToken cancellationToken = default)
    {
        RequireReady(connection);
        var request = RazerAnalogKeyProtocol.CreateGetMultiAnalogKeyRapidTriggerRequest(
            MiddlewareTransactionId, dataType, classId, keyIds);
        var response = await ExecuteAsync(connection,
            new RazerHidRequest("razer_analog_get_rapid_triggers", request),
            cancellationToken).ConfigureAwait(false);
        return RazerAnalogKeyProtocol.ParseMultiAnalogKeyRapidTriggers(response);
    }

    public Task SetAnalogKeyRapidTriggersAsync(
        RazerHidDeviceConnection connection,
        byte dataType,
        byte classId,
        IReadOnlyList<RazerAnalogRapidTrigger> settings,
        CancellationToken cancellationToken = default)
    {
        RequireReady(connection);
        return ExecuteWithoutResultAsync(connection,
            new RazerHidRequest("razer_analog_set_rapid_triggers",
                RazerAnalogKeyProtocol.CreateSetMultiAnalogKeyRapidTriggerRequest(
                    MiddlewareTransactionId, dataType, classId, settings)),
            cancellationToken);
    }

    public async Task<RazerAnalogKeyContinuousRapidTrigger> GetAnalogKeyContinuousRapidTriggerAsync(
        RazerHidDeviceConnection connection,
        byte classId,
        CancellationToken cancellationToken = default)
    {
        RequireReady(connection);
        var request = RazerAnalogKeyProtocol.CreateGetAnalogKeyContinuousRapidTriggerRequest(
            MiddlewareTransactionId, classId);
        var response = await ExecuteAsync(connection,
            new RazerHidRequest("razer_analog_get_continuous_rapid_trigger", request),
            cancellationToken).ConfigureAwait(false);
        return RazerAnalogKeyProtocol.ParseAnalogKeyContinuousRapidTrigger(response);
    }

    public Task SetAnalogKeyContinuousRapidTriggerAsync(
        RazerHidDeviceConnection connection,
        byte classId,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        RequireReady(connection);
        return ExecuteWithoutResultAsync(connection,
            new RazerHidRequest("razer_analog_set_continuous_rapid_trigger",
                RazerAnalogKeyProtocol.CreateSetAnalogKeyContinuousRapidTriggerRequest(
                    MiddlewareTransactionId, classId, enabled)),
            cancellationToken);
    }

    public async Task<RazerAnalogKeyModTapTable> GetAnalogKeyModTapAssignmentsAsync(
        RazerHidDeviceConnection connection,
        byte classId,
        IReadOnlyList<byte> keyIds,
        CancellationToken cancellationToken = default)
    {
        RequireReady(connection);
        var request = RazerAnalogKeyProtocol.CreateGetMultiModTapKeyAssignmentRequest(
            MiddlewareTransactionId, classId, keyIds);
        var response = await ExecuteAsync(connection,
            new RazerHidRequest("razer_analog_get_mod_tap_assignments", request),
            cancellationToken).ConfigureAwait(false);
        return RazerAnalogKeyProtocol.ParseMultiModTapKeyAssignments(response);
    }

    public Task SetAnalogKeyModTapAssignmentsAsync(
        RazerHidDeviceConnection connection,
        byte classId,
        IReadOnlyList<RazerAnalogModTapAssignment> assignments,
        CancellationToken cancellationToken = default)
    {
        RequireReady(connection);
        return ExecuteWithoutResultAsync(connection,
            new RazerHidRequest("razer_analog_set_mod_tap_assignments",
                RazerAnalogKeyProtocol.CreateSetMultiModTapKeyAssignmentRequest(
                    MiddlewareTransactionId, classId, assignments)),
            cancellationToken);
    }

    public async Task<RazerAnalogKeyAssignmentTable> GetAnalogKeyAssignmentsAsync(
        RazerHidDeviceConnection connection,
        byte classId,
        IReadOnlyList<RazerAnalogKeyAssignmentSelector> selectors,
        CancellationToken cancellationToken = default)
    {
        RequireReady(connection);
        var request = RazerAnalogKeyProtocol.CreateGetMultiKeyAssignmentRequest(
            MiddlewareTransactionId, classId, selectors);
        var response = await ExecuteAsync(connection,
            new RazerHidRequest("razer_analog_get_key_assignments", request),
            cancellationToken).ConfigureAwait(false);
        return RazerAnalogKeyProtocol.ParseMultiKeyAssignments(response);
    }

    public Task SetAnalogKeyAssignmentsAsync(
        RazerHidDeviceConnection connection,
        byte classId,
        IReadOnlyList<RazerAnalogKeyAssignment> assignments,
        CancellationToken cancellationToken = default)
    {
        RequireReady(connection);
        return ExecuteWithoutResultAsync(connection,
            new RazerHidRequest("razer_analog_set_key_assignments",
                RazerAnalogKeyProtocol.CreateSetMultiKeyAssignmentRequest(
                    MiddlewareTransactionId, classId, assignments)),
            cancellationToken);
    }

    public async Task<RazerAnalogKeyBottomDeadZone> GetAnalogKeyBottomDeadZoneAsync(
        RazerHidDeviceConnection connection,
        byte classId,
        CancellationToken cancellationToken = default)
    {
        RequireReady(connection);
        var request = RazerAnalogKeyProtocol.CreateGetAnalogKeyBottomDeadZoneRequest(
            MiddlewareTransactionId, classId);
        var response = await ExecuteAsync(connection,
            new RazerHidRequest("razer_analog_get_bottom_dead_zone", request),
            cancellationToken).ConfigureAwait(false);
        return RazerAnalogKeyProtocol.ParseAnalogKeyBottomDeadZone(response);
    }

    public Task SetAnalogKeyBottomDeadZoneAsync(
        RazerHidDeviceConnection connection,
        byte classId,
        byte bottomDeadZone,
        CancellationToken cancellationToken = default)
    {
        RequireReady(connection);
        return ExecuteWithoutResultAsync(connection,
            new RazerHidRequest("razer_analog_set_bottom_dead_zone",
                RazerAnalogKeyProtocol.CreateSetAnalogKeyBottomDeadZoneRequest(
                    MiddlewareTransactionId, classId, bottomDeadZone)),
            cancellationToken);
    }

    public async Task<byte> GetAnalogReportModeAsync(
        RazerHidDeviceConnection connection,
        CancellationToken cancellationToken = default)
    {
        RequireReady(connection);
        var request = RazerAnalogKeyProtocol.CreateGetAnalogReportModeRequest(MiddlewareTransactionId);
        var response = await ExecuteAsync(connection,
            new RazerHidRequest("razer_analog_get_report_mode", request),
            cancellationToken).ConfigureAwait(false);
        return RazerAnalogKeyProtocol.ParseAnalogReportMode(response);
    }

    public Task SetAnalogReportModeAsync(
        RazerHidDeviceConnection connection,
        byte reportMode,
        CancellationToken cancellationToken = default)
    {
        RequireReady(connection);
        return ExecuteWithoutResultAsync(connection,
            new RazerHidRequest("razer_analog_set_report_mode",
                RazerAnalogKeyProtocol.CreateSetAnalogReportModeRequest(
                    MiddlewareTransactionId, reportMode)),
            cancellationToken);
    }

    public async Task<RazerAnalogKeyKillSwitch> GetKeyboardKillSwitchAsync(
        RazerHidDeviceConnection connection,
        byte classId,
        CancellationToken cancellationToken = default)
    {
        RequireReady(connection);
        var request = RazerAnalogKeyProtocol.CreateGetKeyboardKillSwitchRequest(
            MiddlewareTransactionId, classId);
        var response = await ExecuteAsync(connection,
            new RazerHidRequest("razer_keyboard_get_kill_switch", request),
            cancellationToken).ConfigureAwait(false);
        return RazerAnalogKeyProtocol.ParseKeyboardKillSwitch(response);
    }

    public Task SetKeyboardKillSwitchAsync(
        RazerHidDeviceConnection connection,
        byte classId,
        bool enabled,
        byte keyId1,
        byte keyId2,
        CancellationToken cancellationToken = default)
    {
        RequireReady(connection);
        return ExecuteWithoutResultAsync(connection,
            new RazerHidRequest("razer_keyboard_set_kill_switch",
                RazerAnalogKeyProtocol.CreateSetKeyboardKillSwitchRequest(
                    MiddlewareTransactionId, classId, enabled, keyId1, keyId2)),
            cancellationToken);
    }

    public async Task<RazerAnalogSnapTapState> GetAnalogSnapTapAsync(
        RazerHidDeviceConnection connection,
        byte profileId,
        CancellationToken cancellationToken = default)
    {
        RequireReady(connection);
        var request = RazerAnalogKeyProtocol.CreateGetAnalogSnapTapRequest(
            MiddlewareTransactionId, profileId);
        var response = await ExecuteAsync(connection,
            new RazerHidRequest("razer_keyboard_get_snap_tap", request),
            cancellationToken).ConfigureAwait(false);
        return RazerAnalogKeyProtocol.ParseAnalogSnapTap(response);
    }

    public Task SetAnalogSnapTapAsync(
        RazerHidDeviceConnection connection,
        byte profileId,
        bool enabled,
        IReadOnlyList<RazerAnalogSnapTapPair> pairs,
        CancellationToken cancellationToken = default)
    {
        RequireReady(connection);
        return ExecuteWithoutResultAsync(connection,
            new RazerHidRequest("razer_keyboard_set_snap_tap",
                RazerAnalogKeyProtocol.CreateSetAnalogSnapTapRequest(
                    MiddlewareTransactionId, profileId, enabled, pairs)),
            cancellationToken);
    }
    internal async Task ProbeKnobAsync(
        RazerHidDeviceDefinition definition,
        string devicePath,
        ISet<RazerHidBackendCapability> result,
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
            result.Add(RazerHidBackendCapability.KnobRead);
            result.Add(RazerHidBackendCapability.KnobWrite);
        }
    }

    internal async Task ProbeAnalogKeyAsync(
        RazerHidDeviceDefinition definition,
        string devicePath,
        ISet<RazerHidBackendCapability> result,
        CancellationToken cancellationToken)
    {
        if (definition.Category is not DeviceCategory.Keyboard)
        {
            return;
        }

        // Probe the core per-key actuation GET, not report mode: report mode is an
        // 8KHz-era command that some analog keyboards do not answer, and a successful
        // reply with an empty payload still means the firmware understands analog keys.
        var request = RazerAnalogKeyProtocol.CreateGetMultiAnalogKeyActuationPointRequest(
            MiddlewareTransactionId, dataType: 0x00, classId: 0x01, keyIds: [0x1F]);
        if (await ProbeMiddlewareAsync(definition.ProductId, devicePath, definition.DeviceWait,
                "keyboard.analogKey", request, _ => true,
                cancellationToken).ConfigureAwait(false))
        {
            result.Add(RazerHidBackendCapability.AnalogKeyRead);
            result.Add(RazerHidBackendCapability.AnalogKeyWrite);
        }
    }
}
