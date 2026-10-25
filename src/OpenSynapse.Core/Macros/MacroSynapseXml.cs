using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace OpenSynapse.Core.Macros;

/// <summary>
/// Reads the XML macro format exported by Razer Synapse. Synapse 4 key events
/// use Makecode as the Windows virtual-key value and ScanCode for the physical
/// key; State 0 is down and 1 is up.
/// </summary>
internal static class MacroSynapseXml
{
    private const int MaxDocumentCharacters = 5_000_000;

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

        var steps = new List<MacroStep>();
        foreach (var macroEvent in events)
        {
            var delay = ParseOptionalInt(Child(macroEvent, "Delay"), 0, "Delay");
            if (delay is < 0 or > MacroDefinition.MaxDelayMilliseconds)
            {
                throw new InvalidDataException(
                    $"Synapse macro delay must be between 0 and {MacroDefinition.MaxDelayMilliseconds} ms.");
            }

            if (Child(macroEvent, "MouseMovement") is { } movement)
            {
                AddMouseMovement(steps, delay, movement);
            }
            else if (macroEvent.Elements().Any(element => element.Name.LocalName == "MouseMovementEvent"))
            {
                AddMouseMovement(steps, delay, macroEvent);
            }
            else if (Child(macroEvent, "KeyEvent") is { } keyEvent)
            {
                var (input, isDown) = ParseKeyEvent(macroEvent, keyEvent);
                AddInput(steps, delay, input, isDown);
            }
            else if (Child(macroEvent, "MouseEvent") is { } mouseEvent)
            {
                var (input, isDown) = ParseMouseEvent(mouseEvent);
                AddInput(steps, delay, input, isDown);
            }
            else if (delay > 0)
            {
                AddStep(steps, new MacroStep
                {
                    Kind = MacroStepKind.Delay,
                    DelayMilliseconds = delay,
                });
            }
            else
            {
                var type = ParseOptionalInt(Child(macroEvent, "Type"), 0, "Type");
                throw new InvalidDataException(
                    $"Synapse macro event type {type} has no supported KeyEvent, MouseEvent, or Delay payload.");
            }
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

    private static (MacroInput Input, bool IsDown) ParseKeyEvent(
        XElement macroEvent,
        XElement keyEvent)
    {
        var makecode = ParseRequiredInt(Child(keyEvent, "Makecode"), "KeyEvent/Makecode");
        var state = ParseRequiredInt(Child(keyEvent, "State"), "KeyEvent/State");
        if (makecode is <= 0 or > 0xFF || state is not (0 or 1))
        {
            throw new InvalidDataException("Synapse key events need a 1-255 Makecode and State 0/1.");
        }

        var rawScan = ParseOptionalInt(Child(macroEvent, "ScanCode"), 0, "ScanCode");
        if (rawScan is < 0 or > 0xFFFF)
        {
            throw new InvalidDataException("Synapse ScanCode must be between 0 and 65535.");
        }

        var (virtualKey, scanCode, extended) = MapKey(makecode, rawScan);
        return (new MacroInput
        {
            Kind = MacroInputKind.Keyboard,
            VirtualKey = virtualKey,
            ScanCode = scanCode,
            Extended = extended,
        }, state == 0);
    }

    private static (MacroInput Input, bool IsDown) ParseMouseEvent(XElement mouseEvent)
    {
        var button = ParseRequiredInt(Child(mouseEvent, "MouseButton"), "MouseEvent/MouseButton");
        var state = ParseRequiredInt(Child(mouseEvent, "State"), "MouseEvent/State");
        if (button is < 1 or > 5 || state is not (0 or 1))
        {
            throw new InvalidDataException("Synapse mouse events need Button 1-5 and State 0/1.");
        }

        return (new MacroInput
        {
            Kind = MacroInputKind.MouseButton,
            MouseButton = button,
        }, state == 0);
    }

    private static void AddMouseMovement(List<MacroStep> steps, int delay, XElement container)
    {
        var points = new List<MacroMousePoint>();
        long offset = 0;
        foreach (var element in container.Elements()
                     .Where(element => element.Name.LocalName == "MouseMovementEvent"))
        {
            var pointDelay = ParseOptionalInt(
                Child(element, "Delay"),
                0,
                "MouseMovementEvent/Delay");
            if (pointDelay is < 0 or > MacroDefinition.MaxDelayMilliseconds)
            {
                throw new InvalidDataException(
                    $"Synapse mouse movement delay must be between 0 and {MacroDefinition.MaxDelayMilliseconds} ms.");
            }

            // Synapse stores the gap between points; playback expects the
            // elapsed time from the start of the movement.
            // shortcut: a movement longer than the existing 60 s step limit is
            // clamped, split steps if recordings ever exceed it.
            offset = Math.Min(offset + pointDelay, MacroDefinition.MaxDelayMilliseconds);
            points.Add(new MacroMousePoint
            {
                X = ParseRequiredInt(Child(element, "X"), "MouseMovementEvent/X"),
                Y = ParseRequiredInt(Child(element, "Y"), "MouseMovementEvent/Y"),
                OffsetMilliseconds = (int)offset,
            });
        }

        if (points.Count == 0)
        {
            throw new InvalidDataException("Synapse mouse movement contains no points.");
        }
        if (points.Count > MacroStep.MaxMousePathPoints)
        {
            throw new InvalidDataException(
                $"Synapse mouse movement exceeds the {MacroStep.MaxMousePathPoints}-point limit.");
        }

        AddStep(steps, new MacroStep
        {
            Kind = MacroStepKind.MouseMove,
            DelayMilliseconds = delay,
            MouseMoveMode = MacroMouseMoveMode.Absolute,
            MousePath = points,
        });
    }

    private static void AddInput(
        List<MacroStep> steps,
        int delay,
        MacroInput input,
        bool isDown)
    {
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

    private static void AddStep(List<MacroStep> steps, MacroStep step)
    {
        if (steps.Count >= MacroDefinition.MaxSteps)
        {
            throw new InvalidDataException(
                $"Synapse macro exceeds the {MacroDefinition.MaxSteps}-step limit.");
        }

        steps.Add(step);
    }

    private static (int VirtualKey, int ScanCode, bool Extended) MapKey(int makecode, int rawScan)
    {
        var scanCode = rawScan & 0xFF;
        var virtualKey = makecode;
        var extended = rawScan > 0xFF;

        // Synapse stores generic modifier virtual keys and distinguishes the
        // physical side with ScanCode (right Shift is encoded as 256 + 54).
        if (makecode == 0x10)
        {
            virtualKey = scanCode == 54 ? 0xA1 : 0xA0;
            extended = false;
        }
        else if (makecode == 0x11)
        {
            virtualKey = rawScan > 0xFF ? 0xA3 : 0xA2;
        }
        else if (makecode == 0x12)
        {
            virtualKey = rawScan > 0xFF ? 0xA5 : 0xA4;
        }

        return (virtualKey, scanCode, extended);
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
}
