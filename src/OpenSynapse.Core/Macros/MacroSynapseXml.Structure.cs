using System.Xml.Linq;

namespace OpenSynapse.Core.Macros;

internal static partial class MacroSynapseXml
{
    private static List<MacroStep> Current(List<MacroStep> steps, Stack<LoopFrame> loops) =>
        loops.Count > 0 ? loops.Peek().Body : steps;

    private static void ParseKey(
        List<MacroStep> steps,
        XElement macroEvent,
        XElement keyEvent,
        bool version4)
    {
        var makecode = ParseRequiredInt(Child(keyEvent, "Makecode"), "KeyEvent/Makecode");
        if (makecode is < 1 or > 0x1FF)
        {
            throw new InvalidDataException(
                $"Synapse key event Makecode {makecode} is outside the supported 1-511 range.");
        }

        // A key press without <State> is a press. Synapse only writes State 1
        // for the release.
        var isDown = ParseOptionalInt(Child(keyEvent, "State"), 0, "KeyEvent/State") != 1;

        MacroInput input;
        if (version4)
        {
            // Synapse 4 stores the Windows virtual key and, when present, the
            // physical scan code. The scan code is the only way to tell the
            // left and right modifiers apart.
            var scanCode = ParseOptionalInt(Child(macroEvent, "ScanCode"), 0, "ScanCode");
            if (scanCode is < 0 or > 0xFFFF)
            {
                throw new InvalidDataException("Synapse ScanCode must be between 0 and 65535.");
            }

            input = new MacroInput
            {
                Kind = MacroInputKind.Keyboard,
                VirtualKey = makecode,
                ScanCode = scanCode & 0xFF,
                Extended = scanCode > 0xFF,
            };
        }
        else
        {
            // Synapse 3 stores the scan code, with the E0 flag in bit 8.
            var scanCode = makecode & 0xFF;
            var virtualKey = scanCode < ScanCodeToVirtualKey.Length
                ? ScanCodeToVirtualKey[scanCode]
                : 0;
            if (virtualKey == 0)
            {
                throw new InvalidDataException(
                    $"Synapse scan code {scanCode} has no matching Windows key.");
            }

            input = new MacroInput
            {
                Kind = MacroInputKind.Keyboard,
                VirtualKey = SideAwareVirtualKey(scanCode, virtualKey, (makecode & 0x100) != 0),
                ScanCode = scanCode,
                Extended = (makecode & 0x100) != 0,
            };
        }

        AddInput(steps, macroEvent, input, isDown);
    }

    // The scan code table cannot tell the left and right modifiers apart, so
    // the physical side decides the virtual key.
    private static int SideAwareVirtualKey(int scanCode, int virtualKey, bool extended)
    {
        switch (virtualKey)
        {
            case 0x10:
                return scanCode == 0x36 ? 0xA1 : 0xA0;
            case 0x11:
                return extended ? 0xA3 : 0xA2;
            case 0x12:
                return extended ? 0xA5 : 0xA4;
            default:
                return virtualKey;
        }
    }

    private static void ParseMouse(
        List<MacroStep> steps,
        XElement macroEvent,
        XElement mouseEvent,
        bool version4)
    {
        var button = ParseRequiredInt(Child(mouseEvent, "MouseButton"), "MouseEvent/MouseButton");
        var isDown = ParseOptionalInt(Child(mouseEvent, "State"), 0, "MouseEvent/State") != 1;
        var mapped = version4
            ? button switch
            {
                0 => 1,
                1 => 2,
                _ => throw new InvalidDataException(
                    $"Synapse mouse button {button} is not supported in version 4 files."),
            }
            : button switch
            {
                1 => 1,
                2 => 2,
                3 => 3,
                4 => 4,
                5 => 5,
                _ => throw new InvalidDataException(
                    $"Synapse mouse button {button} is not supported."),
            };

        AddInput(steps, macroEvent, new MacroInput
        {
            Kind = MacroInputKind.MouseButton,
            MouseButton = mapped,
        }, isDown);
    }

    private static void ParseMouseMovement(
        List<MacroStep> steps,
        XElement macroEvent,
        bool version4,
        XElement root)
    {
        // Synapse 4 records the path as MouseEvent/Buffer entries with a
        // per-point time; Synapse 3 uses MouseMovementEvent with a per-point
        // delay and a type that says how the coordinates are scaled.
        var buffers = macroEvent
            .Descendants()
            .Where(element => element.Name.LocalName == "Buffer")
            .ToList();
        if (buffers.Count > 0)
        {
            var bufferPoints = buffers.Select(buffer => new MacroMousePoint
            {
                X = ParseRequiredInt(Child(buffer, "x"), "MouseEvent/Buffer/x"),
                Y = ParseRequiredInt(Child(buffer, "y"), "MouseEvent/Buffer/y"),
                // Synapse 4 records the point time in milliseconds as a decimal.
                OffsetMilliseconds = ParseOptionalMilliseconds(Child(buffer, "time"), "MouseEvent/Buffer/time"),
            }).ToList();
            var relative = string.Equals(
                Child(root, "MouseMoveType")?.Value.Trim(),
                "relative",
                StringComparison.OrdinalIgnoreCase);
            AddMousePath(
                steps,
                macroEvent,
                bufferPoints,
                relative ? MacroMouseMoveMode.Relative : MacroMouseMoveMode.Absolute);
            return;
        }

        var movement = macroEvent
            .Descendants()
            .FirstOrDefault(element => element.Name.LocalName == "MouseMovementEvent");
        if (movement is null)
        {
            throw new InvalidDataException("Synapse mouse movement contains no points.");
        }

        var movementType = ParseOptionalInt(Child(movement, "Type"), 0, "MouseMovementEvent/Type");
        var mode = movementType switch
        {
            3 => MacroMouseMoveMode.Absolute,
            // Synapse 3 type 2 scales the path to 0-10000 across the screen,
            // which is the normalized range the playback sink expects.
            2 => MacroMouseMoveMode.ForegroundRelative,
            _ => throw new InvalidDataException(
                $"Synapse mouse movement type {movementType} is not supported."),
        };

        var points = new List<MacroMousePoint>();
        foreach (var element in macroEvent.Descendants()
                     .Where(item => item.Name.LocalName == "MouseMovementEvent"))
        {
            points.Add(new MacroMousePoint
            {
                X = ParseRequiredInt(Child(element, "X"), "MouseMovementEvent/X"),
                Y = ParseRequiredInt(Child(element, "Y"), "MouseMovementEvent/Y"),
                OffsetMilliseconds = ParseOptionalInt(
                    Child(element, "Delay"),
                    0,
                    "MouseMovementEvent/Delay"),
            });
        }

        if (points.Count == 0)
        {
            throw new InvalidDataException("Synapse mouse movement contains no points.");
        }

        AddMousePath(steps, macroEvent, points, mode);
    }

    private static void AddMousePath(
        List<MacroStep> steps,
        XElement macroEvent,
        List<MacroMousePoint> points,
        MacroMouseMoveMode mode)
    {
        if (points.Count > MacroStep.MaxMousePathPoints)
        {
            throw new InvalidDataException(
                $"Synapse mouse movement exceeds the {MacroStep.MaxMousePathPoints}-point limit.");
        }

        // Synapse stores the gap between points; playback wants the elapsed
        // time from the start of the movement.
        long offset = 0;
        foreach (var point in points)
        {
            offset = Math.Min(
                offset + Math.Max(point.OffsetMilliseconds, 0),
                MacroDefinition.MaxDelayMilliseconds);
            point.OffsetMilliseconds = (int)offset;
        }

        AddStep(steps, new MacroStep
        {
            Kind = MacroStepKind.MouseMove,
            DelayMilliseconds = ParseEventDelay(macroEvent),
            MouseMoveMode = mode,
            MousePath = points,
        });
    }

    private static int ParseType6(
        List<MacroStep> rootSteps,
        XElement macroEvent,
        Stack<LoopFrame> loops,
        int legacyLoopIndex)
    {
        var loopEvent = Child(macroEvent, "LoopEvent");
        var legacyLoop = Child(macroEvent, "Loop");
        if (loopEvent is null && legacyLoop is null)
        {
            var milliseconds = ParseRequiredInt(Child(macroEvent, "Delay"), "Delay");
            if (milliseconds < 0 || milliseconds > MacroDefinition.MaxDelayMilliseconds)
            {
                throw new InvalidDataException(
                    $"Synapse delay must be between 0 and {MacroDefinition.MaxDelayMilliseconds} ms.");
            }

            AddDelay(rootSteps, milliseconds);
            return legacyLoopIndex;
        }

        var count = ParseRequiredInt(
            Child(macroEvent, "Number") ?? Child(legacyLoop!, "Count"),
            "Number");
        if (count is < 1 or > MacroStep.MaxLoopCount)
        {
            throw new InvalidDataException(
                $"Synapse loop count {count} is outside the supported 1-{MacroStep.MaxLoopCount} range.");
        }

        // Synapse 4 marks the start and end with LoopEvent/State. Synapse 3 has
        // no state and only writes the two markers in order, so they pair up
        // by position.
        bool isStart;
        if (loopEvent is not null)
        {
            isStart = ParseOptionalInt(Child(loopEvent, "State"), 0, "LoopEvent/State") != 1;
        }
        else
        {
            isStart = legacyLoopIndex % 2 == 0;
        }

        if (isStart)
        {
            if (loops.Count >= MacroDefinition.MaxNestingDepth)
            {
                throw new InvalidDataException(
                    $"Synapse macro nests loops deeper than {MacroDefinition.MaxNestingDepth}.");
            }

            loops.Push(new LoopFrame(count));
            return legacyLoopIndex + 1;
        }

        if (loops.Count == 0)
        {
            throw new InvalidDataException("The Synapse macro has a loop end without a start.");
        }

        var frame = loops.Pop();
        if (frame.Count != count)
        {
            throw new InvalidDataException(
                $"Synapse loop start count {frame.Count} does not match the end count {count}.");
        }

        if (frame.Body.Count > 0)
        {
            // The finished loop belongs to the level that opened it.
            var target = loops.Count > 0 ? loops.Peek().Body : rootSteps;
            AddStep(target, new MacroStep
            {
                Kind = MacroStepKind.Loop,
                LoopCount = count,
                Children = frame.Body,
            });
        }

        return legacyLoopIndex + 1;
    }

    private static void AddInput(
        List<MacroStep> steps,
        XElement macroEvent,
        MacroInput input,
        bool isDown)
    {
        var delay = ParseEventDelay(macroEvent);
        if (delay > 0)
        {
            AddStep(steps, new MacroStep
            {
                Kind = MacroStepKind.Delay,
                DelayMilliseconds = delay,
            });
        }

        AddStep(steps, new MacroStep
        {
            Kind = MacroStepKind.Input,
            Input = input,
            IsDown = isDown,
            AutoRelease = false,
        });
    }

    private static void AddDelay(List<MacroStep> steps, int milliseconds)
    {
        if (milliseconds <= 0)
        {
            return;
        }

        AddStep(steps, new MacroStep
        {
            Kind = MacroStepKind.Delay,
            DelayMilliseconds = milliseconds,
        });
    }
}
