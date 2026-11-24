using System.Text.Json;

namespace OpenSynapse.Core.Lighting;

public readonly record struct RenderModuleInfo(string Id, string Name);

public readonly record struct RenderModuleParameter(
    string Name, JsonValueKind Kind, JsonElement Default, double Minimum = 0, double Maximum = 1, bool Integer = false,
    IReadOnlyList<string>? Choices = null);

/// <summary>One reusable renderer factory. Configuration is parsed once by <see cref="Create"/>.</summary>
public interface IRenderModuleFactory
{
    RenderModuleInfo Info { get; }

    IRenderModule Create(RenderModuleParams parameters);
}

/// <summary>A configured renderer bound to one preset layer.</summary>
public interface IRenderModule
{
    void Render(in RenderModuleContext context);
}

/// <summary>Per-frame inputs for a module. <see cref="Output"/> is reused by the preset engine.</summary>
public readonly record struct RenderModuleContext(
    int Rows,
    int Columns,
    double Seconds,
    StudioAudioFrame? Audio,
    StudioKeyState? Keys,
    Func<int, int, StudioRgb>? ScreenSampler,
    Func<double, double, StudioRgb>? ScreenSamplerUv,
    StudioPalette Palette,
    StudioRgb[] Output,
    StudioEffectState State,
    StudioSystemInput? System = null);

/// <summary>
/// Typed JSON parameters. Unknown keys are rejected by <see cref="EnsureKnown"/>, so a
/// typo in a preset fails at load time instead of silently rendering a default.
/// </summary>
public sealed class RenderModuleParams
{
    private readonly JsonElement _element;
    private readonly HashSet<string> _read = new(StringComparer.Ordinal);
    private readonly List<RenderModuleParameter> _fields = new();

    public IReadOnlyList<RenderModuleParameter> Fields => _fields;

    internal RenderModuleParams(JsonElement element, string moduleId)
    {
        _element = element.ValueKind == JsonValueKind.Object ? element.Clone() : default;
        ModuleId = moduleId;
    }

    public string ModuleId { get; }

    public static RenderModuleParams Empty(string moduleId) => new(default, moduleId);

    public bool Boolean(string name, bool fallback)
    {
        _fields.Add(new(name, JsonValueKind.True, JsonSerializer.SerializeToElement(fallback)));
        if (!TryGet(name, out var value)) return fallback;
        if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw Invalid(name, "must be true or false");
        }

        return value.GetBoolean();
    }

    public int Integer(string name, int fallback, int minimum, int maximum)
    {
        _fields.Add(new(name, JsonValueKind.Number, JsonSerializer.SerializeToElement(fallback), minimum, maximum, true));
        if (!TryGet(name, out var value)) return fallback;
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var number) ||
            number != Math.Floor(number))
        {
            throw Invalid(name, "must be an integer");
        }

        if (number < minimum || number > maximum)
        {
            throw Invalid(name, $"must be between {minimum} and {maximum}");
        }

        return (int)number;
    }

    public double Number(string name, double fallback, double minimum, double maximum)
    {
        _fields.Add(new(name, JsonValueKind.Number, JsonSerializer.SerializeToElement(fallback), minimum, maximum));
        if (!TryGet(name, out var value)) return fallback;
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var number) ||
            !double.IsFinite(number))
        {
            throw Invalid(name, "must be a finite number");
        }

        if (number < minimum || number > maximum)
        {
            throw Invalid(name, $"must be between {minimum} and {maximum}");
        }

        return number;
    }

    public string Text(string name, string fallback)
    {
        _fields.Add(new(name, JsonValueKind.String, JsonSerializer.SerializeToElement(fallback)));
        if (!TryGet(name, out var value)) return fallback;
        if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw Invalid(name, "must be a non-empty string");
        }

        return value.GetString()!;
    }

    public StudioRgb Color(string name, StudioRgb fallback)
    {
        _fields.Add(new(name, JsonValueKind.String, JsonSerializer.SerializeToElement(fallback.ToHex())));
        if (!TryGet(name, out var value)) return fallback;
        if (value.ValueKind != JsonValueKind.String)
        {
            throw Invalid(name, "must be a #RRGGBB string");
        }

        try
        {
            return StudioRgb.FromHex(value.GetString()!);
        }
        catch (FormatException)
        {
            throw Invalid(name, "must be a #RRGGBB string");
        }
    }

    public string Choice(string name, string fallback, params string[] choices)
    {
        var value = Text(name, fallback);
        _fields[^1] = _fields[^1] with { Choices = Array.AsReadOnly(choices) };
        if (!choices.Contains(value, StringComparer.Ordinal))
            throw Invalid(name, $"must be one of {string.Join(", ", choices)}");
        return value;
    }

    /// <summary>Rejects parameters this module did not read, including misspelled keys.</summary>
    public void EnsureKnown()
    {
        if (_element.ValueKind != JsonValueKind.Object) return;
        foreach (var property in _element.EnumerateObject())
        {
            if (!_read.Contains(property.Name))
            {
                throw Invalid(property.Name, "is not a parameter of this module");
            }
        }
    }

    private bool TryGet(string name, out JsonElement value)
    {
        if (_element.ValueKind == JsonValueKind.Object && _element.TryGetProperty(name, out value))
        {
            _read.Add(name);
            return true;
        }

        value = default;
        return false;
    }

    private InvalidDataException Invalid(string name, string message) =>
        new($"Module '{ModuleId}' parameter '{name}' {message}.");
}
