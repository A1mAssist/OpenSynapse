using System.Text.Json;

namespace OpenSynapse.Core.Lighting;

public readonly record struct StudioPresetValidationIssue(int LayerIndex, string Field, string Message)
{
    public override string ToString() =>
        LayerIndex >= 0 ? $"Layer {LayerIndex + 1} {Field}: {Message}" : $"{Field}: {Message}";
}

/// <summary>Structural, module-parameter, and small render checks for one draft.</summary>
public static class StudioPresetValidator
{
    public static IReadOnlyList<StudioPresetValidationIssue> Validate(
        StudioPresetDraft draft,
        bool renderSmoke = false)
    {
        var issues = new List<StudioPresetValidationIssue>();
        if (draft is null)
        {
            issues.Add(new(-1, "preset", "is required"));
            return issues;
        }

        var id = draft.Id?.Trim() ?? string.Empty;
        if (id.Length == 0)
        {
            issues.Add(new(-1, "id", "must not be empty"));
        }
        else if (!id.All(character =>
                     character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-'))
        {
            issues.Add(new(-1, "id", "must use lowercase letters, digits and '-'"));
        }

        if (string.IsNullOrWhiteSpace(draft.Name))
        {
            issues.Add(new(-1, "name", "must not be empty"));
        }

        if (draft.Layers.Count is < 1 or > 4)
        {
            issues.Add(new(-1, "layers", "must contain 1 to 4 layers"));
        }

        for (var index = 0; index < draft.Layers.Count; index++)
        {
            var layer = draft.Layers[index];
            var moduleId = layer.ModuleId?.Trim() ?? string.Empty;
            if (!RenderModuleRegistry.TryGet(moduleId, out _))
            {
                issues.Add(new(index, "module", moduleId.Length == 0
                    ? "must not be empty"
                    : $"'{moduleId}' is not a render module"));
            }
            else
            {
                try
                {
                    RenderModuleRegistry.Describe(moduleId, JsonSerializer.SerializeToElement(layer.Params));
                }
                catch (Exception exception) when (exception is InvalidDataException or FormatException or ArgumentException)
                {
                    issues.Add(new(index, "params", exception.Message));
                }
            }

            if (layer.Palette.Count is < 1 or > 10)
            {
                issues.Add(new(index, "palette", "must contain 1 to 10 color stops"));
            }
            else if (layer.Palette.Any(stop => stop.Stop is < 0 or > 100))
            {
                issues.Add(new(index, "palette", "stop positions must be 0 to 100"));
            }

            if (!double.IsFinite(layer.Opacity) || layer.Opacity is < 0 or > 1)
            {
                issues.Add(new(index, "opacity", "must be between 0 and 1"));
            }

            if (!double.IsFinite(layer.Speed) || layer.Speed is < 0.1 or > 5)
            {
                issues.Add(new(index, "speed", "must be between 0.1 and 5"));
            }

            if (!Enum.IsDefined(layer.Blend))
            {
                issues.Add(new(index, "blend", "is not a supported blend mode"));
            }
        }

        if (issues.Count == 0 && renderSmoke)
        {
            try
            {
                var preset = StudioPresetCompiler.Compile(draft);
                var input = StudioPresetPreviewSimulation.Create(0.5, preset.Layers, null);
                StudioPresetEngine.Render(preset, 6, 22, 0.5, input);
                StudioPresetEngine.Render(preset, 96, 96, 0.5, input);
            }
            catch (Exception exception) when (exception is InvalidDataException or ArgumentException or
                InvalidOperationException or IndexOutOfRangeException)
            {
                issues.Add(new(-1, "render", exception.Message));
            }
        }

        return issues;
    }
}
