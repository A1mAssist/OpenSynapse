using System.Globalization;
using System.Xml.Linq;

namespace OpenSynapse.Core.Macros;

internal static partial class MacroSynapseXml
{
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
