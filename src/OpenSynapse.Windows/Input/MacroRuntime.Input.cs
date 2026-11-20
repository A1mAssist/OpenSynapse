using OpenSynapse.Core.Macros;

namespace OpenSynapse.Windows.Input;

internal sealed partial class MacroRuntime
{
    /// <summary>
    /// A press that arrives this long after an unreleased down cannot be an
    /// auto-repeat; it is the release the device never sent. Keeping the
    /// suppression entry would swallow every later press until the app restarts.
    /// </summary>
    internal long StaleSuppressedInputMilliseconds { get; set; } = 10_000;

    public void SetHyperShiftHeld(bool held)
    {
        var stopPlayback = false;
        lock (_gate)
        {
            if (_hyperShiftHeld == held)
            {
                return;
            }

            _hyperShiftHeld = held;
            if (_isPlaying &&
                _activePlaybackMacroId is { } activeId &&
                _macroMap.TryGetValue(activeId, out var active) &&
                active.AllTriggers.Any() &&
                !active.AllTriggers.Any(trigger => trigger.HyperShiftLayer == held))
            {
                stopPlayback = true;
            }
        }

        if (stopPlayback)
        {
            StopPlayback();
        }
    }

    private bool HandleInput(MacroInputEvent value)
    {
        TrackPhysicalModifier(value);
        if (value.IsMouseMove)
        {
            MacroRecordingSession? mouseRecording;
            bool recordOwnWindow;
            lock (_gate)
            {
                mouseRecording = _recording;
                recordOwnWindow = _recordingOptions.RecordOwnWindow;
            }
            if (mouseRecording is not null &&
                (recordOwnWindow || _isOwnWindowForeground?.Invoke() != true))
            {
                mouseRecording.TryAddMouseMove(
                    value.X,
                    value.Y,
                    value.TimestampMilliseconds,
                    value.WindowLeft,
                    value.WindowTop,
                    value.WindowWidth,
                    value.WindowHeight);
            }
            return false;
        }

        if (MacroInputTrace.Enabled)
        {
            _log?.Invoke(
                $"input {MacroInputFormatter.Describe(value.Input)} down={value.IsDown} " +
                $"modifiers={value.Modifiers} hyperShift={_hyperShiftHeld}");
        }

        // A Razer private key only exists on the device's own report stream; the
        // once-per-collection reader log cannot tell "the key never arrived" from
        // "the trigger stopped matching". Never write the log on the hook thread.
        if (value.IsDown && value.Input.Kind == MacroInputKind.RazerKey)
        {
            var message =
                $"razer-key down code=0x{value.Input.RazerKeyCode:X2} " +
                $"device=0x{value.Input.DeviceId:X4}";
            _ = Task.Run(() => _log?.Invoke(message));
        }

        if (value.Input.Kind == MacroInputKind.RazerKey &&
            value.Input.DeviceId is > 0 and <= 0xFFFF &&
            RazerPrivateKeyCatalog.IsHyperShift(
                (ushort)value.Input.DeviceId,
                value.Input.RazerKeyCode))
        {
            SetHyperShiftHeld(value.IsDown);
        }

        // An unclaimed private-key release returns from the macro lock below;
        // stop its turbo here so hold-to-turbo ends with the physical key.
        if (value.Input.Kind == MacroInputKind.RazerKey &&
            value.Input.DeviceId is > 0 and <= 0xFFFF &&
            !value.IsDown)
        {
            StopTurbo(value.Input);
        }

        MacroDefinition? playMacro = null;
        var playModifiers = MacroModifierKeys.None;
        MacroTrigger? playTrigger = null;
        var suppress = false;
        var stopRequested = false;
        var stopPlayback = false;
        var startPlayback = false;
        var playbackShortcutHandled = false;
        CancellationTokenSource? holdCancellation = null;
        MacroInput? holdInput = null;
        MacroInput? unhandledRazerKey = null;

        // Playback shortcuts take priority over macro triggers, but never while
        // recording or capturing a key.
        lock (_gate)
        {
            var canUsePlaybackShortcuts =
                _recording is null &&
                _stepCapture is null &&
                _triggerCapture is null &&
                !_recordingPending;
            if (canUsePlaybackShortcuts)
            {
                var matchesStart = _playbackStartShortcut?.Matches(value.Input) == true;
                var matchesStop = _playbackStopShortcut.Matches(value.Input);
                if (value.IsDown)
                {
                    if (matchesStart && _playbackStartHotkeyHeld)
                    {
                        playbackShortcutHandled = true;
                    }
                    else if (matchesStop && _playbackStopHotkeyHeld)
                    {
                        playbackShortcutHandled = true;
                    }
                    else if (matchesStop && _isPlaying)
                    {
                        _playbackStopHotkeyHeld = true;
                        stopPlayback = true;
                        playbackShortcutHandled = true;
                    }
                    else if (matchesStart)
                    {
                        _playbackStartHotkeyHeld = true;
                        startPlayback = !_isPlaying;
                        playbackShortcutHandled = true;
                    }
                }
                else
                {
                    if (matchesStart && _playbackStartHotkeyHeld)
                    {
                        _playbackStartHotkeyHeld = false;
                        playbackShortcutHandled = true;
                    }
                    if (matchesStop && _playbackStopHotkeyHeld)
                    {
                        _playbackStopHotkeyHeld = false;
                        playbackShortcutHandled = true;
                    }
                }
            }
        }

        if (playbackShortcutHandled)
        {
            if (stopPlayback)
            {
                StopPlayback();
            }
            if (startPlayback)
            {
                PlaybackStartRequested?.Invoke();
            }
            return true;
        }

        lock (_gate)
        {
            if (_disposed)
            {
                return false;
            }

            if (_recordingStopInput is { } heldStop &&
                heldStop.Matches(value.Input))
            {
                if (!value.IsDown)
                {
                    _recordingStopInput = null;
                }
                return true;
            }

            if (_recording is { } recording)
            {
                if (_recordingOptions.StopShortcut is { } stopShortcut &&
                    stopShortcut.Matches(value.Input))
                {
                    _recordingStopInput = value.IsDown ? value.Input.Clone() : null;
                    if (value.IsDown)
                    {
                        stopRequested = true;
                    }
                    suppress = true;
                    goto InputHandled;
                }

                // Do not record input in our own editor unless the user opted in.
                if (!_recordingOptions.RecordOwnWindow &&
                    _isOwnWindowForeground?.Invoke() == true)
                {
                    return false;
                }

                // Record the standard hook key, not unreplayable device-scoped triggers.
                if (value.Input.Kind is MacroInputKind.NagaSideButton or
                    MacroInputKind.DeviceButton or MacroInputKind.RazerKey ||
                    value.Input.Kind == MacroInputKind.MouseButton &&
                    value.Input.MouseButton > 5)
                {
                    return false;
                }

                recording.TryAdd(value.Input, value.IsDown, value.TimestampMilliseconds);
                return false;
            }

            if (_stepCapture is { } stepCapture)
            {
                if (value.Input.Kind == MacroInputKind.MouseButton &&
                    value.Input.MouseButton is 1 or 2 &&
                    _isOwnWindowForeground?.Invoke() == true)
                {
                    return false;
                }

                var isWheel = value.Input.Kind == MacroInputKind.MouseWheel;
                if ((!value.IsDown && !isWheel) ||
                    !value.Input.IsValid(allowPrimaryMouseButton: true, allowWheel: true))
                {
                    return false;
                }

                _stepCapture = null;
                if (!isWheel)
                {
                    _suppressedCapture = value.Input.Clone();
                }
                stepCapture.TrySetResult(value.Input.Clone());
                return true;
            }

            if (_triggerCapture is { } capture)
            {
                if (value.Input.Kind == MacroInputKind.RazerKey &&
                    value.Input.DeviceId is > 0 and <= 0xFFFF &&
                    RazerPrivateKeyCatalog.IsHyperShift(
                        (ushort)value.Input.DeviceId,
                        value.Input.RazerKeyCode))
                {
                    return false;
                }

                if (!value.IsDown ||
                    !value.Input.IsValid(
                        allowPrimaryMouseButton: false,
                        allowWheel: false,
                        allowNagaSideButton: _triggerCaptureAllowsNagaSideButton,
                        allowDeviceButton: _triggerCaptureAllowsDeviceButton,
                        allowExtraMouseButton: true))
                {
                    return false;
                }

                if (value.Input.IsModifierKey)
                {
                    return false;
                }

                var modifiers = _triggerCaptureAllowsModifiers
                    ? value.Modifiers
                    : MacroModifierKeys.None;
                _triggerCapture = null;
                _suppressedCapture = value.Input.Clone();
                capture.TrySetResult(new MacroTrigger
                {
                    Input = value.Input.Clone(),
                    Modifiers = modifiers,
                    HyperShiftLayer = _hyperShiftHeld,
                });
                return true;
            }

            if (_recordingPending)
            {
                return false;
            }

            if (_suppressedCapture is { } captureSuppressed &&
                captureSuppressed.Matches(value.Input))
            {
                if (!value.IsDown)
                {
                    _suppressedCapture = null;
                }
                return true;
            }

            var key = KeyOf(value.Input);
            if (_suppressedInputs.TryGetValue(key, out var suppressed))
            {
                if (!value.IsDown)
                {
                    _suppressedInputs.Remove(key);
                    suppressed.HoldCancellation?.Cancel();
                    if (suppressed.Mode is MacroTriggerMode.Hold or MacroTriggerMode.HoldRepeat &&
                        StringComparer.Ordinal.Equals(
                            _activePlaybackMacroId,
                            suppressed.MacroId))
                    {
                        stopPlayback = true;
                    }
                    return true;
                }

                // A second down without an up is normal auto-repeat for the
                // hold-family modes. Toggle needs it to reach the stop branch
                // so a lost release can never leave the macro unstoppable.
                // Past the stale window the entry is a release the device never
                // sent: drop it and match this press instead of swallowing it.
                var stale = Environment.TickCount64 - suppressed.DownTick >=
                    StaleSuppressedInputMilliseconds;
                if (suppressed.Mode != MacroTriggerMode.Toggle &&
                    value.Input.Kind != MacroInputKind.MouseButton &&
                    !stale)
                {
                    // A genuinely held key keeps resetting this, so only a real
                    // gap between presses can reach the stale branch.
                    _suppressedInputs[key] = suppressed with
                    {
                        DownTick = Environment.TickCount64,
                    };
                    return true;
                }

                _suppressedInputs.Remove(key);
                suppressed.HoldCancellation?.Cancel();
            }

            if (!value.IsDown)
            {
                return false;
            }

            foreach (var macro in _macros)
            {
                if (!macro.Enabled)
                {
                    TraceSkipped(macro, "disabled");
                    continue;
                }
                if (macro.Steps.Count == 0)
                {
                    TraceSkipped(macro, "no steps");
                    continue;
                }
                var trigger = macro.FindTrigger(value.Input, value.Modifiers, _hyperShiftHeld);
                if (trigger is null)
                {
                    TraceSkipped(macro, "no matching trigger");
                    continue;
                }

                if (MacroInputTrace.Enabled)
                {
                    _log?.Invoke(
                        $"trigger matched macro={macro.Name} id={macro.Id} mode={trigger.Mode}");
                }
                suppress = true;
                playModifiers = trigger.Modifiers;
                _suppressedInputs[key] = new SuppressedInput(
                    trigger.Mode, macro.Id, DownTick: Environment.TickCount64);
                switch (trigger.Mode)
                {
                    case MacroTriggerMode.Hold:
                    case MacroTriggerMode.HoldRepeat:
                        if (!_isPlaying ||
                            !StringComparer.Ordinal.Equals(_activePlaybackMacroId, macro.Id))
                        {
                            playMacro = macro;
                            playTrigger = trigger;
                        }
                        break;

                    case MacroTriggerMode.Toggle:
                        if (_isPlaying &&
                            StringComparer.Ordinal.Equals(_activePlaybackMacroId, macro.Id))
                        {
                            stopPlayback = true;
                        }
                        else
                        {
                            playMacro = macro;
                            playTrigger = trigger;
                        }
                        break;

                    case MacroTriggerMode.Sequence:
                        if (macro.Steps.Count > 0)
                        {
                            var index = _sequencePositions.GetValueOrDefault(macro.Id) %
                                macro.Steps.Count;
                            _sequencePositions[macro.Id] = (index + 1) % macro.Steps.Count;
                            playMacro = new MacroDefinition
                            {
                                Id = macro.Id,
                                Name = macro.Name,
                                PlaybackMode = MacroPlaybackMode.Once,
                                PlaybackSpeed = macro.PlaybackSpeed,
                                MouseTrackingMode = macro.MouseTrackingMode,
                                Steps = [macro.Steps[index]],
                            };
                        }
                        break;

                    default:
                        playMacro = macro;
                        playTrigger = trigger;
                        break;
                }

                if (playMacro is not null &&
                    trigger.Mode is MacroTriggerMode.Hold or MacroTriggerMode.HoldRepeat)
                {
                    holdCancellation = new CancellationTokenSource();
                    holdInput = value.Input.Clone();
                    _suppressedInputs[key] = _suppressedInputs[key] with
                    {
                        HoldCancellation = holdCancellation,
                    };
                }
                break;
            }

            if (!suppress &&
                value.IsDown &&
                value.Input.Kind == MacroInputKind.RazerKey &&
                _recording is null &&
                _stepCapture is null &&
                _triggerCapture is null &&
                !_recordingPending)
            {
                unhandledRazerKey = value.Input.Clone();
            }
        }

        if (value.Input.Kind == MacroInputKind.RazerKey &&
            value.Input.DeviceId is > 0 and <= 0xFFFF)
        {
            if (value.IsDown)
            {
                if (!suppress &&
                    playMacro is null &&
                    TurboResolver?.Invoke(value.Input, _hyperShiftHeld) is { } turbo)
                {
                    StartTurbo(value.Input, turbo);
                }
            }
        }

    InputHandled:
        if (stopRequested)
        {
            RecordingStopRequested?.Invoke();
        }
        if (stopPlayback)
        {
            StopPlayback();
        }
        if (playMacro is not null)
        {
            // Never build a playback plan or send the first input on the
            // low-level hook thread. Windows drops slow hooks and the desktop
            // would stall until the plan finishes.
            _ = Task.Run(() => ObservePlaybackAsync(
                playMacro, playModifiers, holdCancellation, holdInput, playTrigger));
        }
        if (unhandledRazerKey is not null)
        {
            // The fallback reads and writes the device, never the hook thread.
            _ = Task.Run(() => UnhandledRazerKeyHandler?.Invoke(unhandledRazerKey));
        }
        return suppress;
    }

    private void TrackPhysicalModifier(MacroInputEvent value)
    {
        if (value.Input.Kind != MacroInputKind.Keyboard ||
            !value.Input.IsModifierKey)
        {
            return;
        }

        lock (_gate)
        {
            if (value.IsDown)
            {
                _physicalModifierKeys.Add(value.Input.VirtualKey);
            }
            else
            {
                _physicalModifierKeys.Remove(value.Input.VirtualKey);
            }
        }
    }

    private static MacroModifierKeys ModifierFlag(int virtualKey) => virtualKey switch
    {
        0xA0 or 0xA1 => MacroModifierKeys.Shift,
        0xA2 or 0xA3 => MacroModifierKeys.Control,
        0xA4 or 0xA5 => MacroModifierKeys.Alt,
        0x5B or 0x5C => MacroModifierKeys.Windows,
        _ => MacroModifierKeys.None,
    };

    private static InputKey KeyOf(MacroInput input) =>
        input.Kind switch
        {
            MacroInputKind.MouseButton => new InputKey(input.Kind, input.MouseButton, false),
            MacroInputKind.NagaSideButton => new InputKey(input.Kind, input.SideButton, false),
            MacroInputKind.DeviceButton => new InputKey(
                input.Kind,
                (input.VirtualKey << 16) | input.ScanCode,
                input.Extended,
                input.DeviceId),
            MacroInputKind.RazerKey => new InputKey(
                input.Kind,
                input.RazerKeyCode,
                false,
                input.DeviceId),
            _ => new InputKey(input.Kind, input.VirtualKey, input.Extended),
        };

    private readonly record struct InputKey(
        MacroInputKind Kind,
        int Code,
        bool Extended,
        int DeviceId = 0);

    private readonly record struct SuppressedInput(
        MacroTriggerMode Mode,
        string? MacroId,
        CancellationTokenSource? HoldCancellation = null,
        long DownTick = 0);
}
