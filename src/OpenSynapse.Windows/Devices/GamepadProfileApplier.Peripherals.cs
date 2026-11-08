using OpenSynapse.Core.Profiles;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed partial class GamepadProfileApplier
{
    private static async Task ApplySticksAsync(
        GamepadProfileSettings profile,
        RazerGamepadService service,
        RazerGamepadState state,
        List<ProfileSettingApplyResult> results,
        CancellationToken cancellationToken)
    {
        await ApplyStickAsync(
            1,
            "left",
            profile.LeftStickDeadzone,
            profile.LeftStickSensitivity,
            profile.LeftStickCircularity,
            profile.LeftStickPreventDoubleDeadzone,
            service,
            state,
            results,
            cancellationToken);
        await ApplyStickAsync(
            2,
            "right",
            profile.RightStickDeadzone,
            profile.RightStickSensitivity,
            profile.RightStickCircularity,
            profile.RightStickPreventDoubleDeadzone,
            service,
            state,
            results,
            cancellationToken);
    }

    private static async Task ApplyStickAsync(
        byte controlId,
        string label,
        double? deadzone,
        int? sensitivity,
        string? circularity,
        bool? preventDoubleDeadzone,
        RazerGamepadService service,
        RazerGamepadState state,
        List<ProfileSettingApplyResult> results,
        CancellationToken cancellationToken)
    {
        var stick = state.Sticks.FirstOrDefault(item => item.ControlId == controlId);
        if (sensitivity is { } sensitivityPercent)
        {
            var key = $"gamepad.{label}-stick-sensitivity";
            if (stick?.Sensitivity is not { } current)
            {
                results.Add(new(
                    key,
                    ProfileSettingApplyStatus.Skipped,
                    "The device did not report stick sensitivity."));
            }
            else
            {
                try
                {
                    var desired = (byte)Math.Clamp(sensitivityPercent, 0, 100);
                    if (current == desired)
                    {
                        results.Add(new(key, ProfileSettingApplyStatus.Skipped));
                    }
                    else
                    {
                        await service.SetSensitivityAsync(controlId, desired, cancellationToken);
                        results.Add(new(key, ProfileSettingApplyStatus.Applied));
                    }
                }
                catch (Exception exception) when (IsExpected(exception))
                {
                    results.Add(new(key, ProfileSettingApplyStatus.Failed, exception.Message));
                }
            }
        }

        if (deadzone is not null || preventDoubleDeadzone is not null)
        {
            var deadzoneKey = $"gamepad.{label}-stick-deadzone";
            var preventKey = $"gamepad.{label}-stick-prevent-double-deadzone";
            void Add(bool requested, string key, ProfileSettingApplyStatus status, string? error = null)
            {
                if (requested)
                {
                    results.Add(new(key, status, error));
                }
            }

            if (stick?.Zones is not { } zones)
            {
                Add(deadzone is not null, deadzoneKey, ProfileSettingApplyStatus.Skipped,
                    "The device did not report stick zones.");
                Add(preventDoubleDeadzone is not null, preventKey, ProfileSettingApplyStatus.Skipped,
                    "The device did not report stick zones.");
                return;
            }

            var desiredDeadzone = deadzone is { } value
                ? (ushort)Math.Round(value)
                : zones.DeadZone;
            var desiredPrevent = preventDoubleDeadzone ?? zones.PreventDoubleDeadZone;
            if (zones.DeadZone == desiredDeadzone && zones.PreventDoubleDeadZone == desiredPrevent)
            {
                Add(deadzone is not null, deadzoneKey, ProfileSettingApplyStatus.Skipped);
                Add(preventDoubleDeadzone is not null, preventKey, ProfileSettingApplyStatus.Skipped);
                return;
            }

            try
            {
                await service.SetZonesAsync(
                    controlId,
                    new RazerGamepadZones(
                        desiredDeadzone,
                        zones.SoftBumper,
                        zones.DeltaThreshold,
                        desiredPrevent),
                    cancellationToken);
                Add(deadzone is not null, deadzoneKey, ProfileSettingApplyStatus.Applied);
                Add(preventDoubleDeadzone is not null, preventKey, ProfileSettingApplyStatus.Applied);
            }
            catch (Exception exception) when (IsExpected(exception))
            {
                Add(deadzone is not null, deadzoneKey, ProfileSettingApplyStatus.Failed, exception.Message);
                Add(preventDoubleDeadzone is not null, preventKey, ProfileSettingApplyStatus.Failed,
                    exception.Message);
            }
        }

        if (string.IsNullOrWhiteSpace(circularity))
        {
            return;
        }

        var circularityKey = $"gamepad.{label}-stick-circularity";
        if (!TryParseCircularity(circularity, out var desiredCircularity))
        {
            results.Add(new(
                circularityKey,
                ProfileSettingApplyStatus.Failed,
                $"Stick circularity '{circularity}' is not recognized."));
            return;
        }

        if (stick?.Circularity is not { } currentCircularity)
        {
            results.Add(new(
                circularityKey,
                ProfileSettingApplyStatus.Skipped,
                "The device did not report stick circularity."));
            return;
        }

        try
        {
            if (currentCircularity == desiredCircularity)
            {
                results.Add(new(circularityKey, ProfileSettingApplyStatus.Skipped));
                return;
            }

            await service.SetCircularityAsync(controlId, desiredCircularity, cancellationToken);
            results.Add(new(circularityKey, ProfileSettingApplyStatus.Applied));
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            results.Add(new(circularityKey, ProfileSettingApplyStatus.Failed, exception.Message));
        }
    }

    private static async Task ApplyButtonAssignmentsAsync(
        GamepadProfileSettings profile,
        RazerGamepadService service,
        List<ProfileSettingApplyResult> results,
        CancellationToken cancellationToken)
    {
        if (profile.ButtonAssignments is not { Count: > 0 } assignments)
        {
            return;
        }

        foreach (var desired in assignments)
        {
            var key = $"gamepad.button.{desired.ButtonId}.{desired.Mode}";
            var data = new byte[5];
            if (desired.Data is { } source)
            {
                source.Take(data.Length).ToArray().CopyTo(data, 0);
            }

            var function = new RazerGamepadFunctionData(
                (RazerGamepadFunction)desired.Function,
                (byte)Math.Min(desired.Data?.Count ?? 0, data.Length),
                data);
            try
            {
                // The service reads the previous block, skips an identical value
                // and verifies the readback after every write.
                await service.SetButtonAssignmentAsync(
                    desired.ButtonId,
                    (RazerGamepadButtonMode)desired.Mode,
                    function,
                    cancellationToken);
                results.Add(new(key, ProfileSettingApplyStatus.Applied));
            }
            catch (Exception exception) when (IsExpected(exception))
            {
                results.Add(new(key, ProfileSettingApplyStatus.Failed, exception.Message));
            }
        }
    }

    private static bool TryParseCircularity(
        string text,
        out RazerGamepadCircularity circularity) =>
        Enum.TryParse(text, true, out circularity) && Enum.IsDefined(circularity);
}
