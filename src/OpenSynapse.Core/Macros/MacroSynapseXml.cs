using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace OpenSynapse.Core.Macros;

/// <summary>
/// Reads the XML macro format exported by Razer Synapse.
///
/// The format is not one schema, and the difference decides the whole parse.
/// Synapse 4 native exports carry <Version>4</Version>, store key events as
/// Windows virtual keys, mouse buttons zero-based (0 = left, 1 = right) and
/// the recorded mouse path as MouseEvent/Buffer entries with a per-point time.
/// Older Synapse 3 exports have no version, store key events as keyboard scan
/// codes (Synapse 3 records what the key physically is, not what it types),
/// mouse buttons one-based (1 = left, 2 = right) and the path as
/// MouseMovementEvent entries with a per-point delay.
/// </summary>
internal static class MacroSynapseXml
{
    private const int MaxDocumentCharacters = 5_000_000;

    // US layout scan code to virtual key. Synapse 3 records scan codes, and
    // MapVirtualKey resolves them the same way; the table keeps the parser in
    // the platform-independent core project.
    private static readonly byte[] ScanCodeToVirtualKey =
    [
        0x00, 0x1B, 0x31, 0x32, 0x33, 0x34, 0x35, 0x36, 0x37, 0x38, 0x39, 0x30,
        0xBD, 0xBB, 0x08, 0x09, 0x51, 0x57, 0x45, 0x52, 0x54, 0x59, 0x55, 0x49,
        0x4F, 0x50, 0xDB, 0xDD, 0x0D, 0x11, 0x41, 0x53, 0x44, 0x46, 0x47, 0x48,
        0x4A, 0x4B, 0x4C, 0xBA, 0xDE, 0xC0, 0x10, 0xDC, 0x5A, 0x58, 0x43, 0x56,
        0x42, 0x4E, 0x4D, 0xBC, 0xBE, 0xBF, 0x10, 0x6A, 0x12, 0x20, 0x14, 0x70,
        0x71, 0x72, 0x73, 0x74, 0x75, 0x76, 0x77, 0x78, 0x79, 0x90, 0x91, 0x24,
        0x26, 0x21, 0x6D, 0x25, 0x0C, 0x27, 0x6B, 0x23, 0x28, 0x22, 0x2D, 0x2E,
        0x2C, 0x00, 0xE2, 0x7A, 0x7B, 0x0C, 0xEE, 0xF1, 0xEA, 0xF9, 0xF5, 0xF3,
        0x00, 0x00, 0xFB, 0x2F, 0x7C, 0x7D, 0x7E, 0x7F, 0x80, 0x81, 0x82, 0x83,
        0x84, 0x85, 0x86, 0xED, 0x00, 0xE9, 0x00, 0xC1, 0x00, 0x00, 0x87, 0x00,
        0x00, 0x00, 0x00, 0xEB, 0x09, 0x00, 0xC2,
    ];

    public static async Task<IReadOnlyList<MacroDefinition>> ImportAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        var settings = new XmlReaderSettings
        {
            Async = true,
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaxDocumentCharacters,
        };

        try
        {
            await using var stream = new FileStream(
                Path.GetFullPath(filePath),
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                options: FileOptions.SequentialScan | FileOptions.Asynchronous);
            using var reader = XmlReader.Create(stream, settings);
            var document = await XDocument.LoadAsync(
                reader,
                LoadOptions.None,
                cancellationToken).ConfigureAwait(false);
            return [Parse(document)];
        }
        catch (XmlException exception)
        {
            throw new InvalidDataException("Synapse macro XML is invalid.", exception);
        }
    }

    internal static MacroDefinition Parse(XDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var root = document.Root;
        if (root is null || root.Name.LocalName != "Macro")
        {
            throw new InvalidDataException("The XML root must be a Synapse Macro element.");
        }

        var events = Child(root, "MacroEvents")?
            .Elements()
            .Where(element => element.Name.LocalName == "MacroEvent")
            .ToList() ?? [];
        if (events.Count == 0)
        {
            throw new InvalidDataException("The Synapse macro contains no events.");
        }

        var version4 = Child(root, "Version")?.Value.Trim() == "4";
        var steps = new List<MacroStep>();
        var loops = new Stack<LoopFrame>();
        var legacyLoopIndex = 0;

        foreach (var macroEvent in events)
        {
            var type = Child(macroEvent, "Type")?.Value.Trim();
            switch (type)
            {
                case "actionBar":
                    // Editor metadata, not an executed event.
                    continue;

                case "0":
                    AddDelay(Current(steps, loops), ParseSecondsToMilliseconds(Child(macroEvent, "Number")));
                    continue;

                case "1":
                    if (Child(macroEvent, "KeyEvent") is { } keyEvent &&
                        Child(keyEvent, "Makecode") is not null)
                    {
                        ParseKey(Current(steps, loops), macroEvent, keyEvent, version4);
                    }
                    else
                    {
                        // Synapse writes an empty KeyEvent for "release every held
                        // key". The delay it carries is what the macro needs.
                        AddDelay(Current(steps, loops), ParseEventDelay(macroEvent));
                    }

                    continue;

                case "2":
                    if (Child(macroEvent, "MouseEvent") is { } mouseEvent)
                    {
                        ParseMouse(Current(steps, loops), macroEvent, mouseEvent, version4);
                    }
                    else
                    {
                        AddDelay(Current(steps, loops), ParseEventDelay(macroEvent));
                    }

                    continue;

                case "3":
                    ParseMouseMovement(Current(steps, loops), macroEvent, version4, root);
                    continue;

                case "6":
                    legacyLoopIndex = ParseType6(steps, macroEvent, loops, legacyLoopIndex);
                    continue;

                default:
                    throw new InvalidDataException(
                        $"Synapse macro event type '{type ?? "missing"}' is not supported.");
            }
        }

        if (loops.Count > 0)
        {
            throw new InvalidDataException("The Synapse macro ends inside an unclosed loop.");
        }

        if (steps.Count == 0)
        {
            throw new InvalidDataException("The Synapse macro contains no supported steps.");
        }

        var macro = new MacroDefinition
        {
            Name = (Child(root, "Name")?.Value ?? string.Empty).Trim(),
            Steps = steps,
        };
        macro.ApplySafeDefaults();
        if (string.IsNullOrWhiteSpace(macro.Name))
        {
            macro.Name = "Imported macro";
        }

        return macro;
    }

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

    private static int ParseEventDelay(XElement macroEvent)
    {
        var delay = ParseOptionalInt(Child(macroEvent, "Delay"), 0, "Delay");
        if (delay is < 0 or > MacroDefinition.MaxDelayMilliseconds)
        {
            throw new InvalidDataException(
                $"Synapse macro delay must be between 0 and {MacroDefinition.MaxDelayMilliseconds} ms.");
        }

        return delay;
    }

    private static int ParseOptionalMilliseconds(XElement? element, string name)
    {
        if (element is null)
        {
            return 0;
        }

        if (!double.TryParse(
                element.Value.Trim(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var value))
        {
            throw new InvalidDataException($"Synapse XML {name} is not a number.");
        }

        var milliseconds = (int)Math.Round(value, MidpointRounding.AwayFromZero);
        if (milliseconds < 0 || milliseconds > MacroDefinition.MaxDelayMilliseconds)
        {
            throw new InvalidDataException(
                $"Synapse XML {name} must be between 0 and {MacroDefinition.MaxDelayMilliseconds} ms.");
        }

        return milliseconds;
    }

    private static int ParseSecondsToMilliseconds(XElement? number)
    {
        if (number is null)
        {
            return 0;
        }

        if (!double.TryParse(
                number.Value.Trim(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var seconds))
        {
            throw new InvalidDataException("Synapse delay Number is not a number.");
        }

        var milliseconds = (int)Math.Round(seconds * 1000, MidpointRounding.AwayFromZero);
        if (milliseconds < 0 || milliseconds > MacroDefinition.MaxDelayMilliseconds)
        {
            throw new InvalidDataException(
                $"Synapse macro delay must be between 0 and {MacroDefinition.MaxDelayMilliseconds} ms.");
        }

        return milliseconds;
    }

    private static void AddStep(List<MacroStep> steps, MacroStep step)
    {
        if (steps.Count >= MacroDefinition.MaxSteps)
        {
            throw new InvalidDataException(
                $"Synapse macro exceeds the {MacroDefinition.MaxSteps}-step limit.");
        }

        steps.Add(step);
    }

    private static XElement? Child(XElement parent, string localName) =>
        parent.Elements().FirstOrDefault(element => element.Name.LocalName == localName);

    private static int ParseRequiredInt(XElement? element, string name) =>
        element is null
            ? throw new InvalidDataException($"Synapse XML is missing {name}.")
            : ParseOptionalInt(element, 0, name);

    private static int ParseOptionalInt(XElement? element, int fallback, string name)
    {
        if (element is null)
        {
            return fallback;
        }

        if (!int.TryParse(element.Value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            throw new InvalidDataException($"Synapse XML {name} is not an integer.");
        }

        return value;
    }

    private sealed class LoopFrame(int count)
    {
        public int Count { get; } = count;

        public List<MacroStep> Body { get; } = [];
    }
}



