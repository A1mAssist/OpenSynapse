using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHidDeviceViewModel
{
    private static readonly byte[] AnalogJoystickKeyIds =
    [
        18, 31, 32, 33, 17, 19, 20, 26, 21, 35,
        61, 44, 58, 60, 16, 2, 3, 4, 5,
    ];

    private readonly ObservableCollection<AnalogJoystickKeyViewModel> _analogJoystickRows = [];
    private readonly IReadOnlyList<AnalogJoystickButtonChoice> _analogJoystickChoices =
        CreateAnalogJoystickChoices();
    private bool _isAnalogJoystickBusy;
    private bool _analogJoystickModeKnown;
    private byte _analogJoystickReportMode;
    private byte? _analogJoystickMappingMode;
    private bool _analogJoystickEnabled;
    private string _analogJoystickStatusText = AppStrings.Text("GamepadRemapReadFailed");

    public Visibility AnalogJoystickVisibility => VisibleWhen(HasAny(
        RazerHidBackendCapability.AnalogJoystickRead,
        RazerHidBackendCapability.AnalogJoystickWrite));
    public Visibility AnalogJoystickWriteVisibility =>
        VisibleWhen(Has(RazerHidBackendCapability.AnalogJoystickWrite));
    public IReadOnlyList<AnalogJoystickKeyViewModel> AnalogJoystickRows => _analogJoystickRows;

    public bool IsAnalogJoystickBusy
    {
        get => _isAnalogJoystickBusy;
        private set
        {
            if (!SetField(ref _isAnalogJoystickBusy, value))
            {
                return;
            }

            OnPropertyChanged(nameof(CanEditAnalogJoystick));
            OnPropertyChanged(nameof(CanApplyAnalogJoystick));
        }
    }

    public bool AnalogJoystickEnabled
    {
        get => _analogJoystickEnabled;
        set
        {
            if (SetField(ref _analogJoystickEnabled, value))
            {
                OnPropertyChanged(nameof(CanApplyAnalogJoystick));
            }
        }
    }

    public bool CanEditAnalogJoystick =>
        CanUse(RazerHidBackendCapability.AnalogJoystickWrite) &&
        _analogJoystickModeKnown &&
        _analogJoystickReportMode is 0 or 1 &&
        !IsAnalogJoystickBusy;
    public bool CanApplyAnalogJoystick => CanEditAnalogJoystick;
    internal bool AnalogJoystickActive => _analogJoystickModeKnown && _analogJoystickReportMode == 1;

    public string AnalogJoystickStatusText
    {
        get => _analogJoystickStatusText;
        private set => SetField(ref _analogJoystickStatusText, value);
    }

    public async Task LoadAnalogJoystickAsync(CancellationToken cancellationToken = default)
    {
        if (!CanUse(RazerHidBackendCapability.AnalogJoystickRead) || IsAnalogJoystickBusy)
        {
            return;
        }

        IsAnalogJoystickBusy = true;
        try
        {
            _analogJoystickReportMode = await _service
                .GetAnalogJoystickReportModeAsync(Connection, cancellationToken)
                .ConfigureAwait(true);
            _analogJoystickModeKnown = true;
            _analogJoystickEnabled = _analogJoystickReportMode == 1;
            InitializeAnalogJoystickRows();

            try
            {
                var profileId = await ResolveAnalogKeyProfileIdAsync(cancellationToken)
                    .ConfigureAwait(true);
                await ReadAnalogJoystickMappingModeAsync(profileId, cancellationToken)
                    .ConfigureAwait(true);
                await ReadAnalogJoystickRowsAsync(profileId, cancellationToken).ConfigureAwait(true);
            }
            catch (Exception exception) when (IsAnalogKeyReadbackFailure(exception))
            {
                // Mapping mode and the table are advisory; the mode toggle still works.
            }

            if (_analogJoystickReportMode == 0)
            {
                AnalogJoystickRecoveryStore.Shared.Clear(InstanceId);
            }

            UpdateAnalogJoystickStatus();
            OnPropertyChanged(nameof(AnalogJoystickEnabled));
            NotifyAnalogJoystickChanged();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            // Without a readable mode there is no safe write target; hide the editor.
            _unsupported.Add(RazerHidBackendCapability.AnalogJoystickWrite);
            HandleOptionalFailure(exception, RazerHidBackendCapability.AnalogJoystickRead);
            AnalogJoystickStatusText = LocalizeProtocolError(
                exception, RazerHidBackendCapability.AnalogJoystickRead);
            NotifyAnalogJoystickChanged();
        }
        finally
        {
            IsAnalogJoystickBusy = false;
        }
    }

    [ProfileSetting("keyboard.gamepad-remap", DeviceCategory.Keyboard, powerScoped: true)]
    public Task ApplyAnalogJoystickAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(RazerHidBackendCapability.AnalogJoystickWrite,
            () => IsAnalogJoystickBusy, value => IsAnalogJoystickBusy = value,
            async () =>
            {
                if (!_analogJoystickModeKnown || _analogJoystickReportMode is not (0 or 1))
                {
                    return;
                }

                var requested = AnalogJoystickEnabled;
                var previousEnabled = _analogJoystickReportMode == 1;
                var previousProfile = ReadProfile(settings => settings.Keyboard.GamepadRemapEnabled);
                var previousMappings = ReadProfile(settings => settings.Keyboard.ButtonMappings?
                    .Select(mapping => mapping.Clone()).ToList());
                var previousRows = _analogJoystickRows
                    .Select(row => (Row: row, ButtonId: row.LoadedButtonId))
                    .ToArray();
                var changed = _analogJoystickRows
                    .Where(row => row.SelectedChoice.ButtonId != row.LoadedButtonId)
                    .ToArray();
                var profileId = await ResolveAnalogKeyProfileIdAsync(cancellationToken)
                    .ConfigureAwait(true);

                if (!requested)
                {
                    try
                    {
                        await _service.SetAnalogJoystickReportModeAsync(
                            Connection, 0, cancellationToken).ConfigureAwait(true);
                    }
                    catch
                    {
                        await RestoreAnalogJoystickUiAfterFailureAsync(previousRows)
                            .ConfigureAwait(true);
                        throw;
                    }
                }

                var applied = new List<AnalogJoystickKeyViewModel>();
                try
                {
                    foreach (var row in changed)
                    {
                        applied.Add(row);
                        await WriteAnalogJoystickRowAsync(
                            row, row.SelectedChoice.ButtonId, cancellationToken).ConfigureAwait(true);
                    }
                }
                catch
                {
                    await RollbackAnalogJoystickRowsAsync(applied, previousRows)
                        .ConfigureAwait(true);
                    await RestoreAnalogJoystickUiAfterFailureAsync(previousRows)
                        .ConfigureAwait(true);
                    throw;
                }

                if (requested)
                {
                    if (!previousEnabled)
                    {
                        AnalogJoystickRecoveryStore.Shared.Save(new AnalogJoystickRecoveryEntry(
                            InstanceId,
                            _analogJoystickReportMode,
                            _analogJoystickMappingMode ?? 0,
                            DateTimeOffset.UtcNow));
                    }

                    try
                    {
                        await _service.SetAnalogJoystickReportModeAsync(Connection, 1, cancellationToken)
                            .ConfigureAwait(true);
                    }
                    catch
                    {
                        await RollbackAnalogJoystickRowsAsync(applied, previousRows)
                            .ConfigureAwait(true);
                        AnalogJoystickRecoveryStore.Shared.Clear(InstanceId);
                        await RestoreAnalogJoystickUiAfterFailureAsync(previousRows)
                            .ConfigureAwait(true);
                        throw;
                    }
                }
                else
                {
                    AnalogJoystickRecoveryStore.Shared.Clear(InstanceId);
                }

                try
                {
                    await SaveProfileAsync(
                        settings =>
                        {
                            settings.Keyboard.GamepadRemapEnabled = requested;
                            PersistAnalogJoystickMappings(settings);
                        },
                        settings =>
                        {
                            settings.Keyboard.GamepadRemapEnabled = previousProfile;
                            settings.Keyboard.ButtonMappings = previousMappings;
                        },
                        () =>
                        {
                            AnalogJoystickEnabled = previousEnabled;
                            RestoreAnalogJoystickSelections(previousRows);
                        },
                        async token =>
                        {
                            await _service.SetAnalogJoystickReportModeAsync(
                                Connection, previousEnabled ? (byte)1 : (byte)0, token)
                                .ConfigureAwait(true);
                            await RollbackAnalogJoystickRowsAsync(applied, previousRows)
                                .ConfigureAwait(true);
                            if (previousEnabled)
                            {
                                AnalogJoystickRecoveryStore.Shared.Save(
                                    new AnalogJoystickRecoveryEntry(
                                        InstanceId, 0, _analogJoystickMappingMode ?? 0,
                                        DateTimeOffset.UtcNow));
                            }
                            else
                            {
                                AnalogJoystickRecoveryStore.Shared.Clear(InstanceId);
                            }
                        },
                        cancellationToken).ConfigureAwait(true);
                }
                catch
                {
                    await RestoreAnalogJoystickUiAfterFailureAsync(previousRows)
                        .ConfigureAwait(true);
                    throw;
                }

                _analogJoystickReportMode = requested ? (byte)1 : (byte)0;
                _analogJoystickEnabled = requested;
                foreach (var row in _analogJoystickRows)
                {
                    row.SetLoaded(row.SelectedChoice.ButtonId);
                }

                OnPropertyChanged(nameof(AnalogJoystickEnabled));
                UpdateAnalogJoystickStatus();
                NotifyAnalogJoystickChanged();
            }, cancellationToken);

    public void ApplyAnalogJoystickWasdPreset()
    {
        SelectAnalogJoystick(18, RazerControllerButton.LeftStickUp);
        SelectAnalogJoystick(32, RazerControllerButton.LeftStickDown);
        SelectAnalogJoystick(31, RazerControllerButton.LeftStickLeft);
        SelectAnalogJoystick(33, RazerControllerButton.LeftStickRight);
    }

    public void ApplyAnalogJoystickTriggerPreset()
    {
        SelectAnalogJoystick(17, RazerControllerButton.LeftTrigger);
        SelectAnalogJoystick(19, RazerControllerButton.RightTrigger);
    }

    public void ResetAnalogJoystickEdits()
    {
        foreach (var row in _analogJoystickRows)
        {
            row.Select(row.LoadedButtonId);
        }
    }

    internal async Task DisableAnalogJoystickOnExitAsync(CancellationToken cancellationToken)
    {
        if (!AnalogJoystickActive)
        {
            return;
        }

        await _service.SetAnalogJoystickReportModeAsync(Connection, 0, cancellationToken)
            .ConfigureAwait(false);
        AnalogJoystickRecoveryStore.Shared.Clear(InstanceId);
        _analogJoystickReportMode = 0;
    }

    private static IReadOnlyList<AnalogJoystickButtonChoice> CreateAnalogJoystickChoices() =>
    [
        new("A", (byte)RazerControllerButton.A),
        new("B", (byte)RazerControllerButton.B),
        new("X", (byte)RazerControllerButton.X),
        new("Y", (byte)RazerControllerButton.Y),
        new("LB", (byte)RazerControllerButton.LeftBumper),
        new("RB", (byte)RazerControllerButton.RightBumper),
        new("LT", (byte)RazerControllerButton.LeftTrigger),
        new("RT", (byte)RazerControllerButton.RightTrigger),
        new("L3", (byte)RazerControllerButton.LeftStick),
        new("R3", (byte)RazerControllerButton.RightStick),
        new("Start", (byte)RazerControllerButton.Start),
        new("Back", (byte)RazerControllerButton.Back),
        new("D-Pad Up", (byte)RazerControllerButton.DPadUp),
        new("D-Pad Down", (byte)RazerControllerButton.DPadDown),
        new("D-Pad Left", (byte)RazerControllerButton.DPadLeft),
        new("D-Pad Right", (byte)RazerControllerButton.DPadRight),
        new("Guide", (byte)RazerControllerButton.Guide),
        new(AppStrings.Text("GamepadRemapNone"), null),
    ];

    private void InitializeAnalogJoystickRows()
    {
        if (_analogJoystickRows.Count > 0)
        {
            return;
        }

        InitializeAnalogKeyLayout();
        var labels = _analogKeyKeys.ToDictionary(key => key.KeyId, key => key.Label);
        foreach (var keyId in AnalogJoystickKeyIds)
        {
            if (labels.TryGetValue(keyId, out var label))
            {
                _analogJoystickRows.Add(new AnalogJoystickKeyViewModel(
                    keyId, label, _analogJoystickChoices));
            }
        }

        OnPropertyChanged(nameof(AnalogJoystickRows));
    }

    private async Task ReadAnalogJoystickMappingModeAsync(
        byte profileId,
        CancellationToken cancellationToken)
    {
        try
        {
            var mapping = await _service.GetAnalogJoystickMappingModeAsync(
                Connection, profileId, cancellationToken).ConfigureAwait(true);
            _analogJoystickMappingMode = mapping.MappingMode;
        }
        catch (Exception exception) when (IsAnalogKeyReadbackFailure(exception))
        {
            _analogJoystickMappingMode = null;
        }
    }

    private async Task ReadAnalogJoystickRowsAsync(
        byte profileId,
        CancellationToken cancellationToken)
    {
        var assignments = new Dictionary<byte, RazerAnalogKeyAssignment>();
        try
        {
            foreach (var batch in _analogJoystickRows.Chunk(8))
            {
                var selectors = batch.Select(row =>
                    new RazerAnalogKeyAssignmentSelector(
                        row.KeyId, RazerButtonAssignmentMode.NormalMode)).ToArray();
                var table = await _service.GetAnalogKeyAssignmentsAsync(
                    Connection, profileId, selectors, cancellationToken).ConfigureAwait(true);
                foreach (var assignment in table.Entries)
                {
                    assignments[assignment.KeyId] = assignment;
                }
            }
        }
        catch (Exception exception) when (IsAnalogKeyReadbackFailure(exception))
        {
            // The mode toggle still works when the mapping table is busy.
        }

        foreach (var row in _analogJoystickRows)
        {
            var buttonId = assignments.TryGetValue(row.KeyId, out var assignment) &&
                assignment.FunctionId == RazerButtonFunctionType.Controller &&
                assignment.Data.Count > 0
                    ? assignment.Data[0]
                    : (byte?)null;
            row.SetLoaded(buttonId);
        }
    }

    private Task WriteAnalogJoystickRowAsync(
        AnalogJoystickKeyViewModel row,
        byte? buttonId,
        CancellationToken cancellationToken)
    {
        if (buttonId is byte controllerButton)
        {
            return _service.SetButtonAssignmentAsync(
                Connection, row.KeyId, 0, (byte)RazerButtonFunctionType.Controller,
                [controllerButton], cancellationToken);
        }

        if (!RazerHuntsmanV3ProKeys.TryGetHidUsage(row.KeyId, out var hid))
        {
            throw new NotSupportedException(
                $"No native HID usage is known for keyboard key {row.KeyId}.");
        }

        return _service.SetButtonAssignmentAsync(
            Connection, row.KeyId, 0, (byte)RazerButtonFunctionType.KeyCode,
            [0, hid], cancellationToken);
    }

    private async Task RollbackAnalogJoystickRowsAsync(
        IReadOnlyList<AnalogJoystickKeyViewModel> applied,
        IReadOnlyList<(AnalogJoystickKeyViewModel Row, byte? ButtonId)> previous)
    {
        foreach (var row in applied.Reverse())
        {
            var previousButton = previous.FirstOrDefault(item => item.Row == row).ButtonId;
            try
            {
                await WriteAnalogJoystickRowAsync(row, previousButton, CancellationToken.None)
                    .ConfigureAwait(true);
            }
            catch (Exception exception) when (IsAnalogKeyReadbackFailure(exception))
            {
                // The mode rollback below still runs; report the mapping failure through it.
            }
        }
    }

    private async Task RestoreAnalogJoystickUiAfterFailureAsync(
        IReadOnlyList<(AnalogJoystickKeyViewModel Row, byte? ButtonId)> previousRows)
    {
        try
        {
            _analogJoystickReportMode = await _service.GetAnalogJoystickReportModeAsync(
                Connection, CancellationToken.None).ConfigureAwait(true);
            _analogJoystickModeKnown = true;
        }
        catch (Exception exception) when (IsAnalogKeyReadbackFailure(exception))
        {
        }

        _analogJoystickEnabled = _analogJoystickReportMode == 1;
        RestoreAnalogJoystickSelections(previousRows);
        UpdateAnalogJoystickStatus();
        OnPropertyChanged(nameof(AnalogJoystickEnabled));
        NotifyAnalogJoystickChanged();
    }

    private void PersistAnalogJoystickMappings(DeviceProfileSettings settings)
    {
        var mappings = settings.Keyboard.ButtonMappings ??= [];
        foreach (var row in _analogJoystickRows)
        {
            if (row.SelectedChoice.ButtonId == row.LoadedButtonId)
            {
                continue;
            }

            mappings.RemoveAll(mapping =>
                mapping.ButtonId == row.KeyId &&
                (mapping.Mode ?? 0) == 0 &&
                mapping.Extra is not null &&
                mapping.Extra.ContainsKey("gamepad-remap"));
            var data = row.SelectedChoice.ButtonId is byte controllerButton
                ? new List<byte> { controllerButton }
                : RazerHuntsmanV3ProKeys.TryGetHidUsage(row.KeyId, out var hid)
                    ? [0, hid]
                    : null;
            if (data is null)
            {
                continue;
            }

            mappings.Add(new ButtonMappingProfile
            {
                ButtonId = row.KeyId,
                Mode = 0,
                FunctionId = row.SelectedChoice.ButtonId is null
                    ? (byte)RazerButtonFunctionType.KeyCode
                    : (byte)RazerButtonFunctionType.Controller,
                FunctionData = data,
                Extra = new Dictionary<string, string>
                {
                    [RazerHuntsmanV3ProKeys.ProfileKeyIdMarker] =
                        RazerHuntsmanV3ProKeys.ProfileFirmwareKeyIds,
                    ["gamepad-remap"] = "1",
                },
            });
        }
    }

    private void SelectAnalogJoystick(byte keyId, RazerControllerButton button)
    {
        _analogJoystickRows.FirstOrDefault(row => row.KeyId == keyId)?.Select((byte)button);
    }

    private void RestoreAnalogJoystickSelections(
        IReadOnlyList<(AnalogJoystickKeyViewModel Row, byte? ButtonId)> previous)
    {
        foreach (var item in previous)
        {
            item.Row.Select(item.ButtonId);
        }
    }

    private void UpdateAnalogJoystickStatus()
    {
        AnalogJoystickStatusText = !_analogJoystickModeKnown
            ? AppStrings.Text("GamepadRemapReadFailed")
            : _analogJoystickReportMode is 0 or 1
                ? AppStrings.FormatText(
                    "GamepadRemapModeStatus",
                    _analogJoystickReportMode,
                    _analogJoystickMappingMode?.ToString() ?? "--")
                : AppStrings.FormatText("GamepadRemapUnknownMode", _analogJoystickReportMode);
    }

    private void NotifyAnalogJoystickChanged()
    {
        OnPropertyChanged(nameof(AnalogJoystickVisibility));
        OnPropertyChanged(nameof(AnalogJoystickWriteVisibility));
        OnPropertyChanged(nameof(CanEditAnalogJoystick));
        OnPropertyChanged(nameof(CanApplyAnalogJoystick));
        OnPropertyChanged(nameof(AnalogJoystickActive));
    }
}
