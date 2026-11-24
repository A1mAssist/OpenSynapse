using System.Text.Json;

namespace OpenSynapse.Core.Lighting;

public enum StudioPresetKind
{
    Music,
    Reactive,
    Screen,
    System,
    Ambient,
}

/// <summary>One JSON preset: one or more module layers rendered in order.</summary>
public sealed class StudioPresetConfig
{
    private readonly IReadOnlyList<StudioPresetLayer> _layers = [];

    public required string Id { get; init; }

    public required string Name { get; init; }

    public required string Category { get; init; }

    public required StudioPresetKind Kind { get; init; }

    public required IReadOnlyList<StudioPresetLayer> Layers
    {
        get => _layers;
        init => _layers = Array.AsReadOnly(value.ToArray());
    }

    public string? Note { get; init; }
}

public sealed class StudioPresetLayer
{
    internal StudioPresetLayer(
        string moduleId,
        RenderModuleInfo moduleInfo,
        IRenderModule renderer,
        StudioBlendMode blend,
        double opacity,
        StudioPalette palette,
        double speed,
        string stateKey,
        JsonElement parameters)
    {
        ModuleId = moduleId;
        ModuleInfo = moduleInfo;
        Renderer = renderer;
        Blend = blend;
        Opacity = opacity;
        RenderPalette = new StudioPalette(palette.Name, palette.Stops);
        Speed = speed;
        StateKey = stateKey;
        Parameters = parameters.Clone();
    }

    public string ModuleId { get; }

    public RenderModuleInfo ModuleInfo { get; }

    public StudioBlendMode Blend { get; }

    /// <summary>0..1 multiplier applied when this layer is composited.</summary>
    public double Opacity { get; }

    // StudioPalette exposes its backing array; keep the compiled definition private.
    public StudioPalette Palette => new(RenderPalette.Name, RenderPalette.Stops);

    internal StudioPalette RenderPalette { get; }

    /// <summary>Only module parameters; palette, speed and blend belong to the layer.</summary>
    public JsonElement Parameters { get; }

    public double Speed { get; }

    internal IRenderModule Renderer { get; }

    internal string StateKey { get; }
}
