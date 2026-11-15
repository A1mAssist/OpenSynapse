using System.Xml;
using System.Xml.Linq;

namespace OpenSynapse.Core.Macros;

internal static partial class MacroSynapseXml
{
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
}
