namespace OpenSynapse.Core.Lighting;

/// <summary>Turns a scene into layer frames or a throwaway profile for the world canvas.</summary>
public static class StudioSceneRenderer
{
    public static StudioEffectParams BuildParams(StudioSceneLayer layer)
    {
        var parameters = new StudioEffectParams
        {
            Effect = layer.Effect,
            Primary = layer.Primary,
            Secondary = layer.Secondary,
            DurationMs = layer.DurationMs,
            Speed = layer.Speed,
            Angle = layer.Angle,
            Width = layer.Width,
        };

        if (layer.Stops is { Count: > 0 } stops)
        {
            var step = stops.Count == 1 ? 0 : 100d / (stops.Count - 1);
            for (var index = 0; index < stops.Count; index++)
            {
                parameters.ColorStops.Add(new StudioColorStop((int)Math.Round(index * step), stops[index]));
            }
        }

        // A scene layer either names a preset or falls back to a built-in effect.
        if (layer.PresetId is { Length: > 0 } presetId &&
            StudioPresetCatalog.Default.Find(presetId) is { } preset)
        {
            parameters.Effect = StudioEffectKind.Preset;
            parameters.PresetDefinition = preset;
        }

        return parameters;
    }

    /// <summary>
    /// Layers for one target. A role with no dedicated layer falls back to the
    /// keyboard stack, so a scene always paints something.
    /// </summary>
    public static StudioLayerRender[] Render(StudioScene scene, StudioSceneRole role)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var stack = Collect(scene, role);
        if (stack.Count == 0) stack = Collect(scene, StudioSceneRole.Keyboard);
        return stack.ToArray();
    }

    /// <summary>
    /// Scene as a profile whose empty regions cover the whole world canvas;
    /// device placements are copied from the user profile so the shared
    /// geometry stays what the user arranged.
    /// </summary>
    public static StudioProfile BuildProfile(StudioScene scene, StudioProfile? template, string? title = null)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var name = string.IsNullOrWhiteSpace(title) ? scene.NameKey : title;
        var profile = new StudioProfile(name);
        if (template is not null)
        {
            // The scene profile is disposable; editing it must not move the user's template.
            foreach (var placement in template.Devices) profile.Devices.Add(placement.Clone());
        }

        foreach (var layer in scene.Layers)
        {
            var item = StudioLayerItem.CreateLayer(name, layer.Effect);
            item.Id = layer.TimebaseId;
            item.Params = BuildParams(layer);
            item.Opacity = layer.Opacity;
            item.Blend = layer.Blend;
            profile.Items.Add(item);
        }

        return profile;
    }

    private static List<StudioLayerRender> Collect(StudioScene scene, StudioSceneRole role)
    {
        var stack = new List<StudioLayerRender>();
        foreach (var layer in scene.Layers)
        {
            if (!layer.Roles.Contains(role)) continue;
            stack.Add(new StudioLayerRender(
                BuildParams(layer), null, layer.Opacity, layer.Blend, true, layer.TimebaseId));
        }

        return stack;
    }
}
