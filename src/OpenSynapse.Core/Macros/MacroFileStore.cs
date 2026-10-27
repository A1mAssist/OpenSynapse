using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenSynapse.Core.Macros;

public static class MacroFileStore
{
    public const int CurrentVersion = 2;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.General)
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static Task ExportAsync(
        MacroDefinition macro,
        string filePath,
        CancellationToken cancellationToken = default) =>
        ExportAsync(macro, null, filePath, cancellationToken);

    public static async Task ExportAsync(
        MacroDefinition macro,
        IReadOnlyDictionary<string, MacroDefinition>? macros,
        string filePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(macro);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        if (!ValidateDefinition(macro))
        {
            throw new InvalidDataException(
                "Macro contains an incomplete or invalid action.");
        }

        var bundle = CollectBundle(macro, macros);
        var fullPath = Path.GetFullPath(filePath);
        var envelope = bundle.Count == 1
            ? new MacroFile { Version = 1, Macro = bundle[0] }
            : new MacroFile { Version = CurrentVersion, Macros = bundle.ToList() };

        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = new FileStream(
            fullPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            options: FileOptions.Asynchronous);
        await JsonSerializer.SerializeAsync(stream, envelope, SerializerOptions, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    public static async Task<MacroDefinition> ImportAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        var macros = await ImportBundleAsync(filePath, cancellationToken);
        return macros[0];
    }

    public static async Task<IReadOnlyList<MacroDefinition>> ImportBundleAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        var fullPath = Path.GetFullPath(filePath);
        if (string.Equals(Path.GetExtension(fullPath), ".xml", StringComparison.OrdinalIgnoreCase))
        {
            return await MacroSynapseXml.ImportAsync(fullPath, cancellationToken);
        }

        try
        {
            await using var stream = new FileStream(
                fullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                options: FileOptions.SequentialScan | FileOptions.Asynchronous);
            var envelope = await JsonSerializer.DeserializeAsync<MacroFile>(
                stream,
                SerializerOptions,
                cancellationToken);
            if (envelope is null)
            {
                throw new InvalidDataException("Macro file is empty.");
            }
            if (envelope.Version is not (1 or CurrentVersion))
            {
                throw new InvalidDataException(
                    $"Macro version {envelope.Version} is not supported; expected 1 or {CurrentVersion}.");
            }

            List<MacroDefinition>? source;
            if (envelope.Version == 1)
            {
                source = envelope.Macro is null ? null : [envelope.Macro];
            }
            else
            {
                source = envelope.Macros;
            }

            if (source is null || source.Count == 0)
            {
                throw new InvalidDataException("Macro file is empty.");
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            var macros = new List<MacroDefinition>(source.Count);
            foreach (var definition in source)
            {
                if (definition is null ||
                    !ValidateDefinition(definition) ||
                    string.IsNullOrWhiteSpace(definition.Id) ||
                    !ids.Add(definition.Id))
                {
                    throw new InvalidDataException(
                        "Macro file contains an incomplete, invalid, or duplicate macro.");
                }

                var macro = definition.Clone();
                macro.ApplySafeDefaults();
                if (string.IsNullOrWhiteSpace(macro.Name))
                {
                    macro.Name = "Imported macro";
                }

                macros.Add(macro);
            }

            return macros;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Macro file contains invalid JSON.", exception);
        }
        catch (NotSupportedException exception)
        {
            throw new InvalidDataException("Macro file contains unsupported data.", exception);
        }
    }

    private static IReadOnlyList<MacroDefinition> CollectBundle(
        MacroDefinition root,
        IReadOnlyDictionary<string, MacroDefinition>? macros)
    {
        var bundle = new List<MacroDefinition>();
        var added = new HashSet<string>(StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal);

        void Visit(MacroDefinition macro)
        {
            if (!visiting.Add(macro.Id))
            {
                throw new InvalidOperationException(
                    $"Macro reference cycle includes \"{macro.Name}\".");
            }

            try
            {
                if (added.Add(macro.Id))
                {
                    bundle.Add(macro.Clone());
                }

                foreach (var step in EnumerateSteps(macro.Steps))
                {
                    if (step.Kind != MacroStepKind.MacroReference ||
                        string.IsNullOrWhiteSpace(step.MacroId))
                    {
                        continue;
                    }

                    if (macros is null ||
                        !macros.TryGetValue(step.MacroId, out var referenced))
                    {
                        throw new InvalidOperationException(
                            $"Macro reference not found: {step.MacroId}.");
                    }
                    if (!ValidateDefinition(referenced))
                    {
                        throw new InvalidOperationException(
                            $"Referenced macro is incomplete: {referenced.Name}.");
                    }

                    Visit(referenced);
                }
            }
            finally
            {
                visiting.Remove(macro.Id);
            }
        }

        Visit(root);
        return bundle;
    }

    private static IEnumerable<MacroStep> EnumerateSteps(
        IReadOnlyList<MacroStep>? steps)
    {
        if (steps is null)
        {
            yield break;
        }

        foreach (var step in steps)
        {
            yield return step;
            if (step.Kind == MacroStepKind.Loop)
            {
                foreach (var child in EnumerateSteps(step.Children))
                {
                    yield return child;
                }
            }
        }
    }

    private static bool ValidateDefinition(MacroDefinition macro)
    {
        if (string.IsNullOrWhiteSpace(macro.Id) ||
            macro.Id.Length > 64 ||
            !Enum.IsDefined(macro.PlaybackMode))
        {
            return false;
        }

        if (macro.Trigger is { } trigger &&
            (!Enum.IsDefined(trigger.Mode) ||
             (trigger.Modifiers & ~MacroModifierKeys.All) != 0 ||
             trigger.Input is not { } triggerInput ||
             triggerInput.IsModifierKey ||
             !triggerInput.IsValid(
                 allowPrimaryMouseButton: false,
                 allowWheel: false,
                 allowNagaSideButton: true,
                 allowDeviceButton: true)))
        {
            return false;
        }

        var count = 0;
        return ValidateSteps(macro.Steps, depth: 0, ref count);
    }

    private static bool ValidateSteps(
        IReadOnlyList<MacroStep>? steps,
        int depth,
        ref int count)
    {
        if (steps is null || depth > MacroDefinition.MaxNestingDepth)
        {
            return false;
        }

        foreach (var step in steps)
        {
            if (step is null ||
                ++count > MacroDefinition.MaxSteps ||
                (step.Kind == MacroStepKind.Loop && step.Children is null) ||
                (step.Kind == MacroStepKind.MouseMove && step.MousePath is null) ||
                (step.Kind == MacroStepKind.MacroReference &&
                    (string.IsNullOrWhiteSpace(step.MacroId) ||
                     step.MacroId.Length > 64)) ||
                !step.IsValid())
            {
                return false;
            }

            if (step.Kind == MacroStepKind.MouseMove &&
                !Enum.IsDefined(step.MouseMoveMode))
            {
                return false;
            }

            if (step.Kind == MacroStepKind.Loop &&
                !ValidateSteps(step.Children, depth + 1, ref count))
            {
                return false;
            }
        }

        return true;
    }

    private sealed class MacroFile
    {
        public int Version { get; set; } = CurrentVersion;
        public MacroDefinition? Macro { get; set; }
        public List<MacroDefinition>? Macros { get; set; }
    }
}
