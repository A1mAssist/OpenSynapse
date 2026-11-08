using System.Globalization;
using System.Text.Json;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Core.Sensors;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerGamepadViewModel
{
    private string ProfileKey => $"1532:{_connection.ProductId:X4}";

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
            ProfileKey,
            Name,
            0x1532,
            _connection.ProductId,
            DeviceAccessState.Available,
            DeviceCapabilityState.PendingValidation,
            91,
            1,
            2,
            "gamepad",
            DeviceCategory.Gamepad);
        var applier = new GamepadProfileApplier(
            (_, _) => Task.FromResult<RazerGamepadService?>(_service));
        await applier.ApplyAsync(
            new DeviceProfileApplyContext(
                document,
                descriptor,
                powerSource,
                ProfileResolver.Resolve(document, descriptor, powerSource)),
            cancellationToken);
    }

    private Task<bool> SaveButtonAssignmentProfileAsync(
        RazerGamepadButtonAssignment assignment,
        CancellationToken cancellationToken) =>
        SaveProfileAsync(profile =>
        {
            profile.ButtonAssignments ??= [];
            var replacement = new GamepadButtonAssignmentProfile
            {
                ButtonId = assignment.ButtonId,
                Mode = (byte)assignment.Mode,
                Function = (byte)assignment.Function,
                Data = assignment.Data.Take(assignment.DataSize).ToList(),
            };
            var index = profile.ButtonAssignments.FindIndex(item =>
                item.ButtonId == assignment.ButtonId &&
                item.Mode == (byte)assignment.Mode);
            if (index >= 0)
            {
                profile.ButtonAssignments[index] = replacement;
            }
            else
            {
                profile.ButtonAssignments.Add(replacement);
            }
        }, cancellationToken);

    /// <summary>
    /// Saves one gamepad entry. A failed save restores the whole entry so the
    /// in-memory profile cannot disagree with the hardware rollback.
    /// </summary>
    private async Task<bool> SaveProfileAsync(
        Action<GamepadProfileSettings> mutate,
        CancellationToken cancellationToken)
    {
        var document = _getProfile?.Invoke();
        var save = _saveProfile;
        if (document is null || save is null)
        {
            return true;
        }

        var created = !document.Devices.TryGetValue(ProfileKey, out var device);
        device ??= new DeviceProfileSettings();
        device.Gamepad ??= new GamepadProfileSettings();
        var previous = JsonSerializer.SerializeToNode(device);
        mutate(device.Gamepad);
        if (created)
        {
            document.Devices[ProfileKey] = device;
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
                document.Devices.Remove(ProfileKey);
                return;
            }

            document.Devices[ProfileKey] =
                previous?.Deserialize<DeviceProfileSettings>() ?? new DeviceProfileSettings();
        }
    }

    private static Dictionary<string, string> CreateLightingParameters(
        byte regionId,
        byte effectId,
        byte flags,
        byte rate,
        byte brightness,
        RazerRgb? color)
    {
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["regionId"] = regionId.ToString(CultureInfo.InvariantCulture),
            ["effectId"] = effectId.ToString(CultureInfo.InvariantCulture),
            ["flags"] = flags.ToString(CultureInfo.InvariantCulture),
            ["rate"] = rate.ToString(CultureInfo.InvariantCulture),
            ["brightness"] = brightness.ToString(CultureInfo.InvariantCulture),
        };
        if (color is { } rgb)
        {
            parameters["color"] = $"{rgb.Red},{rgb.Green},{rgb.Blue}";
        }

        return parameters;
    }

    private async Task<RazerGamepadState?> ReadStateOrDefaultAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _service.ReadStateAsync(cancellationToken);
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            return null;
        }
    }

    private async Task RestoreGamepadLightingAsync(
        RazerGamepadLightingState? state,
        byte regionId)
    {
        var region = state?.Regions.FirstOrDefault(
            item => item.Region.RegionId == regionId);
        if (region is null)
        {
            return;
        }

        try
        {
            if (region.Effect is { } effect)
            {
                await _service.SetLightingEffectAsync(
                    regionId,
                    effect.EffectId,
                    effect.Flags,
                    effect.Rate,
                    effect.Colors.ToArray(),
                    CancellationToken.None);
            }

            if (region.Brightness is { } brightness)
            {
                await _service.SetLightingBrightnessAsync(regionId, brightness, CancellationToken.None);
            }
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            // The save failure is what the caller must see; rollback is best effort.
        }
    }

    private async Task RestorePollingProfileAsync(RazerGamepadPollingProfileState? state)
    {
        if (state is null)
        {
            return;
        }

        try
        {
            if (state.ActiveProfileId is { } profileId)
            {
                await _service.SetActiveProfileAsync(profileId, CancellationToken.None);
            }

            if (state.PollingRateHertz is { } hertz)
            {
                await _service.SetPollingRateAsync(hertz, CancellationToken.None);
            }
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            // The save failure is what the caller must see; rollback is best effort.
        }

        try
        {
            await LoadPollingProfileCoreAsync(CancellationToken.None);
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            // Keeping the save error visible matters more than a refresh failure.
        }
    }

    private async Task RestoreDpadAsync(RazerGamepadState? state)
    {
        if (state is null)
        {
            return;
        }

        try
        {
            if (state.IgnoreDPadDiagonals is { } ignore)
            {
                await _service.SetIgnoreDPadDiagonalsAsync(ignore, CancellationToken.None);
            }

            if (state.SocdMode is { } socd)
            {
                await _service.SetSocdAsync(socd, CancellationToken.None);
            }
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            // The save failure is what the caller must see; rollback is best effort.
        }
    }

    private static void RestoreStickUi(
        RazerGamepadStickRowViewModel row,
        RazerGamepadStickState? state)
    {
        if (state is null)
        {
            return;
        }

        if (state.Sensitivity is { } sensitivity)
        {
            row.Sensitivity = sensitivity;
        }

        if (state.Zones is { } zones)
        {
            row.DeadZone = zones.DeadZone;
            row.PreventDoubleDeadZone = zones.PreventDoubleDeadZone;
        }

        if (state.Circularity is { } circularity)
        {
            row.SelectedCircularityIndex =
                circularity == RazerGamepadCircularity.Circular ? 1 : 0;
        }
    }

    private async Task RestoreStickAsync(byte controlId, RazerGamepadStickState? state)
    {
        if (state is null)
        {
            return;
        }

        try
        {
            if (state.Sensitivity is { } sensitivity)
            {
                await _service.SetSensitivityAsync(controlId, sensitivity, CancellationToken.None);
            }

            if (state.Zones is { } zones)
            {
                await _service.SetZonesAsync(controlId, zones, CancellationToken.None);
            }

            if (state.Circularity is { } circularity)
            {
                await _service.SetCircularityAsync(controlId, circularity, CancellationToken.None);
            }
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            // The save failure is what the caller must see; rollback is best effort.
        }
    }

    private static void RestoreTriggerUi(
        RazerGamepadTriggerRowViewModel row,
        RazerGamepadTriggerState? state)
    {
        if (state is null)
        {
            return;
        }

        if (state.Mode is { } mode)
        {
            row.SelectedModeIndex = mode == RazerGamepadTriggerMode.Digital ? 1 : 0;
        }

        if (state.StartRange is { } start)
        {
            row.StartRange = start;
        }

        if (state.EndRange is { } end)
        {
            row.EndRange = end;
        }

        if (state.Digital is { } digital)
        {
            row.SelectedActuationIndex = digital.ActuationPoint;
            row.RapidTrigger = digital.IsRapidTrigger;
        }
    }

    private async Task RestoreTriggerAsync(byte controlId, RazerGamepadTriggerState? state)
    {
        if (state is null)
        {
            return;
        }

        try
        {
            if (state.Mode is { } mode)
            {
                await _service.SetTriggerModeAsync(controlId, mode, CancellationToken.None);
            }

            if (state.StartRange is { } start && state.EndRange is { } end)
            {
                await _service.SetTriggerRangeAsync(controlId, start, end, CancellationToken.None);
            }

            if (state.Digital is { } digital)
            {
                await _service.SetDigitalTriggerAsync(controlId, digital, CancellationToken.None);
            }
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            // The save failure is what the caller must see; rollback is best effort.
        }
    }
}
