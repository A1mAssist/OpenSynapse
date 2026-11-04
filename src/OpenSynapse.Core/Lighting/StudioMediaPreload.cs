namespace OpenSynapse.Core.Lighting;

/// <summary>
/// Selects every Generate layer and region that needs a media sampler. Groups
/// are traversed so preloading matches <see cref="StudioProfile.EnumerateLayers"/>.
/// </summary>
public static class StudioMediaPreload
{
    public static IEnumerable<StudioEffectParams> EnumerateGenerateParams(StudioProfile? profile)
    {
        if (profile is null) yield break;

        foreach (var layer in profile.EnumerateLayers())
        {
            if (layer.IsGroup) continue;
            if (layer.Params.Effect == StudioEffectKind.ChromaGenerate) yield return layer.Params;
            foreach (var region in layer.Regions)
            {
                if (region.Params.Effect == StudioEffectKind.ChromaGenerate) yield return region.Params;
            }
        }
    }
}
