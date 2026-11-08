using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerGamepadViewModel
{
    [ProfileSetting("gamepad.stick-deadzone", DeviceCategory.Gamepad)]
    public async Task ApplyStickAsync(
        RazerGamepadStickRowViewModel row,
        CancellationToken cancellationToken = default)
    {
        if (!BeginOperation()) return;
        var previousState = await ReadStateOrDefaultAsync(cancellationToken);
        var previous = previousState?.Sticks.FirstOrDefault(
            stick => stick.ControlId == row.ControlId);
        try
        {
            if (row.HasSensitivity)
            {
                await _service.SetSensitivityAsync(
                    row.ControlId,
                    (byte)Math.Round(row.Sensitivity),
                    cancellationToken);
            }

            if (row.HasZones)
            {
                await _service.SetZonesAsync(
                    row.ControlId,
                    new RazerGamepadZones(
                        (ushort)Math.Round(row.DeadZone),
                        row.ZoneSoftBumper,
                        row.ZoneDeltaThreshold,
                        row.PreventDoubleDeadZone),
                    cancellationToken);
            }

            if (row.HasCircularity)
            {
                await _service.SetCircularityAsync(
                    row.ControlId,
                    row.SelectedCircularityIndex == 1
                        ? RazerGamepadCircularity.Circular
                        : RazerGamepadCircularity.Standard,
                    cancellationToken);
            }

            if (row.HasZones &&
                !await SaveProfileAsync(profile =>
                {
                    if (row.ControlId == 1)
                    {
                        profile.LeftStickDeadzone = Math.Round(row.DeadZone);
                    }
                    else if (row.ControlId == 2)
                    {
                        profile.RightStickDeadzone = Math.Round(row.DeadZone);
                    }
                }, cancellationToken))
            {
                RestoreStickUi(row, previous);
                await RestoreStickAsync(row.ControlId, previous);
                ErrorText = AppStrings.Text("RazerHidSettingProfileSaveFailed");
                OperationStatusText = string.Empty;
                return;
            }

            ErrorText = string.Empty;
            OperationStatusText = AppStrings.Text("BlackSharkSaved");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            ErrorText = exception.Message;
            OperationStatusText = string.Empty;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [ProfileSetting("gamepad.trigger-mode", DeviceCategory.Gamepad)]
    public async Task ApplyTriggerAsync(
        RazerGamepadTriggerRowViewModel row,
        CancellationToken cancellationToken = default)
    {
        if (!BeginOperation()) return;
        var previousState = await ReadStateOrDefaultAsync(cancellationToken);
        var previous = previousState?.Triggers.FirstOrDefault(
            trigger => trigger.ControlId == row.ControlId);
        try
        {
            var mode = row.SelectedModeIndex == 1
                ? RazerGamepadTriggerMode.Digital
                : RazerGamepadTriggerMode.Analog;
            if (row.HasMode)
            {
                await _service.SetTriggerModeAsync(row.ControlId, mode, cancellationToken);
            }

            if (mode == RazerGamepadTriggerMode.Analog && row.HasRange)
            {
                await _service.SetTriggerRangeAsync(
                    row.ControlId,
                    (byte)Math.Round(row.StartRange),
                    (byte)Math.Round(row.EndRange),
                    cancellationToken);
            }
            else if (mode == RazerGamepadTriggerMode.Digital && row.HasDigital)
            {
                await _service.SetDigitalTriggerAsync(
                    row.ControlId,
                    new RazerGamepadDigitalTrigger(
                        (byte)row.SelectedActuationIndex,
                        row.RapidTrigger),
                    cancellationToken);
            }

            if (row.HasMode &&
                !await SaveProfileAsync(profile =>
                    profile.TriggerMode = mode == RazerGamepadTriggerMode.Digital
                        ? "Digital"
                        : "Analog", cancellationToken))
            {
                RestoreTriggerUi(row, previous);
                await RestoreTriggerAsync(row.ControlId, previous);
                ErrorText = AppStrings.Text("RazerHidSettingProfileSaveFailed");
                OperationStatusText = string.Empty;
                return;
            }

            ErrorText = string.Empty;
            OperationStatusText = AppStrings.Text("BlackSharkSaved");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            ErrorText = exception.Message;
            OperationStatusText = string.Empty;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [ProfileSetting("gamepad.dpad", DeviceCategory.Gamepad)]
    public async Task ApplyDpadAsync(CancellationToken cancellationToken = default)
    {
        if (!BeginOperation()) return;
        var previous = await ReadStateOrDefaultAsync(cancellationToken);
        try
        {
            if (_hasIgnoreDPad)
            {
                await _service.SetIgnoreDPadDiagonalsAsync(IgnoreDPadDiagonals, cancellationToken);
            }

            if (_hasSocd)
            {
                await _service.SetSocdAsync((RazerGamepadSocdMode)SocdIndex, cancellationToken);
            }

            if (!await SaveProfileAsync(profile =>
                {
                    if (_hasIgnoreDPad)
                    {
                        profile.IgnoreDpadDiagonals = IgnoreDPadDiagonals;
                    }

                    if (_hasSocd)
                    {
                        profile.SocdModeIndex = SocdIndex;
                    }
                }, cancellationToken))
            {
                if (previous?.IgnoreDPadDiagonals is { } ignore)
                {
                    IgnoreDPadDiagonals = ignore;
                }

                if (previous?.SocdMode is { } socd)
                {
                    SocdIndex = (int)socd;
                }

                await RestoreDpadAsync(previous);
                ErrorText = AppStrings.Text("RazerHidSettingProfileSaveFailed");
                OperationStatusText = string.Empty;
                return;
            }

            ErrorText = string.Empty;
            OperationStatusText = AppStrings.Text("BlackSharkSaved");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            ErrorText = exception.Message;
            OperationStatusText = string.Empty;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [ProfileSetting("gamepad.vibration", DeviceCategory.Gamepad)]
    public async Task ApplyVibrationAsync(CancellationToken cancellationToken = default)
    {
        if (!BeginOperation()) return;
        var previous = await ReadStateOrDefaultAsync(cancellationToken);
        try
        {
            var scale = RazerGamepadProtocol.ToVibrationScale((byte)VibrationIndex);
            await _service.SetVibrationScaleAsync(scale, cancellationToken);
            if (!await SaveProfileAsync(
                    profile => profile.VibrationStrengthPercent = scale,
                    cancellationToken))
            {
                if (previous?.VibrationScale is { } previousScale)
                {
                    VibrationIndex = RazerGamepadProtocol.ToVibrationIndex(previousScale);
                    try
                    {
                        await _service.SetVibrationScaleAsync(previousScale, CancellationToken.None);
                    }
                    catch (Exception exception) when (IsExpectedException(exception))
                    {
                        // The save failure is what the caller must see; rollback is best effort.
                    }
                }

                ErrorText = AppStrings.Text("RazerHidSettingProfileSaveFailed");
                OperationStatusText = string.Empty;
                return;
            }

            ErrorText = string.Empty;
            OperationStatusText = AppStrings.Text("BlackSharkSaved");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            ErrorText = exception.Message;
            OperationStatusText = string.Empty;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
